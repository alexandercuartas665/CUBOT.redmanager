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

// PublicationScheduleWorker: dispara automaticamente publicaciones Status=Scheduled cuya
// ScheduledAt <= NOW. Es el "reloj despertador" del calendario editorial — sin este worker,
// una publi programada para manana 10am se queda como Scheduled hasta clic manual.
var psOpts = builder.Configuration.GetSection("PublicationSchedule").Get<CubotRedManager.Web.BackgroundJobs.PublicationScheduleOptions>()
             ?? new CubotRedManager.Web.BackgroundJobs.PublicationScheduleOptions();
builder.Services.AddSingleton(psOpts);
if (psOpts.Enabled)
{
    builder.Services.AddHostedService<CubotRedManager.Web.BackgroundJobs.PublicationScheduleWorker>();
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
// Buffer en memoria de ultimos payloads webhook (YCloud/Evolution) para diagnosticar sin pelearse
// con los logs de Railway. Capacidad fija 50 por proveedor. No persiste al reiniciar el proceso.
builder.Services.AddSingleton<CubotRedManager.Web.Webhooks.WebhookPayloadBuffer>();
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

// Sirve el binario de una PublicationMedia (bytea en BD). Tenant-scoped por HasQueryFilter.
// Uso: <img src="/api/publications/media/{id}"> en la lista del Calendario editorial.
// Cache-Control corto porque el contenido es inmutable pero el id no se re-emite (v7 GUID unico).
app.MapGet("/api/publications/media/{id:guid}", async (
    Guid id,
    IApplicationDbContext db,
    CancellationToken ct) =>
{
    var media = await db.PublicationMedias.AsNoTracking()
        .Where(m => m.Id == id)
        .Select(m => new { m.Content, m.MimeType, m.FileName })
        .FirstOrDefaultAsync(ct);
    if (media?.Content is not { Length: > 0 }) { return Results.NotFound(); }
    var mime = string.IsNullOrWhiteSpace(media.MimeType) ? "application/octet-stream" : media.MimeType;
    return Results.File(media.Content, mime, media.FileName);
}).RequireAuthorization();

// Sirve el binario de un LeadFile (bytea en BD, patron Railway-safe). Tenant-scoped por HasQueryFilter.
// Uso: <a href="/api/leads/files/{id}" target="_blank"> en el modal detalle del lead.
app.MapGet("/api/leads/files/{id:guid}", async (
    Guid id,
    IApplicationDbContext db,
    CancellationToken ct) =>
{
    var file = await db.LeadFiles.AsNoTracking()
        .Where(f => f.Id == id)
        .Select(f => new { f.Content, f.ContentType, f.FileName })
        .FirstOrDefaultAsync(ct);
    if (file?.Content is not { Length: > 0 }) { return Results.NotFound(); }
    var mime = string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType;
    return Results.File(file.Content, mime, file.FileName);
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

// POST /api/agents - crea un agente IA nuevo. Hasta ahora solo se podia desde /agentes web (UI
// Blazor); exponerlo por API permite guionizar la creacion de agentes desde fuera (p.ej. clonar
// un asesor para otro producto/lider sin pasar por la pantalla). Body JSON:
// {
//   "name": "FUXION_reclutamiento",
//   "role": "asesor comercial red",     // opcional
//   "provider": "Gemini",                // "Claude" | "Gemini" | "ChatGpt" | "DeepSeek"
//   "model": "gemini-2.5-pro",           // opcional
//   "systemPrompt": "...",               // puede ir vacio y editarse despues con /system-prompt
//   "enableMcp": true,                   // DataContainer MCP (default false)
//   "reactionsEnabled": false,
//   "reactionRatioN": 3,
//   "reactionRatioM": 4,
//   "reactionEmojis": "👍, 🙏"
// }
// Devuelve el DTO del agente creado (incluye id para configurar pagos/prompt/pipeline despues).
app.MapPost("/api/agents", async (
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
        using var body = await System.Text.Json.JsonDocument.ParseAsync(http.Request.Body, cancellationToken: ct);
        var root = body.RootElement;
        string? Str(string key) => root.TryGetProperty(key, out var e) && e.ValueKind == System.Text.Json.JsonValueKind.String ? e.GetString() : null;
        bool Bool(string key, bool def) => root.TryGetProperty(key, out var e) && (e.ValueKind == System.Text.Json.JsonValueKind.True || e.ValueKind == System.Text.Json.JsonValueKind.False) ? e.GetBoolean() : def;
        int Int(string key, int def) => root.TryGetProperty(key, out var e) && e.ValueKind == System.Text.Json.JsonValueKind.Number ? e.GetInt32() : def;

        var name = Str("name");
        if (string.IsNullOrWhiteSpace(name)) { return Results.BadRequest(new { error = "name es obligatorio" }); }

        var providerStr = Str("provider") ?? "Gemini";
        if (!Enum.TryParse<CubotRedManager.Domain.Enums.AiProvider>(providerStr, ignoreCase: true, out var provider))
        {
            return Results.BadRequest(new { error = "provider invalido. Valores validos: Claude, Gemini, ChatGpt, DeepSeek" });
        }

        var req = new CubotRedManager.Application.Tenancy.CreateAiAgentRequest(
            Name: name!,
            Role: Str("role"),
            Provider: provider,
            Model: Str("model"),
            SystemPrompt: Str("systemPrompt") ?? string.Empty,
            EnableDataContainerMcp: Bool("enableMcp", false),
            ReactionsEnabled: Bool("reactionsEnabled", false),
            ReactionRatioN: Int("reactionRatioN", 3),
            ReactionRatioM: Int("reactionRatioM", 4),
            ReactionEmojis: Str("reactionEmojis"));

        var created = await svc.CreateAsync(req, ident.UserId, ct);
        return created is null
            ? Results.BadRequest(new { error = "No se pudo crear el agente (nombre duplicado?)" })
            : Results.Created($"/api/agents/{created.Id}", created);
    }
    finally { ambient.Set(null, null); }
}).AllowAnonymous().DisableAntiforgery();

// PATCH /api/agents/{id} - edita metadata del agente (name, role, provider, model, isActive, flags).
// NO toca el systemPrompt (usa /system-prompt, protegido por X-Confirm-Agent-Edit), ni el pago
// (usa /payment-config), ni el pipeline (usa /pipeline-config). Es "update parcial" porque algunos
// campos opcionales no se setean si no vienen (como el servicio UpdateAsync usa un replace, aqui
// reconstruimos el DTO con los valores previos y sobreescribimos solo los que vinieron).
// Body JSON (todos opcionales):
// { "name":"...", "role":"...", "provider":"Gemini", "model":"gemini-2.5-pro",
//   "isActive": true, "enableMcp": true, "reactionsEnabled": false,
//   "reactionRatioN": 3, "reactionRatioM": 4, "reactionEmojis": "👍" }
app.MapMethods("/api/agents/{id:guid}", new[] { "PATCH" }, async (
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
        var current = await svc.GetAsync(id, ct);
        if (current is null) { return Results.NotFound(); }

        using var body = await System.Text.Json.JsonDocument.ParseAsync(http.Request.Body, cancellationToken: ct);
        var root = body.RootElement;
        bool Has(string key) => root.TryGetProperty(key, out _);
        string? Str(string key) => root.TryGetProperty(key, out var e) && e.ValueKind == System.Text.Json.JsonValueKind.String ? e.GetString() : null;
        bool Bool(string key, bool def) => root.TryGetProperty(key, out var e) && (e.ValueKind == System.Text.Json.JsonValueKind.True || e.ValueKind == System.Text.Json.JsonValueKind.False) ? e.GetBoolean() : def;
        int Int(string key, int def) => root.TryGetProperty(key, out var e) && e.ValueKind == System.Text.Json.JsonValueKind.Number ? e.GetInt32() : def;

        var providerStr = Str("provider");
        var provider = current.Agent.Provider;
        if (providerStr is not null)
        {
            if (!Enum.TryParse<CubotRedManager.Domain.Enums.AiProvider>(providerStr, ignoreCase: true, out provider))
            {
                return Results.BadRequest(new { error = "provider invalido" });
            }
        }

        var req = new CubotRedManager.Application.Tenancy.UpdateAiAgentRequest(
            Name: Str("name") ?? current.Agent.Name,
            Role: Has("role") ? Str("role") : current.Agent.Role,
            Provider: provider,
            Model: Has("model") ? Str("model") : current.Agent.Model,
            SystemPrompt: current.Agent.SystemPrompt,   // NO se toca por aqui — hay endpoint dedicado /system-prompt.
            EnableDataContainerMcp: Bool("enableMcp", current.Agent.EnableDataContainerMcp),
            ReactionsEnabled: Bool("reactionsEnabled", current.Agent.ReactionsEnabled),
            ReactionRatioN: Int("reactionRatioN", current.Agent.ReactionRatioN),
            ReactionRatioM: Int("reactionRatioM", current.Agent.ReactionRatioM),
            ReactionEmojis: Has("reactionEmojis") ? Str("reactionEmojis") : current.Agent.ReactionEmojis,
            CreateLeadsInPipeline: Bool("createLeadsInPipeline", current.Agent.CreateLeadsInPipeline),
            DefaultPipelineId: current.Agent.DefaultPipelineId);

        var updated = await svc.UpdateAsync(id, req, ident.UserId, ct);
        return updated is null ? Results.NotFound() : Results.Ok(updated);
    }
    finally { ambient.Set(null, null); }
}).AllowAnonymous().DisableAntiforgery();

// GET /api/debug/webhook-payloads?provider=ycloud&take=20 - ultimos payloads que llegaron al
// webhook del proveedor indicado (ycloud | evolution). Devuelve metadatos + preview del body
// (hasta 4000 chars) + conteo de mensajes/reacciones que el parser reconocio. Pensado para
// diagnosticar: "YCloud configuro mi webhook pero no entra nada" -> si no aparece nada aqui,
// YCloud no esta disparando; si aparece pero con parsedMessages=0, el parser no reconoce el
// formato que YCloud esta mandando. Buffer en memoria, no persiste al reiniciar el container.
app.MapGet("/api/debug/webhook-payloads", async (
    HttpContext http,
    CubotRedManager.Application.Tenancy.IApiTokenService tokens,
    CubotRedManager.Application.Abstractions.IAmbientTenantOverride ambient,
    CubotRedManager.Web.Webhooks.WebhookPayloadBuffer buffer,
    string? provider,
    int? take) =>
{
    var ident = await AuthenticateApiTokenAsync(http, tokens, ambient);
    if (ident is null) { return Results.Unauthorized(); }
    try
    {
        var p = string.IsNullOrWhiteSpace(provider) ? "ycloud" : provider.ToLowerInvariant();
        var entries = buffer.Snapshot(p, take ?? 20);
        return Results.Ok(new { ok = true, provider = p, total = entries.Count, entries });
    }
    finally { ambient.Set(null, null); }
}).AllowAnonymous().DisableAntiforgery();

// GET /api/me - identidad resuelta del X-Api-Token: tenantId + userId. Util para armar URLs
// que llevan el tenantId (p.ej. el webhook de YCloud: /webhooks/ycloud/{tenantId}) sin tener
// que consultarlo en la UI.
app.MapGet("/api/me", async (
    HttpContext http,
    CubotRedManager.Application.Tenancy.IApiTokenService tokens,
    CubotRedManager.Application.Abstractions.IAmbientTenantOverride ambient,
    CancellationToken ct) =>
{
    _ = ct;
    var ident = await AuthenticateApiTokenAsync(http, tokens, ambient);
    if (ident is null) { return Results.Unauthorized(); }
    try
    {
        return Results.Ok(new {
            ok = true,
            tenantId = ident.TenantId,
            userId = ident.UserId,
            webhooks = new {
                ycloud = $"https://red.cubot.com.co/webhooks/ycloud/{ident.TenantId}",
                evolution = $"https://red.cubot.com.co/webhooks/evolution/{ident.TenantId}"
            }
        });
    }
    finally { ambient.Set(null, null); }
}).AllowAnonymous().DisableAntiforgery();

// GET /api/whatsapp-lines - lista las lineas de WhatsApp del tenant (todos los proveedores:
// Evolution, YCloud, Cloud API Meta, Emulator) con su proveedor, status, phone, waba_id, y el
// agente IA vinculado (si lo tiene). Util para diagnosticar por que un mensaje de un numero
// real no llega al agente: si falta el binding, falta el status Connected, o la linea no existe.
app.MapGet("/api/whatsapp-lines", async (
    HttpContext http,
    CubotRedManager.Application.Tenancy.IApiTokenService tokens,
    CubotRedManager.Application.Abstractions.IAmbientTenantOverride ambient,
    CubotRedManager.Application.Abstractions.IApplicationDbContext db,
    CancellationToken ct) =>
{
    var ident = await AuthenticateApiTokenAsync(http, tokens, ambient);
    if (ident is null) { return Results.Unauthorized(); }
    try
    {
        var lines = await db.WhatsAppLines.AsNoTracking()
            .OrderBy(l => l.InstanceName)
            .Select(l => new {
                l.Id, l.InstanceName, l.PhoneNumber, l.Provider, l.Status,
                l.LastConnectedAt, l.LastStatusAt,
                // Datos de proveedor NO sensibles (ids/wabas; nunca tokens):
                yCloudWabaId = l.YCloudWabaId,
                yCloudPhoneNumberId = l.YCloudPhoneNumberId,
                cloudPhoneNumberId = l.CloudPhoneNumberId,
                cloudWabaId = l.CloudBusinessAccountId
            })
            .ToListAsync(ct);
        var bindings = await db.AiAgentLineBindings.AsNoTracking()
            .Where(b => b.IsConnected)
            .Select(b => new { b.WhatsAppLineId, b.AgentId, b.AutoConfirm })
            .ToListAsync(ct);
        var agentNames = await db.AiAgents.AsNoTracking()
            .Select(a => new { a.Id, a.Name })
            .ToDictionaryAsync(a => a.Id, a => a.Name, ct);
        var byLine = bindings.ToDictionary(b => b.WhatsAppLineId);
        var enriched = lines.Select(l => {
            byLine.TryGetValue(l.Id, out var b);
            return new {
                l.Id, l.InstanceName, l.PhoneNumber, provider = l.Provider.ToString(), status = l.Status.ToString(),
                l.LastConnectedAt, l.LastStatusAt,
                l.yCloudWabaId, l.yCloudPhoneNumberId, l.cloudPhoneNumberId, l.cloudWabaId,
                boundAgentId = b?.AgentId,
                boundAgentName = b?.AgentId is Guid aid && agentNames.TryGetValue(aid, out var nm) ? nm : null,
                autoConfirm = b?.AutoConfirm
            };
        }).ToList();
        return Results.Ok(new { ok = true, total = enriched.Count, lines = enriched });
    }
    finally { ambient.Set(null, null); }
}).AllowAnonymous().DisableAntiforgery();

// GET /api/agents/{id}/cache-fields - lista los campos que el agente debe capturar durante la
// conversacion (pais, motivacion, horas disponibles, etc.). El "extractor" LLM del AiInferenceService
// corre despues de cada respuesta y actualiza AiAgentCacheValues con lo que detecte en el chat.
// Al cierre con [[crear_lead_pipeline]], el LeadMarkerProcessor vuelca esos values al Lead.
app.MapGet("/api/agents/{id:guid}/cache-fields", async (
    Guid id,
    HttpContext http,
    CubotRedManager.Application.Tenancy.IApiTokenService tokens,
    CubotRedManager.Application.Abstractions.IAmbientTenantOverride ambient,
    CubotRedManager.Application.Tenancy.IAiAgentCacheService cacheSvc,
    CancellationToken ct) =>
{
    var ident = await AuthenticateApiTokenAsync(http, tokens, ambient);
    if (ident is null) { return Results.Unauthorized(); }
    try { return Results.Ok(await cacheSvc.ListFieldsAsync(id, ct)); }
    finally { ambient.Set(null, null); }
}).AllowAnonymous().DisableAntiforgery();

// POST /api/agents/{id}/cache-fields - crea en bulk los campos de captura del agente. Body:
// { "fields": [{"label": "Pais", "description": "Pais de residencia", "isUpdatable": true}, ...] }
// El FieldKey se auto-genera por slugify del Label (sin acentos, en snake_case). Idempotente a
// nivel de key: si el agente ya tiene un field con el mismo key, se omite silenciosamente para
// que ejecutar la misma llamada dos veces no genere duplicados ni rompa el flujo.
app.MapPost("/api/agents/{id:guid}/cache-fields", async (
    Guid id,
    HttpContext http,
    CubotRedManager.Application.Tenancy.IApiTokenService tokens,
    CubotRedManager.Application.Abstractions.IAmbientTenantOverride ambient,
    CubotRedManager.Application.Tenancy.IAiAgentCacheService cacheSvc,
    CancellationToken ct) =>
{
    var ident = await AuthenticateApiTokenAsync(http, tokens, ambient);
    if (ident is null) { return Results.Unauthorized(); }
    try
    {
        using var body = await System.Text.Json.JsonDocument.ParseAsync(http.Request.Body, cancellationToken: ct);
        if (!body.RootElement.TryGetProperty("fields", out var fieldsEl) || fieldsEl.ValueKind != System.Text.Json.JsonValueKind.Array)
        {
            return Results.BadRequest(new { error = "body debe tener {fields: [...]}" });
        }

        var existing = (await cacheSvc.ListFieldsAsync(id, ct))
            .Select(f => f.FieldKey)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var created = new List<CubotRedManager.Application.Tenancy.AiAgentCacheFieldDto>();
        var skipped = new List<string>();

        foreach (var el in fieldsEl.EnumerateArray())
        {
            if (el.ValueKind != System.Text.Json.JsonValueKind.Object) { continue; }
            var label = el.TryGetProperty("label", out var lbl) && lbl.ValueKind == System.Text.Json.JsonValueKind.String ? lbl.GetString() : null;
            if (string.IsNullOrWhiteSpace(label)) { continue; }
            var description = el.TryGetProperty("description", out var dsc) && dsc.ValueKind == System.Text.Json.JsonValueKind.String ? dsc.GetString() : null;
            var isUpdatable = !el.TryGetProperty("isUpdatable", out var upd)
                || upd.ValueKind != System.Text.Json.JsonValueKind.False;

            // Pre-check de key duplicado: generamos el slug igual que el service para evitar
            // crear uno con "label 2" cuando ya existe el mismo semantico.
            var slug = Slugify(label!);
            if (existing.Contains(slug)) { skipped.Add(slug); continue; }

            var req = new CubotRedManager.Application.Tenancy.CreateAgentCacheFieldRequest(id, label!, description, isUpdatable);
            var dto = await cacheSvc.CreateFieldAsync(req, ident.UserId, ct);
            if (dto is not null)
            {
                created.Add(dto);
                existing.Add(dto.FieldKey);
            }
        }
        return Results.Ok(new { ok = true, created, skippedKeys = skipped });
    }
    finally { ambient.Set(null, null); }

    // Replica el slugify del AiAgentCacheService (sin acentos, en snake_case).
    static string Slugify(string label)
    {
        var normalized = label.Trim().ToLowerInvariant();
        var sb = new System.Text.StringBuilder(normalized.Length);
        foreach (var ch in normalized)
        {
            if (ch >= 'a' && ch <= 'z') { sb.Append(ch); }
            else if (ch >= '0' && ch <= '9') { sb.Append(ch); }
            else if (ch == 'ñ') { sb.Append('n'); }
            else if ("áàäâã".Contains(ch)) { sb.Append('a'); }
            else if ("éèëê".Contains(ch)) { sb.Append('e'); }
            else if ("íìïî".Contains(ch)) { sb.Append('i'); }
            else if ("óòöôõ".Contains(ch)) { sb.Append('o'); }
            else if ("úùüû".Contains(ch)) { sb.Append('u'); }
            else if (ch == ' ' || ch == '-' || ch == '_') { if (sb.Length > 0 && sb[^1] != '_') { sb.Append('_'); } }
        }
        var s = sb.ToString().Trim('_');
        return string.IsNullOrEmpty(s) ? "dato" : s;
    }
}).AllowAnonymous().DisableAntiforgery();

// POST /api/agents/{id}/active - encender o apagar el agente. Body JSON: {"active": true/false}.
// Un agente recien creado por POST /api/agents arranca apagado (IsActive=false) para que el operador
// lo revise antes de dejar que atienda. Este endpoint es el toggle que la UI /agentes hace por un
// click; via API permite scripts tipo "crea + configura + activa" en una secuencia unica.
app.MapPost("/api/agents/{id:guid}/active", async (
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
        using var body = await System.Text.Json.JsonDocument.ParseAsync(http.Request.Body, cancellationToken: ct);
        if (!body.RootElement.TryGetProperty("active", out var activeEl) ||
            (activeEl.ValueKind != System.Text.Json.JsonValueKind.True && activeEl.ValueKind != System.Text.Json.JsonValueKind.False))
        {
            return Results.BadRequest(new { error = "body debe tener {active: true/false}" });
        }
        var updated = await svc.SetActiveAsync(id, activeEl.GetBoolean(), ident.UserId, ct);
        return updated is null ? Results.NotFound() : Results.Ok(updated);
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

// ===== GET /api/pipelines =====
// Lista los pipelines del tenant (ApiToken). Util para configurar DefaultPipelineId de un agente
// desde un script externo sin pasar por la UI.
app.MapGet("/api/pipelines", async (
    HttpContext http,
    CubotRedManager.Application.Tenancy.IApiTokenService tokens,
    CubotRedManager.Application.Abstractions.IAmbientTenantOverride ambient,
    CubotRedManager.Application.Tenancy.IPipelineService pipelines,
    CancellationToken ct) =>
{
    var ident = await AuthenticateApiTokenAsync(http, tokens, ambient);
    if (ident is null) { return Results.Unauthorized(); }
    try
    {
        return Results.Ok(await pipelines.ListPipelinesAsync(ct));
    }
    finally { ambient.Set(null, null); }
}).AllowAnonymous().DisableAntiforgery();

// GET /api/pipelines/{id} - detalle del Kanban: pipeline + sus stages (en orden) + los leads
// activos de cada stage con sus FieldValues capturados. Pensado para que un script o un asistente
// externo revise el embudo sin pasar por la UI Blazor. No incluye leads archivados ni actividades
// (hay endpoints dedicados cuando se necesiten). Mapeo cliente -> stage por id para visualizar.
app.MapGet("/api/pipelines/{id:guid}", async (
    Guid id,
    HttpContext http,
    CubotRedManager.Application.Tenancy.IApiTokenService tokens,
    CubotRedManager.Application.Abstractions.IAmbientTenantOverride ambient,
    CubotRedManager.Application.Tenancy.IPipelineService pipelines,
    CubotRedManager.Application.Tenancy.ILeadService leads,
    CancellationToken ct) =>
{
    var ident = await AuthenticateApiTokenAsync(http, tokens, ambient);
    if (ident is null) { return Results.Unauthorized(); }
    try
    {
        var all = await pipelines.ListPipelinesAsync(ct);
        var pipeline = all.FirstOrDefault(p => p.Id == id);
        if (pipeline is null) { return Results.NotFound(new { error = "pipeline no encontrado" }); }
        var stages = await pipelines.ListStagesAsync(id, ct);
        var allLeads = await leads.ListAsync(pipelineId: id, cancellationToken: ct);
        // Agrupamos los leads por stage (en orden de SortOrder) y los serializamos con sus field
        // values. Asi un consumidor arma el Kanban sin otra consulta.
        var stagesWithLeads = stages
            .OrderBy(s => s.SortOrder)
            .Select(s => new {
                stage = s,
                leads = allLeads
                    .Where(l => l.StageId == s.Id)
                    .OrderByDescending(l => l.StageChangedAt)
                    .Select(l => new {
                        l.Id, l.ContactName, l.ContactPhone, l.Topic,
                        l.Status, l.StageChangedAt, l.CreatedAt,
                        l.AttentionDurationSeconds,
                        fieldValues = l.FieldValues
                    })
                    .ToList()
            })
            .ToList();
        return Results.Ok(new {
            pipeline,
            stageCount = stages.Count,
            totalLeads = allLeads.Count,
            stages = stagesWithLeads
        });
    }
    finally { ambient.Set(null, null); }
}).AllowAnonymous().DisableAntiforgery();

// ===== POST /api/agents/{id}/system-prompt =====
// Reescribe el system prompt de un agente (preservando el resto de su config). Doble guard porque
// cambiar el prompt afecta como responde el agente a conversaciones REALES:
//  1) ApiToken valido del tenant (el helper setea el ambient).
//  2) Header X-Confirm-Agent-Edit: yes  (opt-in explicito del script que llama).
//  3) Prompt minimo 50 chars (previene envios accidentales vacios/null).
// La auditoria queda registrada por AiAgentService.UpdateAsync (super_admin_audit_logs).
app.MapPost("/api/agents/{id:guid}/system-prompt", async (
    Guid id,
    HttpContext http,
    CubotRedManager.Application.Tenancy.IApiTokenService tokens,
    CubotRedManager.Application.Abstractions.IAmbientTenantOverride ambient,
    CubotRedManager.Application.Tenancy.IAiAgentService agents,
    SystemPromptRequest body,
    CancellationToken ct) =>
{
    if (!string.Equals(http.Request.Headers["X-Confirm-Agent-Edit"].ToString(), "yes", StringComparison.OrdinalIgnoreCase))
    {
        return Results.BadRequest(new { error = "Falta header X-Confirm-Agent-Edit: yes (opt-in requerido)." });
    }
    var newPrompt = body.SystemPrompt ?? "";
    if (newPrompt.Length < 50)
    {
        return Results.BadRequest(new { error = "El prompt debe tener al menos 50 caracteres." });
    }
    var ident = await AuthenticateApiTokenAsync(http, tokens, ambient);
    if (ident is null) { return Results.Unauthorized(); }
    try
    {
        var detail = await agents.GetAsync(id, ct);
        if (detail is null) { return Results.NotFound(new { error = "Agente no encontrado." }); }
        var a = detail.Agent;
        var updated = await agents.UpdateAsync(id, new CubotRedManager.Application.Tenancy.UpdateAiAgentRequest(
            a.Name, a.Role, a.Provider, a.Model, newPrompt, a.EnableDataContainerMcp,
            a.ReactionsEnabled, a.ReactionRatioN, a.ReactionRatioM, a.ReactionEmojis,
            a.CreateLeadsInPipeline, a.DefaultPipelineId), ident.UserId, ct);
        return updated is null ? Results.NotFound() : Results.Ok(new { ok = true, systemPromptLength = newPrompt.Length, previousLength = a.SystemPrompt.Length });
    }
    finally { ambient.Set(null, null); }
}).AllowAnonymous().DisableAntiforgery();

// ===== Plantillas HSM (PR G) =====

// POST /api/whatsapp-templates/{id}/send
// Envia una plantilla HSM aprobada a un numero de prueba via la linea indicada. Autenticado por
// X-Api-Token. Body: { "whatsAppLineId": "GUID", "phone": "573001234567", "values": ["Juan","15:00"],
// "headerMediaUrl": "https://..." }. Devuelve ok + messageId del provider si se logro.
app.MapPost("/api/whatsapp-templates/{id:guid}/send", async (
    Guid id,
    HttpContext http,
    CubotRedManager.Application.Tenancy.IApiTokenService tokens,
    CubotRedManager.Application.Abstractions.IAmbientTenantOverride ambient,
    CubotRedManager.Application.Tenancy.IWhatsAppTemplateService svc,
    TemplateSendRequest body,
    CancellationToken ct) =>
{
    var ident = await AuthenticateApiTokenAsync(http, tokens, ambient);
    if (ident is null) { return Results.Unauthorized(); }
    try
    {
        var res = await svc.TestSendAsync(id, body.WhatsAppLineId, body.Phone ?? "",
            body.Values, body.HeaderMediaUrl, ident.UserId, ct);
        return res.Ok
            ? Results.Ok(new { ok = true, messageId = res.MessageId })
            : Results.BadRequest(new { ok = false, error = res.Error });
    }
    finally { ambient.Set(null, null); }
}).AllowAnonymous().DisableAntiforgery();

// POST /api/whatsapp-templates/import/{lineId}
// Importa (upsert) todas las plantillas que YCloud tiene para la WABA de la linea. Devuelve
// contadores imported/updated/skipped.
app.MapPost("/api/whatsapp-templates/import/{lineId:guid}", async (
    Guid lineId,
    HttpContext http,
    CubotRedManager.Application.Tenancy.IApiTokenService tokens,
    CubotRedManager.Application.Abstractions.IAmbientTenantOverride ambient,
    CubotRedManager.Application.Tenancy.IWhatsAppTemplateService svc,
    CancellationToken ct) =>
{
    var ident = await AuthenticateApiTokenAsync(http, tokens, ambient);
    if (ident is null) { return Results.Unauthorized(); }
    try
    {
        var r = await svc.ImportFromYCloudAsync(lineId, ident.UserId, ct);
        return r.Ok
            ? Results.Ok(new { ok = true, imported = r.Imported, updated = r.Updated, skipped = r.Skipped })
            : Results.BadRequest(new { ok = false, error = r.Error });
    }
    finally { ambient.Set(null, null); }
}).AllowAnonymous().DisableAntiforgery();

// POST /api/whatsapp-templates/{id}/active  Body: { "active": true/false }
// Soft-delete / rehabilitar. Devuelve el nuevo Status.
app.MapPost("/api/whatsapp-templates/{id:guid}/active", async (
    Guid id,
    HttpContext http,
    CubotRedManager.Application.Tenancy.IApiTokenService tokens,
    CubotRedManager.Application.Abstractions.IAmbientTenantOverride ambient,
    CubotRedManager.Application.Tenancy.IWhatsAppTemplateService svc,
    TemplateSetActiveRequest body,
    CancellationToken ct) =>
{
    var ident = await AuthenticateApiTokenAsync(http, tokens, ambient);
    if (ident is null) { return Results.Unauthorized(); }
    try
    {
        var status = await svc.SetActiveAsync(id, body.Active, ident.UserId, ct);
        return status is null
            ? Results.NotFound(new { error = "Plantilla no encontrada." })
            : Results.Ok(new { ok = true, status });
    }
    finally { ambient.Set(null, null); }
}).AllowAnonymous().DisableAntiforgery();

// ===== Bitacora del agente (PR H) =====
// Expone la bitacora de atencion del agente IA por API REST autenticada con X-Api-Token, para que
// un operador o un asistente externo pueda diagnosticar por que una conversacion no respondio
// como se esperaba (link_pago no entregado, prompt mal armado, etc.) sin pasar por el login web.
// Devuelve ContactPhone y ContactName REALES (gracias al JOIN del PR H1), de modo que la respuesta
// sirva para analisis sin tener que mapear GUIDs a mano.

// GET /api/agent-bitacora/conversations?take=50&q=<texto>
// Lista las conversaciones del tenant que tienen bitacora, mas recientes primero. "q" filtra en
// memoria por telefono o nombre (contains, case-insensitive) para ergonomia; para listados
// grandes conviene paginar por take.
app.MapGet("/api/agent-bitacora/conversations", async (
    HttpContext http,
    CubotRedManager.Application.Tenancy.IApiTokenService tokens,
    CubotRedManager.Application.Abstractions.IAmbientTenantOverride ambient,
    CubotRedManager.Application.Tenancy.IAgentRunLogService svc,
    int? take,
    string? q,
    CancellationToken ct) =>
{
    var ident = await AuthenticateApiTokenAsync(http, tokens, ambient);
    if (ident is null) { return Results.Unauthorized(); }
    try
    {
        var all = await svc.ListConversationsAsync(ct);
        IEnumerable<CubotRedManager.Application.Tenancy.AgentRunLogConversationDto> result = all;
        if (!string.IsNullOrWhiteSpace(q))
        {
            var needle = q.Trim();
            result = all.Where(c =>
                (c.ContactPhone != null && c.ContactPhone.Contains(needle, StringComparison.OrdinalIgnoreCase))
                || (c.ContactName != null && c.ContactName.Contains(needle, StringComparison.OrdinalIgnoreCase)));
        }
        var limited = result.Take(Math.Clamp(take ?? 50, 1, 500)).ToList();
        return Results.Ok(new { ok = true, total = limited.Count, items = limited });
    }
    finally { ambient.Set(null, null); }
}).AllowAnonymous().DisableAntiforgery();

// GET /api/agent-bitacora/{conversationId}
// Devuelve la linea de tiempo completa de una conversacion (orden cronologico, mas vieja primero).
// Content y Response se devuelven completos (NO se trunca); el consumidor decide como mostrar.
app.MapGet("/api/agent-bitacora/{conversationId:guid}", async (
    Guid conversationId,
    HttpContext http,
    CubotRedManager.Application.Tenancy.IApiTokenService tokens,
    CubotRedManager.Application.Abstractions.IAmbientTenantOverride ambient,
    CubotRedManager.Application.Tenancy.IAgentRunLogService svc,
    CancellationToken ct) =>
{
    var ident = await AuthenticateApiTokenAsync(http, tokens, ambient);
    if (ident is null) { return Results.Unauthorized(); }
    try
    {
        var entries = await svc.GetConversationLogAsync(conversationId, ct);
        return Results.Ok(new { ok = true, conversationId, total = entries.Count, entries });
    }
    finally { ambient.Set(null, null); }
}).AllowAnonymous().DisableAntiforgery();

// GET /api/agent-bitacora/by-phone/{phone}
// Shortcut de diagnostico: busca la ultima conversacion cuyo ContactPhone contenga los digitos de
// {phone} (tolerante al formato: "+57 300 123 4567" matchea "573001234567") y devuelve el header
// de la conversacion + su bitacora completa en una sola llamada. Pensado para "revisa que esta
// pasando con el numero X" sin tener que listar primero y copiar un GUID.
app.MapGet("/api/agent-bitacora/by-phone/{phone}", async (
    string phone,
    HttpContext http,
    CubotRedManager.Application.Tenancy.IApiTokenService tokens,
    CubotRedManager.Application.Abstractions.IAmbientTenantOverride ambient,
    CubotRedManager.Application.Tenancy.IAgentRunLogService svc,
    CancellationToken ct) =>
{
    var ident = await AuthenticateApiTokenAsync(http, tokens, ambient);
    if (ident is null) { return Results.Unauthorized(); }
    try
    {
        var conv = await svc.FindLatestByPhoneAsync(phone, ct);
        if (conv is null)
        {
            return Results.NotFound(new { ok = false, error = $"Sin conversacion con bitacora que matchee '{phone}'." });
        }
        var entries = await svc.GetConversationLogAsync(conv.ConversationId, ct);
        return Results.Ok(new { ok = true, conversation = conv, total = entries.Count, entries });
    }
    finally { ambient.Set(null, null); }
}).AllowAnonymous().DisableAntiforgery();

// ===== POST /api/agents/{id}/pipeline-config =====
// Activa/desactiva CreateLeadsInPipeline y setea DefaultPipelineId en un agente IA. Body JSON:
//   { "createLeadsInPipeline": true, "defaultPipelineId": "GUID" }
// Preserva los demas campos del agente (Name, Role, Provider, Model, Prompt, EnableMcp, Reactions).
app.MapPost("/api/agents/{id:guid}/pipeline-config", async (
    Guid id,
    HttpContext http,
    CubotRedManager.Application.Tenancy.IApiTokenService tokens,
    CubotRedManager.Application.Abstractions.IAmbientTenantOverride ambient,
    CubotRedManager.Application.Tenancy.IAiAgentService agents,
    PipelineConfigRequest body,
    CancellationToken ct) =>
{
    var ident = await AuthenticateApiTokenAsync(http, tokens, ambient);
    if (ident is null) { return Results.Unauthorized(); }
    try
    {
        var detail = await agents.GetAsync(id, ct);
        if (detail is null) { return Results.NotFound(new { error = "Agente no encontrado." }); }
        var a = detail.Agent;
        var updated = await agents.UpdateAsync(id, new CubotRedManager.Application.Tenancy.UpdateAiAgentRequest(
            a.Name, a.Role, a.Provider, a.Model, a.SystemPrompt, a.EnableDataContainerMcp,
            a.ReactionsEnabled, a.ReactionRatioN, a.ReactionRatioM, a.ReactionEmojis,
            body.CreateLeadsInPipeline, body.DefaultPipelineId), ident.UserId, ct);
        return updated is null ? Results.NotFound() : Results.Ok(updated);
    }
    finally { ambient.Set(null, null); }
}).AllowAnonymous().DisableAntiforgery();

// ===== POST /api/test/agent =====
// Emulador de linea de WhatsApp: inyecta un mensaje "como si fuera el cliente" y corre el runtime
// REAL del agente (ChatIngestService + AgentDispatcher) sin que la respuesta salga a ningun provider.
// Portado del hermano ECOREX.tareas con dos diferencias clave:
//  - La linea Emulator es por-tenant, no por-agente (reutilizable).
//  - El binding emulado NO toca bindings reales: la linea Emulator es exclusiva, solo rotamos cual
//    agente la atiende en cada prueba. Los bindings a lineas reales (Evolution/Cloud/YCloud) quedan
//    intactos, asi que probar un agente NO rompe su WhatsApp real.
// Autenticacion dual: (a) cookie Blazor con tenant_id (consola), o (b) header X-Api-Token con
// un ApiToken opaco del tenant (scripts externos, pruebas desde otro cliente HTTP). El primero
// que resuelva un tenant valido manda.
app.MapPost("/api/test/agent", async (
    TestAgentRequest req,
    HttpContext http,
    CubotRedManager.Application.Abstractions.IApplicationDbContext db,
    CubotRedManager.Application.Tenancy.IChatIngestService ingest,
    CubotRedManager.Application.Tenancy.IAgentDispatcher dispatcher,
    CubotRedManager.Application.Tenancy.IAiAgentLineBindingService bindings,
    CubotRedManager.Application.Tenancy.IApiTokenService tokens,
    CubotRedManager.Application.Abstractions.IAmbientTenantOverride ambient,
    TimeProvider time,
    CancellationToken ct) =>
{
    // 1. Tenant: primero el claim de la cookie; si no hay, X-Api-Token.
    Guid tenantId;
    Guid actorUserId;
    var tenantClaim = http.User.FindFirst("tenant_id")?.Value;
    if (Guid.TryParse(tenantClaim, out tenantId))
    {
        Guid.TryParse(http.User.FindFirstValue(System.Security.Claims.ClaimTypes.NameIdentifier)
                       ?? http.User.FindFirstValue("sub"), out actorUserId);
    }
    else
    {
        var tokenIdent = await AuthenticateApiTokenAsync(http, tokens, ambient);
        if (tokenIdent is null) { return Results.Unauthorized(); }
        tenantId = tokenIdent.TenantId;
        actorUserId = tokenIdent.UserId;
    }
    ambient.Set(tenantId, actorUserId == Guid.Empty ? null : actorUserId);
    try
    {
        // 2. Linea emulada: busca o crea UNA por tenant, reutilizable.
        var line = await db.WhatsAppLines.IgnoreQueryFilters()
            .FirstOrDefaultAsync(l => l.TenantId == tenantId && l.Provider == CubotRedManager.Domain.Enums.WhatsAppProvider.Emulator, ct);
        if (line is null)
        {
            line = new CubotRedManager.Domain.Entities.WhatsAppLine
            {
                TenantId = tenantId,
                Provider = CubotRedManager.Domain.Enums.WhatsAppProvider.Emulator,
                InstanceName = "Canal de pruebas",
                Status = CubotRedManager.Domain.Enums.WhatsAppLineStatus.Connected,
                PhoneNumber = "emulator",
                LastConnectedAt = time.GetUtcNow(),
                LastStatusAt = time.GetUtcNow()
            };
            db.WhatsAppLines.Add(line);
            await db.SaveChangesAsync(ct);
        }

        // 3. Agente a probar: el del body, o el primer activo del tenant.
        Guid agentId;
        if (req.AgentId is Guid aid)
        {
            agentId = aid;
            var owned = await db.AiAgents.IgnoreQueryFilters()
                .AnyAsync(a => a.Id == agentId && a.TenantId == tenantId, ct);
            if (!owned) { return Results.BadRequest(new { error = "El agente no pertenece a esta agencia o no existe." }); }
        }
        else
        {
            var first = await db.AiAgents.IgnoreQueryFilters()
                .Where(a => a.TenantId == tenantId && a.IsActive)
                .OrderBy(a => a.SortOrder)
                .Select(a => (Guid?)a.Id).FirstOrDefaultAsync(ct);
            if (first is null) { return Results.BadRequest(new { error = "No hay agentes activos en esta agencia." }); }
            agentId = first.Value;
        }

        // 4. Binding: la linea Emulator debe estar atendida SOLO por este agente. Desconectamos a
        //    cualquier otro agente que la tuviera y conectamos a este. No tocamos bindings de lineas
        //    reales — por eso probar un agente nunca rompe su WhatsApp productivo.
        var otherBindings = await db.AiAgentLineBindings.IgnoreQueryFilters()
            .Where(b => b.TenantId == tenantId && b.WhatsAppLineId == line.Id && b.IsConnected && b.AgentId != agentId)
            .Select(b => b.AgentId)
            .ToListAsync(ct);
        foreach (var otherAgentId in otherBindings)
        {
            await bindings.SetAsync(new CubotRedManager.Application.Tenancy.SetAgentLineBindingRequest(otherAgentId, line.Id, false, true), actorUserId, ct);
        }
        await bindings.SetAsync(new CubotRedManager.Application.Tenancy.SetAgentLineBindingRequest(agentId, line.Id, true, true), actorUserId, ct);

        // 5. Ingesta: crea Conversation + Message entrante igual que el webhook real. enqueueDispatch
        //    = false porque corremos el dispatcher sincrono en el paso 7.
        var phone = string.IsNullOrWhiteSpace(req.ContactPhone) ? "999999999999" : new string(req.ContactPhone.Where(char.IsDigit).ToArray());
        if (string.IsNullOrWhiteSpace(phone)) { phone = "999999999999"; }
        var now = time.GetUtcNow();
        var inboundText = req.Text ?? string.Empty;
        var payload = new CubotRedManager.Application.Tenancy.IngestMessageRequest(
            ContactPhone: phone,
            ContactName: string.IsNullOrWhiteSpace(req.ContactName) ? "Cliente de prueba" : req.ContactName!.Trim(),
            ExternalMessageId: $"emu-{Guid.NewGuid():N}",
            Body: inboundText,
            MessageType: "text",
            SentAt: now,
            WhatsAppLineId: line.Id);
        var ingestResult = await ingest.IngestTrustedAsync(tenantId, payload, ct, enqueueDispatch: false);
        if (ingestResult == CubotRedManager.Application.Tenancy.ChatIngestResult.Unauthorized)
        {
            return Results.Unauthorized();
        }

        // 6. Resolver conversationId por (tenant, linea, telefono).
        var conversationId = await db.Conversations.IgnoreQueryFilters()
            .Where(c => c.TenantId == tenantId && c.WhatsAppLineId == line.Id && c.ContactPhone == phone)
            .OrderByDescending(c => c.LastMessageAt)
            .Select(c => (Guid?)c.Id)
            .FirstOrDefaultAsync(ct);
        if (conversationId is null)
        {
            return Results.Problem("No se pudo resolver la conversacion despues de la ingesta.");
        }

        // 7. Dispatcher sincrono: corre el agente REAL (herramientas, markers, LLM) sobre la
        //    conversacion. Puede tardar 10-60s con el LLM; el cliente HTTP puede dar timeout de
        //    gateway aunque el server termine OK — en ese caso revisar BD / /conversaciones.
        var dispatchResult = await dispatcher.DispatchForTestAsync(tenantId, conversationId.Value, line.Id, inboundText, ct);

        // 8. Respuesta: TODOS los mensajes salientes del turno (orden cronologico). Un turno real
        //    puede producir varios salientes (reaccion + texto + recursos + link resuelto). Para que
        //    el emulador sea fiel, los devolvemos todos, no solo el primero.
        var outboundsRaw = await db.Messages.IgnoreQueryFilters()
            .Where(m => m.TenantId == tenantId && m.ConversationId == conversationId.Value
                     && m.Direction == CubotRedManager.Domain.Enums.MessageDirection.Outbound
                     && m.SentAt >= now)
            .OrderBy(m => m.SentAt)
            .Select(m => new { m.Body, m.MessageType, m.SentAt })
            .ToListAsync(ct);
        var outbounds = outboundsRaw.Select(m => new {
            body = m.Body,
            messageType = m.MessageType,
            sentAt = m.SentAt
        }).ToList();

        // Validaciones post-dispatch utiles para pruebas:
        //  - linkPagoRequested: el prompt pidio generar link ([[link_pago] aparecio al menos una vez).
        //  - linkPagoResolved: encontramos una URL que el PaymentLinkProcessor sustituyo (http/https
        //    reales en algun saliente). Si fue requested pero no resolved => quedo marker crudo o
        //    fallback: ver outbounds para diagnosticar (nombre no mapeado, token expirado, catalogo).
        //  - resolvedUrls: lista de URLs reales encontradas, para que la prueba pueda hacer HEAD.
        //  - unresolvedMarkers: markers que quedaron sin procesar (bracket pairs sueltos).
        var urlRegex = new System.Text.RegularExpressions.Regex(@"https?://[^\s)\]>]+", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        var markerRegex = new System.Text.RegularExpressions.Regex(@"\[\[[^\]]+\]\]");
        var allBodies = string.Join("\n", outboundsRaw.Select(x => x.Body ?? ""));
        var linkPagoRequested = allBodies.Contains("[[link_pago", StringComparison.OrdinalIgnoreCase)
            || outboundsRaw.Any(x => (x.Body ?? "").Contains("salesLink", StringComparison.OrdinalIgnoreCase));
        var resolvedUrls = urlRegex.Matches(allBodies).Select(m => m.Value).Distinct().ToList();
        var unresolvedMarkers = markerRegex.Matches(allBodies).Select(m => m.Value).Distinct().ToList();
        var linkPagoResolved = linkPagoRequested && resolvedUrls.Count > 0;

        return Results.Ok(new
        {
            conversationId = conversationId.Value,
            lineId = line.Id,
            agentId,
            ok = dispatchResult.Ok,
            skipReason = dispatchResult.SkipReason,
            reply = outboundsRaw.Count > 0 ? outboundsRaw[^1].Body : dispatchResult.ReplyText,
            outbounds,
            resolvedUrls,
            unresolvedMarkers,
            linkPagoRequested,
            linkPagoResolved,
            simulated = dispatchResult.Simulated,
            leadsCreated = dispatchResult.LeadsCreated,
            inputTokens = dispatchResult.InputTokens,
            outputTokens = dispatchResult.OutputTokens
        });
    }
    finally { ambient.Set(null, null); }
// Mismo patron que /api/agents/{id}/sync-prices y /api/data-containers/*: AllowAnonymous a nivel
// de middleware, autenticacion interna por cookie (claim tenant_id) o por header X-Api-Token
// (helper AuthenticateApiTokenAsync). Si ninguna de las dos resuelve tenant, el handler devuelve 401.
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
// POST /webhooks/ycloud/{tenantId}: recibe mensajes entrantes. Idempotencia por wamid (via dedupe
// interno del ChatIngestService). Resuelve la linea por YCloudPhoneNumberId = payload.to y los
// persiste en el modelo canonico Conversation/Message (igual que Evolution), para que el
// AgentDispatcher responda automaticamente a mensajes YCloud. Antes se guardaba como InboxMessage
// (quedaba en bandeja operador, el agente IA no respondia); PR C migra al modelo unificado.
// Responde 200 rapido (Meta desconecta si > 5s).
app.MapPost("/webhooks/ycloud/{tenantId:guid}", async (
    Guid tenantId,
    HttpContext http,
    IServiceScopeFactory scopeFactory,
    ILoggerFactory loggerFactory,
    CubotRedManager.Web.Webhooks.WebhookPayloadBuffer webhookBuffer) =>
{
    var log = loggerFactory.CreateLogger("YCloudWebhook");

    // Capturamos el body como string ANTES de parsear para registrarlo en el buffer de diagnostico.
    // Antes el handler leia el stream directo al JsonDocument y perdiamos la forma exacta de lo que
    // YCloud mandaba cuando el parser no la reconocia (status updates, formatos distintos, etc.).
    string body;
    using (var reader = new StreamReader(http.Request.Body, System.Text.Encoding.UTF8, leaveOpen: false))
    {
        body = await reader.ReadToEndAsync();
    }
    var contentType = http.Request.ContentType;
    var userAgent = http.Request.Headers.UserAgent.ToString();
    var remoteIp = http.Connection.RemoteIpAddress?.ToString()
                   ?? http.Request.Headers["X-Forwarded-For"].ToString();

    System.Text.Json.JsonDocument doc;
    try { doc = System.Text.Json.JsonDocument.Parse(body); }
    catch (Exception ex)
    {
        log.LogWarning(ex, "YCloud webhook: JSON invalido para tenant {TenantId}", tenantId);
        webhookBuffer.Record("ycloud", tenantId.ToString(), 200, contentType, userAgent, remoteIp,
            body, 0, 0, notes: "JSON invalido: " + ex.GetType().Name);
        return Results.Ok();
    }

    List<CubotRedManager.Web.Webhooks.YCloudWebhookParser.InboundMessage> messages;
    List<CubotRedManager.Web.Webhooks.YCloudWebhookParser.InboundReaction> reactions;
    try
    {
        messages = CubotRedManager.Web.Webhooks.YCloudWebhookParser.Parse(doc);
        reactions = CubotRedManager.Web.Webhooks.YCloudWebhookParser.ParseReactions(doc);
    }
    finally { doc.Dispose(); }

    if (messages.Count == 0 && reactions.Count == 0)
    {
        webhookBuffer.Record("ycloud", tenantId.ToString(), 200, contentType, userAgent, remoteIp,
            body, 0, 0, notes: "Sin mensajes ni reacciones reconocidas");
        return Results.Ok();
    }

    // Acumular notas por mensaje/reaccion procesada para que el buffer refleje si cada uno
    // completo el flujo (lineResolved / ingested / error). Antes el Record se llamaba antes del
    // procesamiento y perdiamos esa visibilidad.
    var handlerNotes = new List<string>();

    using var scope = scopeFactory.CreateScope();
    scope.ServiceProvider.GetRequiredService<IAmbientTenantOverride>().Set(tenantId, null);
    var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
    var ingest = scope.ServiceProvider.GetRequiredService<CubotRedManager.Application.Tenancy.IChatIngestService>();

    // Reacciones primero (son UPDATE Messages.Reaction, no insertan mensaje nuevo).
    for (int ri = 0; ri < reactions.Count; ri++)
    {
        var r = reactions[ri];
        try
        {
            await ingest.ApplyReactionAsync(tenantId, r.TargetWamid, r.Emoji, http.RequestAborted);
            handlerNotes.Add($"rx[{ri}]=OK target={r.TargetWamid.Substring(0, Math.Min(16, r.TargetWamid.Length))}");
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "YCloud webhook: ApplyReaction fallo wamid={Wamid}", r.TargetWamid);
            handlerNotes.Add($"rx[{ri}]=ERROR {ex.GetType().Name}: {ex.Message}");
        }
    }

    for (int mi = 0; mi < messages.Count; mi++)
    {
        var m = messages[mi];
        // Resuelve la linea por sender (to). Si la linea no existe en el tenant o no es YCloud,
        // se descarta silenciosamente (podria ser un webhook mal configurado).
        var line = await db.WhatsAppLines.FirstOrDefaultAsync(l =>
            l.YCloudPhoneNumberId == m.ToPhone && l.Provider == CubotRedManager.Domain.Enums.WhatsAppProvider.YCloud);
        if (line is null)
        {
            log.LogInformation("YCloud webhook: linea no encontrada para tenant {TenantId}, to {To}", tenantId, m.ToPhone);
            handlerNotes.Add($"msg[{mi}]=LINE_NOT_FOUND to={m.ToPhone}");
            continue;
        }

        // Modelo canonico: persistir via IChatIngestService (crea/resuelve Conversation + Message
        // + dispatch al AgentDispatcher). Dedupe interno por ExternalMessageId. enqueueDispatch:true
        // para que el agente IA responda igual que a mensajes Evolution.
        var payload = new CubotRedManager.Application.Tenancy.IngestMessageRequest(
            ContactPhone: m.FromPhone,
            ContactName: null,
            ExternalMessageId: m.Wamid,
            Body: m.Text ?? $"(mensaje tipo {m.MessageType})",
            MessageType: m.MessageType,
            SentAt: m.ReceivedAt,
            WhatsAppLineId: line.Id);
        try
        {
            var ingestResult = await ingest.IngestTrustedAsync(tenantId, payload, http.RequestAborted, enqueueDispatch: true);
            handlerNotes.Add($"msg[{mi}]=LINE_OK line={line.InstanceName} ingest={ingestResult} from={m.FromPhone}");
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "YCloud webhook: ingest fallo tenant={TenantId} wamid={Wamid}", tenantId, m.Wamid);
            // Incluimos inner exception porque DbUpdateException deja el motivo real (unique index
            // violation, FK, NOT NULL) en el inner. Sin esto solo veiamos el mensaje generico de EF.
            var innerMsg = ex.InnerException?.Message;
            handlerNotes.Add($"msg[{mi}]=INGEST_ERROR {ex.GetType().Name}: {ex.Message}" +
                (innerMsg != null ? " || inner: " + innerMsg : ""));
            // Best-effort: no bloqueamos la respuesta 200 (Meta reintenta igual si devolvemos != 200).
        }
    }

    // Registrar al final con TODAS las notas acumuladas del procesamiento. Asi el buffer refleja
    // exactamente que paso con cada mensaje/reaccion (resolucion de linea, resultado del ingest).
    webhookBuffer.Record("ycloud", tenantId.ToString(), 200, contentType, userAgent, remoteIp,
        body, messages.Count, reactions.Count,
        notes: handlerNotes.Count > 0 ? string.Join(" | ", handlerNotes) : null);
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
    if (string.IsNullOrEmpty(expected))
    {
        log.LogWarning("Evolution webhook: master WebhookToken vacio; devolviendo 503");
        return Results.StatusCode(503);
    }

    var provided = request.Headers["x-webhook-token"].ToString();
    if (string.IsNullOrEmpty(provided)) { provided = request.Query["token"].ToString(); }
    if (!string.Equals(provided, expected, StringComparison.Ordinal))
    {
        log.LogWarning("Evolution webhook: token invalido (tenant {Tenant}, tenia_header={HasHeader})",
            tenantId, !string.IsNullOrEmpty(request.Headers["x-webhook-token"].ToString()));
        return Results.Unauthorized();
    }

    // Leemos el body a memoria para poder loggearlo (una sola lectura del stream) + parsear.
    // Cuesta poco (webhook Evolution < 32KB tipico) y nos deja el body RAW en Railway logs
    // cuando hay un caso raro (LID sin telefono, evento nuevo, etc).
    string rawBody;
    using (var reader = new StreamReader(request.Body))
    {
        rawBody = await reader.ReadToEndAsync(ct);
    }
    log.LogInformation("Evolution webhook: body {Len} bytes, snippet: {Snippet}",
        rawBody.Length, rawBody.Length > 800 ? rawBody.Substring(0, 800) + "..." : rawBody);

    System.Text.Json.JsonDocument doc;
    try { doc = System.Text.Json.JsonDocument.Parse(rawBody); }
    catch (Exception ex) { log.LogWarning(ex, "Evolution webhook: JSON invalido"); return Results.Ok(); }

    CubotRedManager.Application.Tenancy.ParsedInbound? parsed;
    CubotRedManager.Application.Tenancy.ParsedReaction? reaction;
    try
    {
        // Primero chequeo si es reaccion inbound (reactionMessage sobre un wamid de nuestro
        // mensaje OUTBOUND). Si lo es, actualizo Message.Reaction y termino.
        reaction = CubotRedManager.Application.Tenancy.EvolutionWebhookParser.TryParseReaction(doc.RootElement);
        parsed = reaction is null
            ? CubotRedManager.Application.Tenancy.EvolutionWebhookParser.Parse(doc.RootElement)
            : null;
    }
    finally { doc.Dispose(); }
    if (reaction is not null)
    {
        tenantOverride.Set(tenantId, null);
        var applied = await ingest.ApplyReactionAsync(reaction.TenantId, reaction.TargetExternalId, reaction.Emoji, ct);
        log.LogInformation("Evolution webhook: reaccion '{Emoji}' sobre wamid={Wamid} applied={Applied}",
            reaction.Emoji, reaction.TargetExternalId, applied);
        return Results.Ok(new { status = applied ? "reaction-applied" : "reaction-no-match" });
    }
    if (parsed is null)
    {
        log.LogInformation("Evolution webhook: parser devolvio null (evento no ingestable)");
        return Results.Ok(new { status = "ignored" });
    }
    log.LogInformation("Evolution webhook: parsed OK phone={Phone} lineId={Line} externalId={Ext}",
        parsed.Payload.ContactPhone, parsed.Payload.WhatsAppLineId, parsed.Payload.ExternalMessageId);

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

/// <summary>Payload JSON del POST /api/test/agent (emulador de linea WhatsApp para probar un agente IA
/// end-to-end sin salir al mundo). Todos los campos son opcionales:
///   - Text: cuerpo del mensaje "como si fuera el cliente" (default: vacio).
///   - AgentId: agente a probar; si es null, el primer activo del tenant.
///   - ContactPhone/ContactName: identificacion del "contacto" virtual (defaults razonables).</summary>
public sealed record TestAgentRequest(string? Text, Guid? AgentId, string? ContactPhone, string? ContactName);

/// <summary>Payload de POST /api/agents/{id}/pipeline-config.</summary>
public sealed record PipelineConfigRequest(bool CreateLeadsInPipeline, Guid? DefaultPipelineId);

/// <summary>Payload de POST /api/agents/{id}/system-prompt. Minimo 50 chars; requiere header
/// X-Confirm-Agent-Edit: yes.</summary>
public sealed record SystemPromptRequest(string? SystemPrompt);

/// <summary>Payload de POST /api/whatsapp-templates/{id}/send.</summary>
public sealed record TemplateSendRequest(Guid WhatsAppLineId, string? Phone, IReadOnlyList<string>? Values, string? HeaderMediaUrl);

/// <summary>Payload de POST /api/whatsapp-templates/{id}/active.</summary>
public sealed record TemplateSetActiveRequest(bool Active);
