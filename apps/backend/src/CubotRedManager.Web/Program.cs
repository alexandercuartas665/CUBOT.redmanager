using System.Security.Claims;
using CubotRedManager.Application.Abstractions;
using CubotRedManager.Application.Common.Auth;
using CubotRedManager.Domain.Enums;
using CubotRedManager.Infrastructure;
using CubotRedManager.Infrastructure.Persistence;
using CubotRedManager.Web.Auth;
using CubotRedManager.Web.Components;
using CubotRedManager.Web.Endpoints;
using CubotRedManager.Web.Seed;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

// Nombre del scheme JWT dedicado a la Admin Agent API. Se declara aqui para que Program.cs
// (registro del scheme + policy) y los endpoints (RequireAuthorization(scheme)) usen la MISMA
// constante y no haya typos silenciosos.
const string SuperAdminJwtScheme = "SuperAdminJwt";

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

// Servicio de state OAuth para TikTok (firma tenant/cliente/actor en el state que viaja por TikTok
// y que /oauth/tiktok/callback recibe). Sin este servicio el operador tendria que pegar el code.
builder.Services.AddScoped<CubotRedManager.Web.Auth.TikTokOAuthStateService>();

// Worker periodico: refresh proactivo de tokens + sync TikTok cada N minutos.
// Configurable via appsettings BackgroundJobs:TikTokMaintenance:Enabled (default: true).
var tkOpts = builder.Configuration.GetSection("BackgroundJobs:TikTokMaintenance").Get<CubotRedManager.Web.BackgroundJobs.TikTokMaintenanceOptions>()
             ?? new CubotRedManager.Web.BackgroundJobs.TikTokMaintenanceOptions();
builder.Services.AddSingleton(tkOpts);
if (tkOpts.Enabled)
{
    builder.Services.AddHostedService<CubotRedManager.Web.BackgroundJobs.TikTokMaintenanceWorker>();
}

// AutoReply (Modulo 2.11). Worker que procesa comentarios pendientes segun la programacion
// configurada por cuenta (Frequency + ActiveHoursMask + ActiveDaysOfWeekMask) y genera respuesta
// via plantilla / IA segun Mode. Escribe AutoReplyJobLog para auditoria en la pagina de logs.
var arOpts = builder.Configuration.GetSection("AutoReply").Get<CubotRedManager.Web.BackgroundJobs.AutoReplyOptions>()
             ?? new CubotRedManager.Web.BackgroundJobs.AutoReplyOptions();
builder.Services.AddSingleton(arOpts);
builder.Services.AddHostedService<CubotRedManager.Web.BackgroundJobs.AutoReplyWorker>();

// FuxionPaymentMaintenance (Fase 3 pagos). Cada CheckInterval (4h) verifica el token FUXION de
// cada agente con Payment habilitado via /api/auth/user/verify-session y notifica al operador via
// TenantAlertConfig cuando el token esta por expirar o fue rechazado. Sin este worker, el user
// solo se entera de un token muerto cuando un cliente ya intento pagar y recibio el fallback.
var fxOpts = builder.Configuration.GetSection("FuxionPaymentMaintenance").Get<CubotRedManager.Web.BackgroundJobs.FuxionPaymentMaintenanceOptions>()
             ?? new CubotRedManager.Web.BackgroundJobs.FuxionPaymentMaintenanceOptions();
builder.Services.AddSingleton(fxOpts);
if (fxOpts.Enabled)
{
    builder.Services.AddHostedService<CubotRedManager.Web.BackgroundJobs.FuxionPaymentMaintenanceWorker>();
}

// AgentDispatchQueue: procesador en background del agente IA. Se registra como singleton triple
// (patron travels) para que el mismo objeto sea IAgentDispatchQueue (donde el ChatIngestService
// encola) y BackgroundService (donde .NET lo levanta como hosted service).
builder.Services.AddSingleton<CubotRedManager.Web.BackgroundJobs.AgentDispatchQueue>();
builder.Services.AddSingleton<CubotRedManager.Application.Tenancy.IAgentDispatchQueue>(sp =>
    sp.GetRequiredService<CubotRedManager.Web.BackgroundJobs.AgentDispatchQueue>());
builder.Services.AddHostedService(sp =>
    sp.GetRequiredService<CubotRedManager.Web.BackgroundJobs.AgentDispatchQueue>());

// Estado de autenticacion en cascada para los componentes (AuthorizeView, AuthorizeRouteView).
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddHttpContextAccessor();

// JWT settings (login unificado emite tambien JWT propio si se necesita en futuras integraciones).
builder.Services.Configure<JwtSettings>(builder.Configuration.GetSection(JwtSettings.SectionName));

// DataProtection compartido con SuperAdmin: mismo ApplicationName + llaves persistidas en la
// tabla data_protection_keys de Postgres. Sin esto, cada app rota su propio anillo y la cookie
// emitida en un host no se valida en el otro. En Railway el filesystem es efimero, por eso las
// llaves NO pueden vivir en disco (patron portado de CUBOT.travels).
builder.Services.AddDataProtection()
    .SetApplicationName("cubot.redmanager")
    .PersistKeysToDbContext<CubotRedManagerDbContext>();

// Nota deploy: el SuperAdmin dejo de ser una app aparte (Camino B, ADR 0003). La consola de
// plataforma vive en este mismo servicio bajo /admin/*, asi que ya no hace falta una URL externa
// para redirigir. La variable de entorno Deployment__SuperAdminUrl es historica; se ignora.

// Railway hace TLS termination antes del contenedor: sin ForwardedHeaders, ASP.NET cree que la
// request es HTTP y entra en bucle de redirects. KnownNetworks/Proxies se limpian porque el
// proxy de Railway no tiene IP fija conocida.
builder.Services.Configure<Microsoft.AspNetCore.Builder.ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor
        | Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

// Config JWT compartida (misma seccion "Jwt" que emite JwtTokenService).
var jwtSettings = builder.Configuration.GetSection(JwtSettings.SectionName).Get<JwtSettings>() ?? new JwtSettings();

builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        // Cookie de sesion del unico host (red.cubot.com.co). Antes se compartia con el subdominio
        // admin.red.cubot.com.co via Cookie.Domain=".cubot.com.co"; con el Camino B (ADR 0003) la
        // consola vive en /admin del mismo host, asi que no hace falta compartir dominio.
        options.Cookie.Name = ".cubot.auth";
        options.Cookie.Path = "/";
        options.LoginPath = "/login";
        options.AccessDeniedPath = "/login";
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
        if (builder.Environment.IsProduction())
        {
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        }
    })
    // Scheme JWT SOLO para la Admin Agent API cross-tenant. NO es el default (para no romper
    // Blazor Server / la consola /admin/* que sigue con Cookie). Las policies que lo necesitan
    // fuerzan AuthenticationSchemes=SuperAdminJwtScheme.
    //
    // Nota: apagamos el DefaultInboundClaimTypeMap ANTES de registrar el handler (mas abajo, statico)
    // para que los claims del JWT lleguen con sus nombres originales ("sub", "email", "is_super_admin")
    // y no como URIs largas (ClaimTypes.NameIdentifier, etc.). Sin esto, un endpoint que lea
    // http.User.FindFirstValue("sub") recibe null.
    .AddJwtBearer(SuperAdminJwtScheme, options =>
    {
        options.RequireHttpsMetadata = builder.Environment.IsProduction();
        options.SaveToken = true;
        // MapInboundClaims=false hace que el handler NO renombre los claims a las URIs de
        // ClaimTypes.*; el token queda tal cual salio del emisor.
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            NameClaimType = "email",
            RoleClaimType = "platform_role",
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtSettings.Issuer,
            ValidAudience = jwtSettings.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings.SigningKey)),
            ClockSkew = TimeSpan.FromMinutes(1)
        };
    });

// Politicas alineadas a la familia CUBOT (ver SuperAdmin de travels).
builder.Services.AddAuthorizationBuilder()
    .AddPolicy(CubotRedManager.Web.Authorization.AppPolicies.PlatformOperator, p => p.RequireClaim("platform_role"))
    .AddPolicy(CubotRedManager.Web.Authorization.AppPolicies.TenantMember, p => p.RequireClaim("tenant_id"))
    .AddPolicy(CubotRedManager.Web.Authorization.AppPolicies.TenantAdmin, p => p.RequireClaim("tenant_role", "Owner", "Admin"))
    // Policy para la Admin Agent API: exige Bearer JWT + claim binario is_super_admin=true.
    // No mezcla con PlatformOperator (que es cookie): un operador de la consola web NO obtiene
    // acceso al API cross-tenant sin loguear via POST /connect/login y obtener el JWT.
    .AddPolicy(CubotRedManager.Web.Authorization.AppPolicies.SuperAdminApi, p => p
        .AddAuthenticationSchemes(SuperAdminJwtScheme)
        .RequireAuthenticatedUser()
        .RequireClaim("is_super_admin", "true"));

// CORS: solo para /api/mobile/*. La APK Android abre la WebView con origen "https://localhost"
// (Capacitor con androidScheme: 'https'); iOS futuro seria "capacitor://localhost". El resto de la
// web (Blazor Server) no necesita CORS porque es misma-origen.
builder.Services.AddCors(o => o.AddPolicy("mobile", p =>
    p.WithOrigins("https://localhost", "capacitor://localhost", "http://localhost")
     .AllowAnyMethod()
     .AllowAnyHeader()
     .WithExposedHeaders("Content-Type")));

var app = builder.Build();

// ForwardedHeaders debe ir ANTES de cualquier middleware que mire el scheme (HttpsRedirection,
// Authentication): asi Request.Scheme = https detras del proxy de Railway.
app.UseForwardedHeaders();

// Aplica migraciones pendientes al arrancar. En Railway este es el mecanismo oficial del piloto:
// el primer arranque crea el esquema completo (incluida data_protection_keys).
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<CubotRedManagerDbContext>();
    await db.Database.MigrateAsync();

    // Bootstrap de produccion: si la BD no tiene ningun usuario de plataforma, crea el primer
    // SuperAdmin desde variables de entorno (Bootstrap__AdminEmail / Bootstrap__AdminPassword).
    // Sin esto, un despliegue nuevo quedaria sin forma de iniciar sesion. No se loggea la clave.
    if (!app.Environment.IsDevelopment())
    {
        var bootEmail = builder.Configuration["Bootstrap:AdminEmail"];
        var bootPassword = builder.Configuration["Bootstrap:AdminPassword"];
        if (!string.IsNullOrWhiteSpace(bootEmail)
            && !string.IsNullOrWhiteSpace(bootPassword)
            && !await db.PlatformUsers.AnyAsync())
        {
            var bootstrapHasher = scope.ServiceProvider.GetRequiredService<CubotRedManager.Application.Common.Auth.IPasswordHasher>();
            db.PlatformUsers.Add(new CubotRedManager.Domain.Entities.PlatformUser
            {
                Id = Guid.CreateVersion7(),
                Email = bootEmail.Trim().ToLowerInvariant(),
                DisplayName = "Super Admin",
                PasswordHash = bootstrapHasher.Hash(bootPassword),
                PlatformRole = CubotRedManager.Domain.Enums.PlatformRole.SuperAdmin,
                AuthProvider = CubotRedManager.Domain.Enums.AuthProvider.Local,
                EmailVerified = true
            });
            await db.SaveChangesAsync();
        }
    }

    // Los seeds demo (tenant, usuarios con clave conocida, contenedores de datos) son SOLO de
    // desarrollo: en produccion crearian un SuperAdmin con clave publica (admin123).
    if (app.Environment.IsDevelopment())
    {
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
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}
// StatusCodePages: solo se aplica a rutas de UI. Se salta /webhooks/*, /oauth/*, /auth/*, /api/*,
// /hubs/* porque son endpoints programaticos que responden JSON o redirects; si /webhooks/evolution
// devuelve 400/401/503, no queremos re-ejecutar hacia la pagina Blazor /not-found (que ademas
// tiene antiforgery metadata y trigea un error interno). Sin este bypass, el POST a /webhooks/*
// se reescribia a /not-found y AntiforgeryMiddleware lo rechazaba con 400 confuso.
app.UseWhen(
    ctx => !ctx.Request.Path.StartsWithSegments("/webhooks")
        && !ctx.Request.Path.StartsWithSegments("/oauth")
        && !ctx.Request.Path.StartsWithSegments("/auth")
        && !ctx.Request.Path.StartsWithSegments("/api")
        && !ctx.Request.Path.StartsWithSegments("/hubs"),
    branch => branch.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true));
app.UseHttpsRedirection();

// Sirve los archivos subidos por usuarios (wwwroot/uploads/...). La URL incluye un Guid v7 que
// hace inenumerable el path; en produccion mover a almacenamiento dedicado (S3) con firma.
app.UseStaticFiles();

// CORS: aplica SOLO a /api/mobile/*. Antes de Authentication para que el preflight OPTIONS
// pase limpio sin requerir credenciales.
app.UseWhen(ctx => ctx.Request.Path.StartsWithSegments("/api/mobile"),
    branch => branch.UseCors("mobile"));

app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// ===== Servido del binario de recursos del agente =====
// GET /api/agent-resources/{id}/file - devuelve el bytea de ai_agent_resources.file_content.
// - Requiere autenticacion (tenant_id claim) para evitar leaks entre agencias. El QueryFilter de
//   IApplicationDbContext filtra por TenantId, asi que un usuario solo ve recursos de su tenant.
// - No es una API publica: solo la usa el agente (dispatcher via IAgentMediaReader) y la UI del
//   propio operador para verificar que el archivo persistio.
app.MapGet("/api/agent-resources/{id:guid}/file", async (
    Guid id,
    IApplicationDbContext db,
    CancellationToken ct) =>
{
    var res = await db.AiAgentResources.AsNoTracking()
        .Where(r => r.Id == id)
        .Select(r => new { r.FileContent, r.FileMimeType, r.FileName })
        .FirstOrDefaultAsync(ct);
    if (res?.FileContent is not { Length: > 0 }) { return Results.NotFound(); }
    var mime = string.IsNullOrWhiteSpace(res.FileMimeType) ? "application/octet-stream" : res.FileMimeType;
    return Results.File(res.FileContent, mime, res.FileName);
}).RequireAuthorization();

// ===== REST API autenticada por X-Api-Token (DataContainers) =====
// Endpoints /api/data-containers/* para uso programatico (scripts, integraciones). Se autentican
// con header "X-Api-Token: cubot_..." (token opaco generado por el user en /cuenta).
// El token va cifrado con SHA256 en la BD; el validador setea el ambient tenant scope segun la
// claim del token. Todas las escrituras respetan HasQueryFilter, o sea aisladas por tenant.
//
// Helper: valida el header y setea el ambient scope. Retorna la identidad o null (401).
static async Task<CubotRedManager.Application.Tenancy.ApiTokenIdentity?> AuthenticateApiTokenAsync(
    HttpContext http,
    CubotRedManager.Application.Tenancy.IApiTokenService tokens,
    CubotRedManager.Application.Abstractions.IAmbientTenantOverride ambient)
{
    var header = http.Request.Headers["X-Api-Token"].ToString();
    if (string.IsNullOrWhiteSpace(header)) { return null; }
    var identity = await tokens.ValidateAsync(header, http.RequestAborted);
    if (identity is null) { return null; }
    ambient.Set(identity.TenantId, identity.UserId);
    return identity;
}

// GET /api/data-containers - lista de contenedores del tenant (id, nombre, columnas, filas)
app.MapGet("/api/data-containers", async (
    HttpContext http,
    CubotRedManager.Application.Tenancy.IApiTokenService tokens,
    CubotRedManager.Application.Abstractions.IAmbientTenantOverride ambient,
    CubotRedManager.Application.Tenancy.IDataContainerService svc,
    CancellationToken ct) =>
{
    var id = await AuthenticateApiTokenAsync(http, tokens, ambient);
    if (id is null) { return Results.Unauthorized(); }
    try
    {
        var list = await svc.ListAsync(ct);
        return Results.Ok(list);
    }
    finally { ambient.Set(null, null); }
}).AllowAnonymous().DisableAntiforgery();

// GET /api/data-containers/{id} - detalle con columnas (id, nombre, tipo, sortOrder, isRequired)
app.MapGet("/api/data-containers/{id:guid}", async (
    Guid id,
    HttpContext http,
    CubotRedManager.Application.Tenancy.IApiTokenService tokens,
    CubotRedManager.Application.Abstractions.IAmbientTenantOverride ambient,
    CubotRedManager.Application.Tenancy.IDataContainerService svc,
    CancellationToken ct) =>
{
    var ident = await AuthenticateApiTokenAsync(http, tokens, ambient);
    if (ident is null) { return Results.Unauthorized(); }
    try
    {
        var detail = await svc.GetAsync(id, ct);
        return detail is null ? Results.NotFound() : Results.Ok(detail);
    }
    finally { ambient.Set(null, null); }
}).AllowAnonymous().DisableAntiforgery();

// GET /api/data-containers/{id}/rows?take=N&search=texto - filas con valores por columna
app.MapGet("/api/data-containers/{id:guid}/rows", async (
    Guid id,
    HttpContext http,
    CubotRedManager.Application.Tenancy.IApiTokenService tokens,
    CubotRedManager.Application.Abstractions.IAmbientTenantOverride ambient,
    CubotRedManager.Application.Tenancy.IDataContainerService svc,
    int? take,
    string? search,
    CancellationToken ct) =>
{
    var ident = await AuthenticateApiTokenAsync(http, tokens, ambient);
    if (ident is null) { return Results.Unauthorized(); }
    try
    {
        var rows = await svc.ListRowsAsync(id, string.IsNullOrWhiteSpace(search) ? null : search, take ?? 2000, ct);
        return Results.Ok(rows);
    }
    finally { ambient.Set(null, null); }
}).AllowAnonymous().DisableAntiforgery();

// PATCH /api/data-containers/{id}/rows/{rowId} - actualiza SOLO las celdas incluidas en el body
// Body: { "valuesByColumnId": { "<guid>": "<value>", "<guid>": null } }
// null en el body borra el valor; columnas NO incluidas quedan intactas (esto es lo que preserva
// tus otras columnas como beneficio/precio cuando yo solo mando productId).
app.MapMethods("/api/data-containers/{id:guid}/rows/{rowId:guid}", new[] { "PATCH" }, async (
    Guid id,
    Guid rowId,
    HttpContext http,
    CubotRedManager.Application.Tenancy.IApiTokenService tokens,
    CubotRedManager.Application.Abstractions.IAmbientTenantOverride ambient,
    CubotRedManager.Application.Tenancy.IDataContainerService svc,
    CancellationToken ct) =>
{
    var ident = await AuthenticateApiTokenAsync(http, tokens, ambient);
    if (ident is null) { return Results.Unauthorized(); }
    try
    {
        // Body parse: { valuesByColumnId: { "<guid>": "<value?>" } }
        var body = await System.Text.Json.JsonDocument.ParseAsync(http.Request.Body, cancellationToken: ct);
        if (!body.RootElement.TryGetProperty("valuesByColumnId", out var vElem) || vElem.ValueKind != System.Text.Json.JsonValueKind.Object)
        {
            return Results.BadRequest(new { error = "body debe tener 'valuesByColumnId' como objeto {columnId: valor}" });
        }
        // Cargar fila existente para preservar columnas NO incluidas en el PATCH.
        var existing = await svc.ListRowsAsync(id, null, take: 5000, ct);
        var current = existing.FirstOrDefault(r => r.Id == rowId);
        if (current is null) { return Results.NotFound(new { error = "row no encontrada en este contenedor" }); }
        var merged = new Dictionary<Guid, string?>(current.ValuesByColumnId);
        foreach (var p in vElem.EnumerateObject())
        {
            if (!Guid.TryParse(p.Name, out var colId)) { continue; }
            merged[colId] = p.Value.ValueKind == System.Text.Json.JsonValueKind.Null ? null : p.Value.ToString();
        }
        var req = new CubotRedManager.Application.Tenancy.SaveDataRowRequest(id, rowId, merged);
        var saved = await svc.SaveRowAsync(req, ident.UserId, ct);
        return saved is null ? Results.NotFound() : Results.Ok(saved);
    }
    finally { ambient.Set(null, null); }
}).AllowAnonymous().DisableAntiforgery();

// ===== REST API autenticada por X-Api-Token (Agentes IA) =====
// GET /api/agents - lista agentes del tenant (id, nombre, provider, activo, resourceCount)
app.MapGet("/api/agents", async (
    HttpContext http,
    CubotRedManager.Application.Tenancy.IApiTokenService tokens,
    CubotRedManager.Application.Abstractions.IAmbientTenantOverride ambient,
    CubotRedManager.Application.Tenancy.IAiAgentService svc,
    CancellationToken ct) =>
{
    var ident = await AuthenticateApiTokenAsync(http, tokens, ambient);
    if (ident is null) { return Results.Unauthorized(); }
    try { return Results.Ok(await svc.ListAsync(ct)); }
    finally { ambient.Set(null, null); }
}).AllowAnonymous().DisableAntiforgery();

// GET /api/agents/{id} - detalle del agente incluyendo su PaymentConfig (con TokenPresent bool,
// NO expone el token descifrado). Sirve para revisar el estado actual antes del PATCH.
app.MapGet("/api/agents/{id:guid}", async (
    Guid id,
    HttpContext http,
    CubotRedManager.Application.Tenancy.IApiTokenService tokens,
    CubotRedManager.Application.Abstractions.IAmbientTenantOverride ambient,
    CubotRedManager.Application.Tenancy.IAiAgentService svc,
    CancellationToken ct) =>
{
    var ident = await AuthenticateApiTokenAsync(http, tokens, ambient);
    if (ident is null) { return Results.Unauthorized(); }
    try
    {
        var detail = await svc.GetAsync(id, ct);
        return detail is null ? Results.NotFound() : Results.Ok(detail);
    }
    finally { ambient.Set(null, null); }
}).AllowAnonymous().DisableAntiforgery();

// GET /api/agents/{id}/run-logs?take=N - ultimas N entradas de bitacora del agente (todas las
// conversaciones que atendio). Sirve para diagnosticar por que no responde con imagen o por que
// no marca reacciones. Sin este endpoint habia que abrir la UI /agentes -> Bitacora manualmente.
app.MapGet("/api/agents/{id:guid}/run-logs", async (
    Guid id,
    HttpContext http,
    CubotRedManager.Application.Tenancy.IApiTokenService tokens,
    CubotRedManager.Application.Abstractions.IAmbientTenantOverride ambient,
    CubotRedManager.Application.Abstractions.IApplicationDbContext db,
    int? take,
    CancellationToken ct) =>
{
    var ident = await AuthenticateApiTokenAsync(http, tokens, ambient);
    if (ident is null) { return Results.Unauthorized(); }
    try
    {
        var n = Math.Clamp(take ?? 50, 1, 500);
        var logs = await db.AiAgentRunLogs.AsNoTracking()
            .Where(l => l.AgentId == id)
            .OrderByDescending(l => l.OccurredAt)
            .Take(n)
            .Select(l => new { l.OccurredAt, l.Kind, l.Title, l.Content, l.Response, l.ConversationId })
            .ToListAsync(ct);
        return Results.Ok(logs);
    }
    finally { ambient.Set(null, null); }
}).AllowAnonymous().DisableAntiforgery();

// PATCH /api/agents/{id}/payment-config - actualiza la config de Pagos FUXION del agente. Body:
// {
//   "enabled": true,
//   "userId": "3238",
//   "country": "pe",
//   "newToken": "cubot_..."  // null=no tocar, ""=borrar, otro=reemplazar (se cifra)
//   "catalogContainerName": "PRECIOS PRODUCTOS",
//   "catalogNameColumn": "Producto",
//   "catalogProductIdColumn": "IdProducto",
//   "catalogCountryColumn": "Pais",
//   "apiBaseUrl": null, "apiPathTemplate": null, "responseUrlPath": null  // overrides opcionales
// }
// Devuelve el DTO actualizado (TokenPresent bool, expira, etc). Nunca expone el token descifrado.
app.MapMethods("/api/agents/{id:guid}/payment-config", new[] { "PATCH" }, async (
    Guid id,
    HttpContext http,
    CubotRedManager.Application.Tenancy.IApiTokenService tokens,
    CubotRedManager.Application.Abstractions.IAmbientTenantOverride ambient,
    CubotRedManager.Application.Tenancy.IAiAgentService svc,
    CancellationToken ct) =>
{
    var ident = await AuthenticateApiTokenAsync(http, tokens, ambient);
    if (ident is null) { return Results.Unauthorized(); }
    try
    {
        var body = await System.Text.Json.JsonDocument.ParseAsync(http.Request.Body, cancellationToken: ct);
        var root = body.RootElement;
        string? Str(string key) => root.TryGetProperty(key, out var e) && e.ValueKind == System.Text.Json.JsonValueKind.String ? e.GetString() : null;
        bool Bool(string key, bool def) => root.TryGetProperty(key, out var e) && (e.ValueKind == System.Text.Json.JsonValueKind.True || e.ValueKind == System.Text.Json.JsonValueKind.False) ? e.GetBoolean() : def;
        // NewToken tri-estado: si la propiedad NO viene en el body -> null (no tocar);
        // si viene como string -> ese string (puede ser "" para borrar).
        string? newToken = null;
        if (root.TryGetProperty("newToken", out var tokElem) && tokElem.ValueKind == System.Text.Json.JsonValueKind.String)
        {
            newToken = tokElem.GetString();
        }
        var req = new CubotRedManager.Application.Tenancy.SetAgentPaymentConfigRequest(
            Enabled: Bool("enabled", false),
            UserId: Str("userId"),
            Country: Str("country"),
            NewToken: newToken,
            CatalogContainerName: Str("catalogContainerName"),
            CatalogNameColumn: Str("catalogNameColumn"),
            CatalogProductIdColumn: Str("catalogProductIdColumn"),
            CatalogCountryColumn: Str("catalogCountryColumn"),
            ApiBaseUrl: Str("apiBaseUrl"),
            ApiPathTemplate: Str("apiPathTemplate"),
            ResponseUrlPath: Str("responseUrlPath"));
        var result = await svc.SetPaymentConfigAsync(id, req, ident.UserId, ct);
        return result is null ? Results.NotFound() : Results.Ok(result);
    }
    finally { ambient.Set(null, null); }
}).AllowAnonymous().DisableAntiforgery();

// POST /api/agents/{id}/sync-prices - sincroniza la columna Precio del DataContainer con los
// precios actuales del catalogo FUXION (baja /api/products?country=XX por cada pais con filas y
// PATCHea solo lo que difiere). Devuelve el detalle de la corrida.
app.MapPost("/api/agents/{id:guid}/sync-prices", async (
    Guid id,
    HttpContext http,
    CubotRedManager.Application.Tenancy.IApiTokenService tokens,
    CubotRedManager.Application.Abstractions.IAmbientTenantOverride ambient,
    CubotRedManager.Application.Tenancy.IPriceSyncService priceSync,
    CancellationToken ct) =>
{
    var ident = await AuthenticateApiTokenAsync(http, tokens, ambient);
    if (ident is null) { return Results.Unauthorized(); }
    try
    {
        var result = await priceSync.SyncPricesAsync(id, ident.UserId, ct);
        return Results.Ok(result);
    }
    finally { ambient.Set(null, null); }
}).AllowAnonymous().DisableAntiforgery();

// DELETE /api/data-containers/{id}/rows/{rowId} - borra una fila del contenedor. Usado para
// limpiar duplicados (tarea one-shot) o rows creadas por error via API. Devuelve 204 si borro,
// 404 si no existia. La validacion de que la fila pertenezca al contenedor la hace el servicio.
app.MapDelete("/api/data-containers/{id:guid}/rows/{rowId:guid}", async (
    Guid id,
    Guid rowId,
    HttpContext http,
    CubotRedManager.Application.Tenancy.IApiTokenService tokens,
    CubotRedManager.Application.Abstractions.IAmbientTenantOverride ambient,
    CubotRedManager.Application.Tenancy.IDataContainerService svc,
    CancellationToken ct) =>
{
    _ = id; // no lo pasamos al servicio (borra por rowId directo, tenant-scoped via ambient)
    var ident = await AuthenticateApiTokenAsync(http, tokens, ambient);
    if (ident is null) { return Results.Unauthorized(); }
    try
    {
        var ok = await svc.DeleteRowAsync(rowId, ident.UserId, ct);
        return ok ? Results.NoContent() : Results.NotFound();
    }
    finally { ambient.Set(null, null); }
}).AllowAnonymous().DisableAntiforgery();

// POST /api/data-containers/{id}/rows - crea una fila nueva. Body: {"valuesByColumnId": {...}}
// Devuelve la fila creada con su Id. Sirve para agregar variantes (ej. una fila por presentacion
// distinta del mismo producto). Columnas no incluidas quedan vacias.
app.MapPost("/api/data-containers/{id:guid}/rows", async (
    Guid id,
    HttpContext http,
    CubotRedManager.Application.Tenancy.IApiTokenService tokens,
    CubotRedManager.Application.Abstractions.IAmbientTenantOverride ambient,
    CubotRedManager.Application.Tenancy.IDataContainerService svc,
    CancellationToken ct) =>
{
    var ident = await AuthenticateApiTokenAsync(http, tokens, ambient);
    if (ident is null) { return Results.Unauthorized(); }
    try
    {
        var body = await System.Text.Json.JsonDocument.ParseAsync(http.Request.Body, cancellationToken: ct);
        if (!body.RootElement.TryGetProperty("valuesByColumnId", out var vElem) || vElem.ValueKind != System.Text.Json.JsonValueKind.Object)
        {
            return Results.BadRequest(new { error = "body debe tener 'valuesByColumnId' como objeto {columnId: valor}" });
        }
        var values = new Dictionary<Guid, string?>();
        foreach (var p in vElem.EnumerateObject())
        {
            if (!Guid.TryParse(p.Name, out var colId)) { continue; }
            values[colId] = p.Value.ValueKind == System.Text.Json.JsonValueKind.Null ? null : p.Value.ToString();
        }
        // RowId=null indica al servicio que es un create, no update.
        var req = new CubotRedManager.Application.Tenancy.SaveDataRowRequest(id, RowId: null, values);
        var saved = await svc.SaveRowAsync(req, ident.UserId, ct);
        return saved is null ? Results.NotFound() : Results.Created($"/api/data-containers/{id}/rows/{saved.Id}", saved);
    }
    finally { ambient.Set(null, null); }
}).AllowAnonymous().DisableAntiforgery();

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
        return Results.Redirect("/admin");
    }
    return Results.Redirect("/dashboard");
}).DisableAntiforgery();

// -----------------------------------------------------------------------------------------------
// Admin Agent API — login por JWT (para consumo sin UI, ej. Claude con Bearer token).
// Este endpoint es la contraparte del /auth/login por cookie: valida la MISMA identidad
// (PlatformUsers), pero solo emite token si el user es SuperAdmin. Un tenant admin recibe 401
// aunque su clave sea correcta — la separacion es intencional (ver AppPolicies.SuperAdminApi).
//
// Contract:
//   POST /connect/login  { email, password }
//     -> 200 { kind:"superadmin", accessToken, expiresAt, userId, email, displayName, platformRole }
//     -> 401 { error:"invalid_credentials" }   (ambos casos: unknown user y "no eres super admin")
//                                              se colapsan para NO fugar por-email quien es super admin.
// -----------------------------------------------------------------------------------------------
app.MapPost("/connect/login", async (
    HttpContext http,
    [FromBody] SuperAdminLoginRequest req,
    CubotRedManager.Application.Auth.ISuperAdminAuthService svc,
    CancellationToken ct) =>
{
    if (req is null || string.IsNullOrWhiteSpace(req.Email) || string.IsNullOrWhiteSpace(req.Password))
    {
        return Results.Json(new { error = "invalid_credentials" }, statusCode: 401);
    }

    var result = await svc.LoginAsync(req.Email, req.Password, ct);
    return result switch
    {
        CubotRedManager.Application.Auth.SuperAdminLoginResult.Ok ok => Results.Json(ok.Response),
        _ => Results.Json(new { error = "invalid_credentials" }, statusCode: 401)
    };
}).DisableAntiforgery();

// -----------------------------------------------------------------------------------------------
// Admin Agent API: tenants, agents, tools, run-logs. Todo bajo /api/admin/*, todo con la policy
// SuperAdminApi (Bearer JWT + is_super_admin=true).
//
// Prefijo /api/admin en vez de /admin: /admin/* esta reservado para paginas Blazor de la consola
// (AdminHome.razor, Agencias.razor, etc.); el router Blazor devuelve 404 antes de que el
// MinimalAPI vea la ruta. /api/admin evita esa colision y deja explicito "esto es API".
// Line-binding + lines llegan en PR3.
// -----------------------------------------------------------------------------------------------
app.MapSuperAdminApi();
// Ping para el checklist fail-closed del brief (401 sin token / 200 con super admin).
app.MapGet("/api/admin/ping", (HttpContext http) => Results.Ok(new
{
    ok = true,
    userId = http.User.FindFirstValue("sub"),
    email = http.User.FindFirstValue("email")
})).RequireAuthorization(CubotRedManager.Web.Authorization.AppPolicies.SuperAdminApi);

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
        return Results.Redirect("/admin");
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

// ===== Webhook Evolution (crudo) =====
// POST /webhooks/evolution: recibe TODOS los eventos de Evolution (mensajes, reacciones, etc).
// - Valida el token global contra EvolutionMasterConfig.WebhookToken (o env
//   CUBOT_EVOLUTION_WEBHOOK_TOKEN como fallback). Header: x-webhook-token. Fallback query: ?token=.
// - Parsea con EvolutionWebhookParser: solo procesa messages.upsert que no sean grupo/reaccion.
// - Comando de toma de control "Manejo_asesor" (fromMe) -> agrega a lista negra y sale.
// - Los demas mensajes se persisten via IChatIngestService.IngestTrustedAsync (dedupe por
//   ExternalMessageId, crea/actualiza Conversation, guarda Message inbound).
// - .AllowAnonymous().DisableAntiforgery() porque no lleva antiforgery token.
// TODO Fase 2: descargar media faltante via IWhatsAppConnectorService.FetchInboundMediaAsync
// y guardar en /uploads/chat.
// TODO Fase 3: encolar dispatch al agente tras persistir.
// IMPORTANTE: este MapPost DEBE ir despues del MapPost de YCloud. Bug conocido de ASP.NET Core 10
// con Blazor Server: el primer MapPost anonimo con DisableAntiforgery tras UseAntiforgery() no
// registra correctamente la metadata (el middleware ve el endpoint sin DisableAntiforgery y
// devuelve 400). El segundo y siguientes MapPost si toman la metadata. Solucion: dejar YCloud
// primero (mismo bug pero descubierto antes) y Evolution justo despues. NO reordenar.

// ===== Webhooks: YCloud (WhatsApp BSP oficial) =====
// POST /webhooks/ycloud/{tenantId}: recibe mensajes entrantes. Idempotencia por wamid.
// Resuelve la linea por YCloudPhoneNumberId = payload.to. Guarda como InboxMessage.
// Responde 200 rapido (Meta desconecta si > 5s). Fira-and-forget al escribir el ambient tenant.
app.MapPost("/webhooks/ycloud/{tenantId:guid}", async (
    Guid tenantId,
    HttpContext http,
    IServiceScopeFactory scopeFactory,
    ILoggerFactory loggerFactory) =>
{
    var log = loggerFactory.CreateLogger("YCloudWebhook");
    System.Text.Json.JsonDocument doc;
    try { doc = await System.Text.Json.JsonDocument.ParseAsync(http.Request.Body); }
    catch (Exception ex) { log.LogWarning(ex, "YCloud webhook: JSON invalido para tenant {TenantId}", tenantId); return Results.Ok(); }

    List<CubotRedManager.Web.Webhooks.YCloudWebhookParser.InboundMessage> messages;
    try { messages = CubotRedManager.Web.Webhooks.YCloudWebhookParser.Parse(doc); }
    finally { doc.Dispose(); }
    if (messages.Count == 0) { return Results.Ok(); }

    using var scope = scopeFactory.CreateScope();
    scope.ServiceProvider.GetRequiredService<IAmbientTenantOverride>().Set(tenantId, null);
    var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

    foreach (var m in messages)
    {
        // Resuelve la linea por sender (to). Si la linea no existe en el tenant o no es YCloud,
        // se descarta silenciosamente (podria ser un webhook mal configurado).
        var line = await db.WhatsAppLines.FirstOrDefaultAsync(l =>
            l.YCloudPhoneNumberId == m.ToPhone && l.Provider == CubotRedManager.Domain.Enums.WhatsAppProvider.YCloud);
        if (line is null)
        {
            log.LogInformation("YCloud webhook: linea no encontrada para tenant {TenantId}, to {To}", tenantId, m.ToPhone);
            continue;
        }
        // Idempotencia global por (network_code, external_id) -- indice unico en el modelo.
        var exists = await db.InboxMessages
            .IgnoreQueryFilters()
            .AnyAsync(x => x.NetworkCode == "whatsapp" && x.ExternalId == m.Wamid);
        if (exists) { continue; }

        db.InboxMessages.Add(new CubotRedManager.Domain.Entities.InboxMessage
        {
            TenantId = tenantId,
            ClientId = Guid.Empty, // Sin cliente vinculado hasta que se asocie manualmente.
            NetworkCode = "whatsapp",
            Type = CubotRedManager.Domain.Enums.InboxMessageType.DirectMessage,
            ExternalId = m.Wamid,
            AuthorExternalId = m.FromPhone,
            AuthorName = m.FromPhone,
            Body = m.Text ?? $"(mensaje tipo {m.MessageType})",
            ReceivedAt = m.ReceivedAt,
            Status = CubotRedManager.Domain.Enums.InboxStatus.Unread
        });
    }
    await db.SaveChangesAsync();
    return Results.Ok();
}).AllowAnonymous().DisableAntiforgery();

// ===== Webhook Evolution (crudo) =====
// POST /webhooks/evolution/{tenantId}: recibe TODOS los eventos de Evolution (mensajes,
// reacciones, etc). Valida el token global contra EvolutionMasterConfig.WebhookToken (header
// x-webhook-token o query ?token=). Parsea con EvolutionWebhookParser. Comando "Manejo_asesor"
// (fromMe) -> agrega a lista negra. Los demas mensajes van a IChatIngestService que persiste
// Conversation + Message.
// IMPORTANTE: registrado DESPUES de YCloud a proposito -- ver comentario arriba.
app.MapPost("/webhooks/evolution/{tenantId:guid}", async (
    Guid tenantId,
    HttpRequest request,
    IApplicationDbContext db,
    CubotRedManager.Application.Tenancy.IChatIngestService ingest,
    IAmbientTenantOverride tenantOverride,
    ILoggerFactory loggerFactory,
    CancellationToken ct) =>
{
    var log = loggerFactory.CreateLogger("EvolutionWebhook");
    log.LogInformation("Evolution webhook: request recibido para tenant {Tenant}", tenantId);

    var master = await db.EvolutionMasterConfigs.FirstOrDefaultAsync(ct);
    var expected = master?.WebhookToken
        ?? Environment.GetEnvironmentVariable("CUBOT_EVOLUTION_WEBHOOK_TOKEN");
    if (string.IsNullOrEmpty(expected)) { return Results.StatusCode(503); }

    var provided = request.Headers["x-webhook-token"].ToString();
    if (string.IsNullOrEmpty(provided)) { provided = request.Query["token"].ToString(); }
    if (!string.Equals(provided, expected, StringComparison.Ordinal)) { return Results.Unauthorized(); }

    System.Text.Json.JsonDocument doc;
    try { doc = await System.Text.Json.JsonDocument.ParseAsync(request.Body, cancellationToken: ct); }
    catch (Exception ex) { log.LogWarning(ex, "Evolution webhook: JSON invalido"); return Results.Ok(); }

    CubotRedManager.Application.Tenancy.ParsedInbound? parsed;
    try { parsed = CubotRedManager.Application.Tenancy.EvolutionWebhookParser.Parse(doc.RootElement); }
    finally { doc.Dispose(); }
    if (parsed is null) { return Results.Ok(new { status = "ignored" }); }

    // Ambient tenant para que los filtros globales y auditor apunten al tenant correcto.
    tenantOverride.Set(tenantId, null);

    if (parsed.IsTakeControlCommand)
    {
        await CubotRedManager.Application.Tenancy.AgentControlCommands.BlockNumberForLineAsync(
            db, parsed.Payload.WhatsAppLineId ?? Guid.Empty, parsed.Payload.ContactPhone, ct);
        return Results.Ok(new { status = "blocked" });
    }

    var result = await ingest.IngestTrustedAsync(tenantId, parsed.Payload, ct);
    return result == CubotRedManager.Application.Tenancy.ChatIngestResult.Duplicate
        ? Results.Ok(new { status = "duplicate" })
        : Results.Accepted();
}).AllowAnonymous().DisableAntiforgery();

// ===== Callback OAuth TikTok =====
// TikTok redirige aqui despues de que el usuario autoriza. El state va firmado con DataProtection
// y contiene tenant/cliente/actor. Nosotros:
//   1. Descifra el state (rechaza si expiro >15 min o firma no valida).
//   2. Fija el ambient tenant.
//   3. Llama a ExchangeCodeAsync -> crea/actualiza SocialAccount y guarda tokens cifrados.
//   4. Redirige al detalle de la cuenta con toast de exito, o vuelta a la lista con error.
// SEGURIDAD:
//   - El code de TikTok viaja por la URL. NUNCA se loguea (regla CLAUDE.md).
//   - El state expira a los 15 min (TTL corto).
//   - No aceptamos code sin state valido (CSRF).
app.MapGet("/oauth/tiktok/callback", async (
    HttpContext http,
    [FromQuery] string? code,
    [FromQuery] string? state,
    [FromQuery] string? error,
    [FromQuery(Name = "error_description")] string? errorDescription,
    IServiceScopeFactory scopeFactory) =>
{
    // 1) TikTok reporto un error propio (usuario cancelo, scope denegado, etc.).
    if (!string.IsNullOrEmpty(error))
    {
        var msg = string.IsNullOrWhiteSpace(errorDescription) ? error : errorDescription;
        return Results.Redirect("/cuentas-sociales?tkerr=" + Uri.EscapeDataString(msg));
    }
    if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(state))
    {
        return Results.Redirect("/cuentas-sociales?tkerr=" + Uri.EscapeDataString("TikTok no devolvio code o state."));
    }

    // 2) Descifrar el state para conocer tenant/cliente/actor sin tocar cookies del navegador (por
    //    si el usuario autorizo desde un dispositivo distinto al que abrio la conexion).
    using var scope = scopeFactory.CreateScope();
    var stateSvc = scope.ServiceProvider.GetRequiredService<CubotRedManager.Web.Auth.TikTokOAuthStateService>();
    if (!stateSvc.TryDecode(state, out var tenantId, out var clientId, out var actorUserId, out var stateErr))
    {
        return Results.Redirect("/cuentas-sociales?tkerr=" + Uri.EscapeDataString($"state invalido: {stateErr}"));
    }

    // 3) Fijar tenant y ejecutar el exchange.
    scope.ServiceProvider.GetRequiredService<IAmbientTenantOverride>().Set(tenantId, null);
    var tiktok = scope.ServiceProvider.GetRequiredService<CubotRedManager.Application.Tenancy.ITikTokConnectionService>();
    var result = await tiktok.ExchangeCodeAsync(clientId, code, actorUserId);

    if (!result.Success || result.Account is null)
    {
        return Results.Redirect("/cuentas-sociales?tkerr=" + Uri.EscapeDataString(result.Error ?? "TikTok rechazo el code."));
    }

    // 4) Redirigir al detalle de la cuenta reciente. Preferimos /cuentas/{id} porque muestra el
    //    Resumen con estado del token, KPIs y siguiente accion sugerida.
    return Results.Redirect($"/cuentas/{result.Account.Id}?connected=1");
}).AllowAnonymous();

// Healthcheck para Railway: verifica que la app responde y que Postgres esta accesible.
// Railway lo consulta tras cada deploy; si falla, el deploy se marca unhealthy y no rota trafico.
app.MapGet("/healthz", async (CubotRedManagerDbContext db) =>
{
    try
    {
        await db.Database.ExecuteSqlRawAsync("SELECT 1");
        return Results.Ok(new { status = "ok", ts = DateTimeOffset.UtcNow, version = CubotRedManager.Web.AppVersion.ShortSha });
    }
    catch (Exception ex)
    {
        return Results.Problem($"db down: {ex.Message}", statusCode: 503);
    }
}).AllowAnonymous();

// Endpoint de version (publico, sin secretos). Sirve al operador para verificar que el pod que
// esta viendo es el ultimo deploy (SHA), no una cache de CDN. Usa RAILWAY_GIT_COMMIT_SHA que
// Railway inyecta al contenedor en cada deploy.
app.MapGet("/version", () => Results.Ok(new
{
    version = CubotRedManager.Web.AppVersion.ShortSha,
    fullSha = CubotRedManager.Web.AppVersion.FullSha,
    deploymentId = CubotRedManager.Web.AppVersion.DeploymentId,
    startedAtUtc = CubotRedManager.Web.AppVersion.StartedAtUtc
})).AllowAnonymous();

// ============================================================================
// REST API para la app Android (/api/mobile/*)
// Login con email+password del usuario de la plataforma → devuelve ApiToken opaco
// (TTL 30d) que la app manda como X-Api-Token en todas las llamadas siguientes.
// Los endpoints protegidos usan el mismo AuthenticateApiTokenAsync helper.
// ============================================================================

// Rate limit MUY simple para login mobile: cache in-memory de intentos por IP.
// Sin dependencias nuevas; si algun dia hace falta algo mas robusto, cambiamos a
// System.Threading.RateLimiting o un middleware.
var mobileLoginAttempts = new System.Collections.Concurrent.ConcurrentDictionary<string, (int Count, DateTimeOffset ResetAt)>();

app.MapPost("/api/mobile/auth/login", async (
    HttpContext http,
    CubotRedManager.Application.Mobile.IMobileService mobile,
    CancellationToken ct) =>
{
    // Anti brute-force: max 8 intentos por 5 min por IP. Al superar, 429 sin costo de BD.
    var ip = http.Connection.RemoteIpAddress?.ToString() ?? "?";
    var now = DateTimeOffset.UtcNow;
    var slot = mobileLoginAttempts.AddOrUpdate(ip,
        _ => (1, now.AddMinutes(5)),
        (_, existing) => existing.ResetAt <= now ? (1, now.AddMinutes(5)) : (existing.Count + 1, existing.ResetAt));
    if (slot.Count > 8)
    {
        return Results.StatusCode(429);
    }

    CubotRedManager.Application.Mobile.MobileLoginRequest? req;
    try { req = await http.Request.ReadFromJsonAsync<CubotRedManager.Application.Mobile.MobileLoginRequest>(ct); }
    catch { return Results.BadRequest(new { error = "body invalido" }); }
    if (req is null || string.IsNullOrWhiteSpace(req.Email) || string.IsNullOrWhiteSpace(req.Password))
    {
        return Results.BadRequest(new { error = "email y password requeridos" });
    }

    var res = await mobile.LoginAsync(req, ct);
    if (res is null) { return Results.Unauthorized(); }
    return Results.Ok(res);
}).AllowAnonymous().DisableAntiforgery();

app.MapGet("/api/mobile/dashboard", async (
    HttpContext http,
    CubotRedManager.Application.Tenancy.IApiTokenService tokens,
    CubotRedManager.Application.Abstractions.IAmbientTenantOverride ambient,
    CubotRedManager.Application.Mobile.IMobileService mobile,
    CancellationToken ct) =>
{
    var ident = await AuthenticateApiTokenAsync(http, tokens, ambient);
    if (ident is null) { return Results.Unauthorized(); }
    try
    {
        return Results.Ok(await mobile.GetDashboardAsync(ct));
    }
    finally { ambient.Set(null, null); }
}).AllowAnonymous().DisableAntiforgery();

app.MapGet("/api/mobile/conversations", async (
    HttpContext http,
    CubotRedManager.Application.Tenancy.IApiTokenService tokens,
    CubotRedManager.Application.Abstractions.IAmbientTenantOverride ambient,
    CubotRedManager.Application.Mobile.IMobileService mobile,
    int? take,
    CancellationToken ct) =>
{
    var ident = await AuthenticateApiTokenAsync(http, tokens, ambient);
    if (ident is null) { return Results.Unauthorized(); }
    try
    {
        return Results.Ok(await mobile.ListConversationsAsync(take ?? 30, ct));
    }
    finally { ambient.Set(null, null); }
}).AllowAnonymous().DisableAntiforgery();

app.MapGet("/api/mobile/conversations/{id:guid}/messages", async (
    Guid id,
    HttpContext http,
    CubotRedManager.Application.Tenancy.IApiTokenService tokens,
    CubotRedManager.Application.Abstractions.IAmbientTenantOverride ambient,
    CubotRedManager.Application.Mobile.IMobileService mobile,
    int? take,
    CancellationToken ct) =>
{
    var ident = await AuthenticateApiTokenAsync(http, tokens, ambient);
    if (ident is null) { return Results.Unauthorized(); }
    try
    {
        return Results.Ok(await mobile.ListMessagesAsync(id, take ?? 100, ct));
    }
    finally { ambient.Set(null, null); }
}).AllowAnonymous().DisableAntiforgery();

app.MapGet("/api/mobile/agents", async (
    HttpContext http,
    CubotRedManager.Application.Tenancy.IApiTokenService tokens,
    CubotRedManager.Application.Abstractions.IAmbientTenantOverride ambient,
    CubotRedManager.Application.Mobile.IMobileService mobile,
    CancellationToken ct) =>
{
    var ident = await AuthenticateApiTokenAsync(http, tokens, ambient);
    if (ident is null) { return Results.Unauthorized(); }
    try
    {
        return Results.Ok(await mobile.ListAgentsAsync(ct));
    }
    finally { ambient.Set(null, null); }
}).AllowAnonymous().DisableAntiforgery();

app.MapPost("/api/mobile/agents/{id:guid}/fuxion-token", async (
    Guid id,
    HttpContext http,
    CubotRedManager.Application.Tenancy.IApiTokenService tokens,
    CubotRedManager.Application.Abstractions.IAmbientTenantOverride ambient,
    CubotRedManager.Application.Mobile.IMobileService mobile,
    CancellationToken ct) =>
{
    var ident = await AuthenticateApiTokenAsync(http, tokens, ambient);
    if (ident is null) { return Results.Unauthorized(); }
    try
    {
        using var body = await System.Text.Json.JsonDocument.ParseAsync(http.Request.Body, cancellationToken: ct);
        if (!body.RootElement.TryGetProperty("jwt", out var jwtEl) || jwtEl.ValueKind != System.Text.Json.JsonValueKind.String)
        {
            return Results.BadRequest(new { error = "body debe tener {jwt: '...'}" });
        }
        var updated = await mobile.UpdateFuxionTokenAsync(id, jwtEl.GetString() ?? "", ident.UserId, ct);
        return updated is null ? Results.NotFound() : Results.Ok(updated);
    }
    finally { ambient.Set(null, null); }
}).AllowAnonymous().DisableAntiforgery();

app.MapPost("/api/mobile/agents/{id:guid}/sync-prices", async (
    Guid id,
    HttpContext http,
    CubotRedManager.Application.Tenancy.IApiTokenService tokens,
    CubotRedManager.Application.Abstractions.IAmbientTenantOverride ambient,
    CubotRedManager.Application.Mobile.IMobileService mobile,
    CancellationToken ct) =>
{
    var ident = await AuthenticateApiTokenAsync(http, tokens, ambient);
    if (ident is null) { return Results.Unauthorized(); }
    try
    {
        return Results.Ok(await mobile.SyncPricesAsync(id, ident.UserId, ct));
    }
    finally { ambient.Set(null, null); }
}).AllowAnonymous().DisableAntiforgery();

app.Run();

// Helper: solo permite redirigir a URLs locales, a los hosts dev (:5036/:5037) o a los dominios
// de produccion, para evitar open-redirect.
static bool IsSafeReturnUrl(string url)
{
    if (url.StartsWith("/", StringComparison.Ordinal)) { return true; }
    if (url.StartsWith("http://localhost:5036", StringComparison.OrdinalIgnoreCase)) { return true; }
    if (url.StartsWith("https://red.cubot.com.co", StringComparison.OrdinalIgnoreCase)) { return true; }
    return false;
}

/// <summary>Agencia demo del dev-login (se elimina con el modulo de identidad real).</summary>
static class DemoTenant
{
    public static readonly Guid Id = Guid.Parse("0192a000-0000-7000-8000-000000000001");
}

/// <summary>Payload JSON del POST /connect/login (Admin Agent API).</summary>
public sealed record SuperAdminLoginRequest(string Email, string Password);
