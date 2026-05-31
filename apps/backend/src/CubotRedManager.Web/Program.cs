using System.Security.Claims;
using CubotRedManager.Application.Abstractions;
using CubotRedManager.Infrastructure;
using CubotRedManager.Infrastructure.Persistence;
using CubotRedManager.Web.Auth;
using CubotRedManager.Web.Components;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Persistencia (PostgreSQL) + servicios de Application portados.
builder.Services.AddInfrastructure(builder.Configuration);

// Resolucion de agencia/usuario desde los claims de la cookie.
builder.Services.AddScoped<HttpTenantContext>();
builder.Services.AddScoped<ITenantProvider>(sp => sp.GetRequiredService<HttpTenantContext>());
builder.Services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<HttpTenantContext>());

// Estado de autenticacion en cascada para los componentes (AuthorizeView, AuthorizeRouteView).
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddHttpContextAccessor();

builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/login";
        options.AccessDeniedPath = "/login";
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
    });

// Politicas alineadas a la familia CUBOT (ver SuperAdmin de travels).
builder.Services.AddAuthorizationBuilder()
    .AddPolicy(CubotRedManager.Web.Authorization.AppPolicies.PlatformOperator, p => p.RequireClaim("platform_role"))
    .AddPolicy(CubotRedManager.Web.Authorization.AppPolicies.TenantMember, p => p.RequireClaim("tenant_id"))
    .AddPolicy(CubotRedManager.Web.Authorization.AppPolicies.TenantAdmin, p => p.RequireClaim("tenant_role", "Owner", "Admin"));

var app = builder.Build();

// Aplica migraciones pendientes al arrancar (dev). En produccion se controla aparte.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<CubotRedManagerDbContext>();
    await db.Database.MigrateAsync();

    // Siembra la agencia demo (mismo GUID que emite el dev-login) para que las entidades
    // tenant-scoped con FK a Tenant (Client, etc.) puedan persistir en desarrollo.
    if (!await db.Tenants.AnyAsync(t => t.Id == DemoTenant.Id))
    {
        db.Tenants.Add(new CubotRedManager.Domain.Entities.Tenant
        {
            Id = DemoTenant.Id,
            Name = "Studio Marketing (demo)",
            Status = CubotRedManager.Domain.Enums.TenantStatus.Active,
            Kind = CubotRedManager.Domain.Enums.TenantKind.Demo
        });
        await db.SaveChangesAsync();
    }
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

// ===== Login de desarrollo (temporal) =====
// Emite los claims de la familia (tenant_id, tenant_role, agency_name) sin backend de identidad.
// Se reemplaza por el login real (cuentas + contrasena + Google) con el modulo de Usuarios/Onboarding.
app.MapPost("/auth/dev-login", async (HttpContext http, [FromForm] string role) =>
{
    var isAdmin = string.Equals(role, "admin", StringComparison.OrdinalIgnoreCase);
    var tenantRole = isAdmin ? "Admin" : "Operator";
    var displayName = isAdmin ? "Admin Demo" : "Operador Demo";

    var claims = new List<Claim>
    {
        new(ClaimTypes.NameIdentifier, Guid.CreateVersion7().ToString()),
        new(ClaimTypes.Name, displayName),
        new("tenant_id", DemoTenant.Id.ToString()),
        new("tenant_role", tenantRole),
        new("agency_name", "Studio Marketing (demo)")
    };

    var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
    await http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));
    return Results.Redirect("/dashboard");
}).DisableAntiforgery();

app.MapPost("/auth/logout", async (HttpContext http) =>
{
    await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.Redirect("/login");
}).DisableAntiforgery();

app.Run();

/// <summary>Agencia demo del dev-login (se elimina con el modulo de identidad real).</summary>
static class DemoTenant
{
    public static readonly Guid Id = Guid.Parse("0192a000-0000-7000-8000-000000000001");
}
