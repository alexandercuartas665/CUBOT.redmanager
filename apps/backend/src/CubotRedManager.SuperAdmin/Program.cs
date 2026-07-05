using CubotRedManager.Application.Abstractions;
using CubotRedManager.Application.Common.Auth;
using CubotRedManager.Infrastructure;
using CubotRedManager.Infrastructure.Persistence;
using CubotRedManager.SuperAdmin.Auth;
using CubotRedManager.SuperAdmin.Components;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddInfrastructure(builder.Configuration);

// Resolver de uploads (mismo que el Web; SuperAdmin no publica pero AddInfrastructure registra
// el publisher que depende de esta abstraccion). Apunta al WebRootPath aunque no exista — el
// SuperAdmin no resolvera URLs porque no muestra publicaciones.
builder.Services.AddSingleton<IUploadPathResolver>(_ =>
    new CubotRedManager.Infrastructure.Storage.StaticPathUploadPathResolver(builder.Environment.WebRootPath ?? builder.Environment.ContentRootPath));

builder.Services.AddCascadingAuthenticationState();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<HttpTenantContext>();
builder.Services.AddScoped<ITenantProvider>(sp => sp.GetRequiredService<HttpTenantContext>());
builder.Services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<HttpTenantContext>());

// JWT settings (mismo secret que el Web para que la cookie sea verificable en ambos hosts).
builder.Services.Configure<JwtSettings>(builder.Configuration.GetSection(JwtSettings.SectionName));

// DataProtection compartido con Web: mismo ApplicationName + llaves en la tabla
// data_protection_keys de la MISMA base de datos. Asi la cookie emitida por el Web se valida
// aqui, en dev y en Railway (donde el filesystem es efimero).
builder.Services.AddDataProtection()
    .SetApplicationName("cubot.redmanager")
    .PersistKeysToDbContext<CubotRedManagerDbContext>();

// URL del Web (login unificado) para redirects (dev: localhost:5036; prod: red.cubot.com.co).
var webUrl = (builder.Configuration["Deployment:WebUrl"] ?? "http://localhost:5036").TrimEnd('/');

// Railway hace TLS termination antes del contenedor (ver comentario equivalente en el Web).
builder.Services.Configure<Microsoft.AspNetCore.Builder.ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor
        | Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        // Cookie compartida con Web (:5036): mismo nombre y misma DataProtection. El login real
        // vive en el Web; el SuperAdmin solo redirige al Web cuando falta la cookie.
        options.Cookie.Name = ".cubot.auth";
        options.Cookie.Path = "/";
        options.LoginPath = "/login";           // pagina local que hace meta-refresh al Web
        options.AccessDeniedPath = "/login";
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
        // Cookie compartida entre red.cubot.com.co y admin.red.cubot.com.co en produccion.
        if (builder.Environment.IsProduction())
        {
            options.Cookie.Domain = ".cubot.com.co";
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        }
    });

builder.Services.AddAuthorizationBuilder()
    .AddPolicy("PlatformOperator", p => p.RequireClaim("platform_role"))
    .AddPolicy("SuperAdminOnly", p => p.RequireClaim("platform_role", "SuperAdmin"));

var app = builder.Build();

// ForwardedHeaders antes de cualquier middleware que mire el scheme (ver Web).
app.UseForwardedHeaders();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<CubotRedManagerDbContext>();
    await db.Database.MigrateAsync();
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// SuperAdmin ya NO hospeda un login propio: el login unificado vive en el Web (:5036).
// El boton "Salir" sigue siendo local para limpiar la cookie y volver al login del Web.
app.MapPost("/auth/logout", async (HttpContext http) =>
{
    await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.Redirect($"{webUrl}/login");
}).DisableAntiforgery();

// Healthcheck para Railway: app viva + Postgres accesible.
app.MapGet("/healthz", async (CubotRedManagerDbContext db) =>
{
    try
    {
        await db.Database.ExecuteSqlRawAsync("SELECT 1");
        return Results.Ok(new { status = "ok", ts = DateTimeOffset.UtcNow });
    }
    catch (Exception ex)
    {
        return Results.Problem($"db down: {ex.Message}", statusCode: 503);
    }
}).AllowAnonymous();

app.Run();
