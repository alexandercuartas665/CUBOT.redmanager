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

// DataProtection compartido con Web (:5036): mismo ApplicationName + misma carpeta de llaves
// permiten descifrar la cookie de auth en ambos hosts.
var dpKeysDir = System.IO.Path.Combine(builder.Environment.ContentRootPath, "..", "..", ".dp-keys");
System.IO.Directory.CreateDirectory(dpKeysDir);
builder.Services.AddDataProtection()
    .SetApplicationName("cubot.redmanager")
    .PersistKeysToFileSystem(new System.IO.DirectoryInfo(dpKeysDir));

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
    });

builder.Services.AddAuthorizationBuilder()
    .AddPolicy("PlatformOperator", p => p.RequireClaim("platform_role"))
    .AddPolicy("SuperAdminOnly", p => p.RequireClaim("platform_role", "SuperAdmin"));

var app = builder.Build();

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
    return Results.Redirect("http://localhost:5036/login");
}).DisableAntiforgery();

app.Run();
