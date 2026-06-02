using System.Security.Claims;
using CubotRedManager.Application.Abstractions;
using CubotRedManager.Application.Common.Auth;
using CubotRedManager.Domain.Enums;
using CubotRedManager.Infrastructure;
using CubotRedManager.Infrastructure.Persistence;
using CubotRedManager.Web.Auth;
using CubotRedManager.Web.Components;
using CubotRedManager.Web.Seed;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents()
    // Permite subir archivos grandes (videos hasta 60MB) via InputFile + SignalR.
    .AddHubOptions(o => o.MaximumReceiveMessageSize = 64 * 1024 * 1024);

// Persistencia (PostgreSQL) + servicios de Application portados.
builder.Services.AddInfrastructure(builder.Configuration);

// Resolucion de agencia/usuario desde los claims de la cookie (con override "ambient" para workers).
builder.Services.AddScoped<IAmbientTenantOverride, AmbientTenantOverride>();
builder.Services.AddScoped<HttpTenantContext>();
builder.Services.AddScoped<ITenantProvider>(sp => sp.GetRequiredService<HttpTenantContext>());
builder.Services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<HttpTenantContext>());
// Resolver URLs /uploads/... -> rutas absolutas en disco (para que el publisher lea los archivos).
builder.Services.AddSingleton<IUploadPathResolver>(_ =>
    new CubotRedManager.Infrastructure.Storage.StaticPathUploadPathResolver(builder.Environment.WebRootPath));

// Worker periodico: refresh proactivo de tokens + sync TikTok cada N minutos.
// Configurable via appsettings BackgroundJobs:TikTokMaintenance:Enabled (default: true).
var tkOpts = builder.Configuration.GetSection("BackgroundJobs:TikTokMaintenance").Get<CubotRedManager.Web.BackgroundJobs.TikTokMaintenanceOptions>()
             ?? new CubotRedManager.Web.BackgroundJobs.TikTokMaintenanceOptions();
builder.Services.AddSingleton(tkOpts);
if (tkOpts.Enabled)
{
    builder.Services.AddHostedService<CubotRedManager.Web.BackgroundJobs.TikTokMaintenanceWorker>();
}

// Estado de autenticacion en cascada para los componentes (AuthorizeView, AuthorizeRouteView).
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddHttpContextAccessor();

// JWT settings (login unificado emite tambien JWT propio si se necesita en futuras integraciones).
builder.Services.Configure<JwtSettings>(builder.Configuration.GetSection(JwtSettings.SectionName));

// DataProtection compartido con SuperAdmin (:5037): mismo ApplicationName + mismo PFS de llaves
// permiten descifrar la cookie de auth en ambos hosts. Sin esto, cada app rota su propio anillo
// y la cookie emitida en :5036 no se valida en :5037.
var dpKeysDir = System.IO.Path.Combine(builder.Environment.ContentRootPath, "..", "..", ".dp-keys");
System.IO.Directory.CreateDirectory(dpKeysDir);
builder.Services.AddDataProtection()
    .SetApplicationName("cubot.redmanager")
    .PersistKeysToFileSystem(new System.IO.DirectoryInfo(dpKeysDir));

builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        // Cookie compartida con SuperAdmin (:5037): mismo nombre y misma DataProtection (cubotrm)
        // permiten que ambos hosts (localhost:5036 y :5037) compartan la sesion del usuario.
        options.Cookie.Name = ".cubot.auth";
        options.Cookie.Path = "/";
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

    // Siembra usuarios demo (uno SuperAdmin de plataforma + uno Admin de la agencia demo)
    // con clave PBKDF2 real. Se elimina cuando aterrice el modulo de Onboarding.
    var hasher = scope.ServiceProvider.GetRequiredService<CubotRedManager.Application.Common.Auth.IPasswordHasher>();
    if (!await db.PlatformUsers.AnyAsync(u => u.Email == "admin@cubot.local"))
    {
        db.PlatformUsers.Add(new CubotRedManager.Domain.Entities.PlatformUser
        {
            Id = Guid.Parse("0192a000-0000-7000-8000-00000000a001"),
            Email = "admin@cubot.local",
            DisplayName = "Super Admin Demo",
            PasswordHash = hasher.Hash("admin123"),
            PlatformRole = CubotRedManager.Domain.Enums.PlatformRole.SuperAdmin,
            AuthProvider = CubotRedManager.Domain.Enums.AuthProvider.Local,
            EmailVerified = true
        });
        await db.SaveChangesAsync();
    }
    if (!await db.PlatformUsers.AnyAsync(u => u.Email == "operador@cubot.local"))
    {
        var operatorId = Guid.Parse("0192a000-0000-7000-8000-00000000a002");
        db.PlatformUsers.Add(new CubotRedManager.Domain.Entities.PlatformUser
        {
            Id = operatorId,
            Email = "operador@cubot.local",
            DisplayName = "Admin Agencia Demo",
            PasswordHash = hasher.Hash("demo123"),
            PlatformRole = null,
            AuthProvider = CubotRedManager.Domain.Enums.AuthProvider.Local,
            EmailVerified = true
        });
        await db.SaveChangesAsync();
        // Membresia en la agencia demo como Admin (rol del tenant).
        if (!await db.TenantUsers.IgnoreQueryFilters().AnyAsync(tu => tu.PlatformUserId == operatorId && tu.TenantId == DemoTenant.Id))
        {
            db.TenantUsers.Add(new CubotRedManager.Domain.Entities.TenantUser
            {
                TenantId = DemoTenant.Id,
                PlatformUserId = operatorId,
                TenantRole = CubotRedManager.Domain.Enums.TenantRole.Admin,
                Status = CubotRedManager.Domain.Enums.PlatformUserStatus.Active
            });
            await db.SaveChangesAsync();
        }
    }

    // Modelos demo del Contenedor de Datos (solo si NO existen ya).
    await DataContainerSeed.EnsureAsync(db, DemoTenant.Id);

    // Filas demo (20 productos + 20 precios) para alimentar al agente FUXION via MCP.
    // Solo siembra si el container existe y todavia esta vacio (idempotente).
    await DataContainerDataSeed.EnsureAsync(db, DemoTenant.Id);
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

// Sirve los archivos subidos por usuarios (wwwroot/uploads/...). La URL incluye un Guid v7 que
// hace inenumerable el path; en produccion mover a almacenamiento dedicado (S3) con firma.
app.UseStaticFiles();

app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// ===== Login unificado (Web :5036 hospeda el formulario real para SuperAdmin y Tenant) =====
// Portado verbatim del SuperAdmin de CUBOT.travels. Despues del login exitoso:
//   - Si el usuario es operador de plataforma (claim platform_role): redirige a SuperAdmin (:5037).
//   - Si es miembro de una agencia (claim tenant_id): queda en :5036/dashboard.
// ReturnUrl tiene prioridad si viene en la peticion (login iniciado desde SuperAdmin).
app.MapPost("/auth/login", async (
    HttpContext http,
    [FromForm] string email,
    [FromForm] string password,
    [FromForm] string? returnUrl,
    IApplicationDbContext db,
    IPasswordHasher hasher) =>
{
    var normalized = (email ?? string.Empty).Trim().ToLowerInvariant();
    var user = await db.PlatformUsers.FirstOrDefaultAsync(u => u.Email == normalized);

    if (user is null
        || string.IsNullOrEmpty(user.PasswordHash)
        || !hasher.Verify(user.PasswordHash, password ?? string.Empty))
    {
        // TODO redmanager: preservar returnUrl en el redirect de error cuando se ajuste la pagina.
        return Results.Redirect("/login?error=1");
    }
    // TODO redmanager: PlatformUser no tiene Status (vs travels). Cuando se agregue, validar Active.

    var claims = new List<Claim>
    {
        new(ClaimTypes.NameIdentifier, user.Id.ToString()),
        new(ClaimTypes.Name, user.DisplayName ?? user.Email),
        new(ClaimTypes.Email, user.Email)
    };

    var isOperator = user.PlatformRole is PlatformRole;
    if (isOperator)
    {
        claims.Add(new Claim("platform_role", user.PlatformRole!.Value.ToString()));
    }

    // Membresia de agencia: la resolvemos para TODOS los usuarios.
    var membership = await db.TenantUsers
        .IgnoreQueryFilters()
        .Where(tu => tu.PlatformUserId == user.Id && tu.Status == PlatformUserStatus.Active)
        .OrderBy(tu => tu.CreatedAt)
        .FirstOrDefaultAsync();

    if (!isOperator && membership is null)
    {
        // Identidad valida pero sin rol de plataforma ni membresia activa: sin acceso.
        return Results.Redirect("/login?error=1");
    }

    if (membership is not null)
    {
        claims.Add(new Claim("tenant_id", membership.TenantId.ToString()));
        claims.Add(new Claim("tenant_role", membership.TenantRole.ToString()));
    }

    var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
    await http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));

    // Decision de redireccion:
    //  1. Si llego returnUrl explicito (ej. el SuperAdmin redirigio aqui), respetarlo.
    //  2. Si el usuario es operador de plataforma, mandarlo al SuperAdmin (:5037).
    //  3. En otro caso, al dashboard del tenant en este mismo host.
    if (!string.IsNullOrWhiteSpace(returnUrl) && IsSafeReturnUrl(returnUrl))
    {
        return Results.Redirect(returnUrl);
    }
    if (isOperator)
    {
        return Results.Redirect("http://localhost:5037/dashboard");
    }
    return Results.Redirect("/dashboard");
}).DisableAntiforgery();

// Recuperar contrasena (autogestion): envia un enlace de reseteo por correo.
app.MapPost("/auth/forgot", async (
    HttpContext http,
    [FromForm] string email,
    CubotRedManager.Application.Auth.IPasswordResetService reset) =>
{
    var baseUrl = $"{http.Request.Scheme}://{http.Request.Host}";
    var result = await reset.RequestAsync(email, baseUrl);
    if (!result.Success)
    {
        return Results.Redirect($"/recuperar?error={Uri.EscapeDataString(result.Error ?? "No se pudo procesar la solicitud.")}");
    }
    return Results.Redirect("/recuperar?sent=1");
}).DisableAntiforgery();

// Aplica la nueva contrasena usando el token del enlace del correo.
app.MapPost("/auth/reset", async (
    [FromForm] string token,
    [FromForm] string password,
    CubotRedManager.Application.Auth.IPasswordResetService reset) =>
{
    var result = await reset.ResetAsync(token, password);
    if (!result.Success)
    {
        return Results.Redirect($"/restablecer?token={Uri.EscapeDataString(token)}&error={Uri.EscapeDataString(result.Error ?? "No se pudo restablecer la contrasena.")}");
    }
    return Results.Redirect("/login?reset=1");
}).DisableAntiforgery();

// Inicia el flujo OIDC con Google.
app.MapGet("/connect/google", async (
    HttpContext http,
    [FromQuery] string? mode,
    [FromQuery] string? agency,
    CubotRedManager.Application.Auth.IGoogleSignInService google) =>
{
    var redirectUri = $"{http.Request.Scheme}://{http.Request.Host}/signin-google";
    var state = Guid.NewGuid().ToString("N");
    var url = await google.BuildAuthorizeUrlAsync(redirectUri, state);
    if (url is null) { return Results.Redirect("/login?gerror=" + Uri.EscapeDataString("El ingreso con Google no esta habilitado.")); }

    var cookieOpts = new CookieOptions
    {
        HttpOnly = true,
        SameSite = SameSiteMode.Lax,
        Secure = http.Request.IsHttps,
        MaxAge = TimeSpan.FromMinutes(10),
        Path = "/"
    };
    http.Response.Cookies.Append("g_oauth_state", state, cookieOpts);

    var isSignup = string.Equals(mode, "signup", StringComparison.OrdinalIgnoreCase);
    if (isSignup && !string.IsNullOrWhiteSpace(agency))
    {
        http.Response.Cookies.Append("g_signup_agency", Uri.EscapeDataString(agency.Trim()), cookieOpts);
    }
    else
    {
        http.Response.Cookies.Delete("g_signup_agency");
    }
    return Results.Redirect(url);
}).AllowAnonymous();

// Callback de Google.
app.MapGet("/signin-google", async (
    HttpContext http,
    [FromQuery] string? code,
    [FromQuery] string? state,
    [FromQuery] string? error,
    CubotRedManager.Application.Auth.IGoogleSignInService google,
    IApplicationDbContext db) =>
{
    if (!string.IsNullOrEmpty(error) || string.IsNullOrEmpty(code))
    {
        return Results.Redirect("/login?gerror=" + Uri.EscapeDataString("No se completo el ingreso con Google."));
    }

    var expectedState = http.Request.Cookies["g_oauth_state"];
    http.Response.Cookies.Delete("g_oauth_state");

    var signupAgencyRaw = http.Request.Cookies["g_signup_agency"];
    http.Response.Cookies.Delete("g_signup_agency");
    var signupAgency = string.IsNullOrWhiteSpace(signupAgencyRaw) ? null : Uri.UnescapeDataString(signupAgencyRaw);

    if (string.IsNullOrEmpty(state) || !string.Equals(state, expectedState, StringComparison.Ordinal))
    {
        return Results.Redirect("/login?gerror=" + Uri.EscapeDataString("Sesion de ingreso invalida. Intenta de nuevo."));
    }

    var redirectUri = $"{http.Request.Scheme}://{http.Request.Host}/signin-google";
    var result = await google.ResolveAsync(code, redirectUri, signupAgency);
    if (!result.Success)
    {
        if (signupAgency is not null)
        {
            return Results.Redirect("/login?mode=signup&regerror=" + Uri.EscapeDataString(result.Error ?? "No se pudo crear la cuenta con Google."));
        }
        return Results.Redirect("/login?gerror=" + Uri.EscapeDataString(result.Error ?? "No se pudo iniciar sesion con Google."));
    }

    var claims = new List<Claim>
    {
        new(ClaimTypes.NameIdentifier, result.UserId.ToString()),
        new(ClaimTypes.Name, result.DisplayName ?? result.Email ?? string.Empty),
        new(ClaimTypes.Email, result.Email ?? string.Empty)
    };

    var isOperator = result.PlatformRole is not null;
    if (isOperator)
    {
        claims.Add(new Claim("platform_role", result.PlatformRole!));
    }

    if (result.TenantId is { } resultTenantId)
    {
        claims.Add(new Claim("tenant_id", resultTenantId.ToString()));
        claims.Add(new Claim("tenant_role", result.TenantRole ?? TenantRole.Owner.ToString()));
    }
    else if (isOperator)
    {
        var membership = await db.TenantUsers
            .IgnoreQueryFilters()
            .Where(tu => tu.PlatformUserId == result.UserId && tu.Status == PlatformUserStatus.Active)
            .OrderBy(tu => tu.CreatedAt)
            .FirstOrDefaultAsync();
        if (membership is not null)
        {
            claims.Add(new Claim("tenant_id", membership.TenantId.ToString()));
            claims.Add(new Claim("tenant_role", membership.TenantRole.ToString()));
        }
    }

    var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
    await http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));

    if (isOperator)
    {
        return Results.Redirect("http://localhost:5037/dashboard");
    }
    return Results.Redirect("/dashboard");
}).AllowAnonymous();

app.MapPost("/auth/logout", async (HttpContext http) =>
{
    await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.Redirect("/login");
}).DisableAntiforgery();

// ===== Endpoint DEV para probar MCP + agente FUXION =====
// Solo se monta en Development. Recibe { agent: "FUXION", message: "..." } y devuelve la
// respuesta del LLM ya con los placeholders MCP del prompt resueltos. Usa el AmbientTenantOverride
// para fijar el tenant demo sin necesidad de cookie.
if (app.Environment.IsDevelopment())
{
    app.MapPost("/dev/test-agent-mcp", async (
        HttpContext http,
        IServiceScopeFactory scopeFactory) =>
    {
        var body = await System.Text.Json.JsonDocument.ParseAsync(http.Request.Body);
        var agentName = body.RootElement.TryGetProperty("agent", out var aEl) ? aEl.GetString() : "FUXION";
        var message = body.RootElement.TryGetProperty("message", out var mEl) ? mEl.GetString() ?? "" : "";
        if (string.IsNullOrWhiteSpace(agentName) || string.IsNullOrWhiteSpace(message))
        {
            return Results.BadRequest(new { ok = false, error = "agent y message son obligatorios" });
        }

        using var scope = scopeFactory.CreateScope();
        scope.ServiceProvider.GetRequiredService<IAmbientTenantOverride>().Set(DemoTenant.Id, null);

        var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        var agent = await db.AiAgents.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Name == agentName);
        if (agent is null)
        {
            return Results.NotFound(new { ok = false, error = $"Agente '{agentName}' no existe en el tenant demo." });
        }

        var inference = scope.ServiceProvider.GetRequiredService<CubotRedManager.Application.Tenancy.IAiInferenceService>();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var result = await inference.TestChatAsync(
            agent.Id,
            new[] { new CubotRedManager.Application.Tenancy.AiChatTurn("user", message!) });
        sw.Stop();

        return Results.Json(new
        {
            ok = result.Ok,
            error = result.Error,
            text = result.Text,
            inputTokens = result.InputTokens,
            outputTokens = result.OutputTokens,
            elapsedMs = sw.ElapsedMilliseconds
        });
    }).AllowAnonymous().DisableAntiforgery();

    // Endpoint adicional: devuelve el prompt resuelto SIN llamar al LLM (util cuando no hay API key).
    app.MapGet("/dev/test-agent-mcp/prompt", async (
        string agent,
        IServiceScopeFactory scopeFactory) =>
    {
        if (string.IsNullOrWhiteSpace(agent)) { return Results.BadRequest(new { ok = false, error = "agent requerido" }); }
        using var scope = scopeFactory.CreateScope();
        scope.ServiceProvider.GetRequiredService<IAmbientTenantOverride>().Set(DemoTenant.Id, null);
        var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        var ag = await db.AiAgents.AsNoTracking().FirstOrDefaultAsync(a => a.Name == agent);
        if (ag is null) { return Results.NotFound(new { ok = false, error = "agente no existe" }); }
        var mcp = scope.ServiceProvider.GetRequiredService<CubotRedManager.Application.Tenancy.IDataContainerMcpService>();
        var resolved = await mcp.ResolvePlaceholdersAsync(ag.SystemPrompt, ag.EnableDataContainerMcp);
        return Results.Text(resolved, "text/plain; charset=utf-8");
    }).AllowAnonymous();
}

app.Run();

// Helper: solo permite redirigir a URLs locales o al host del SuperAdmin (:5037) para evitar
// open-redirect.
static bool IsSafeReturnUrl(string url)
{
    if (url.StartsWith("/", StringComparison.Ordinal)) { return true; }
    if (url.StartsWith("http://localhost:5037", StringComparison.OrdinalIgnoreCase)) { return true; }
    if (url.StartsWith("http://localhost:5036", StringComparison.OrdinalIgnoreCase)) { return true; }
    return false;
}

/// <summary>Agencia demo del dev-login (se elimina con el modulo de identidad real).</summary>
static class DemoTenant
{
    public static readonly Guid Id = Guid.Parse("0192a000-0000-7000-8000-000000000001");
}
