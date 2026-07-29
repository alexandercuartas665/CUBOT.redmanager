using CubotRedManager.Application.Abstractions;
using CubotRedManager.Application.Tenancy;
using CubotRedManager.Infrastructure.Persistence;
using CubotRedManager.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CubotRedManager.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// Registra el DbContext (PostgreSQL + snake_case), su abstraccion IApplicationDbContext,
    /// cifrado de secretos, auditoria y los servicios de Application portados. El ITenantProvider
    /// y el ITenantContext los registra la capa de presentacion (Web/SuperAdmin) segun sus claims.
    /// </summary>
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = NormalizeConnectionString(configuration.GetConnectionString("Default"));

        services.AddDbContext<CubotRedManagerDbContext>(options =>
        {
            options.UseNpgsql(connectionString);
            options.UseSnakeCaseNamingConvention();
            // Silencia el "PendingModelChangesWarning" de EF Core que detecta divergencias
            // no-materiales entre el ModelSnapshot y el modelo actual (ej. metadata generada
            // automaticamente por EF que no queda en la migracion manual). No afecta al
            // aplicar migraciones ni al schema real - la ultima migracion aplicada es la
            // fuente de verdad.
            options.ConfigureWarnings(w => w.Ignore(
                Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning));
        });

        services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<CubotRedManagerDbContext>());

        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<ISecretProtector, DataProtectionSecretProtector>();
        services.AddScoped<IAuditWriter, AuditWriter>();

        // Servicios de Application portados.
        services.AddScoped<IWhatsAppLineService, WhatsAppLineService>();
        services.AddScoped<IWhatsAppConnectorService, WhatsAppConnectorService>();
        services.AddScoped<IClientService, ClientService>();
        services.AddScoped<ISocialAccountService, SocialAccountService>();
        services.AddScoped<ITikTokConnectionService, TikTokConnectionService>();
        services.AddScoped<ITokenRefreshLogService, TokenRefreshLogService>();
        services.AddScoped<ITikTokSyncService, TikTokSyncService>();
        services.AddScoped<IPublicationExecutorService, PublicationExecutorService>();
        services.AddScoped<IAutoReplyConfigService, AutoReplyConfigService>();
        services.AddScoped<IAutomationService, AutomationService>();
        services.AddScoped<ITenantMetricsService, TenantMetricsService>();
        services.AddScoped<ITenantAlertService, TenantAlertService>();
        services.AddScoped<IBlockedNumberService, BlockedNumberService>();
        services.AddScoped<IAgentRunLogService, AgentRunLogService>();
        services.AddScoped<IChatIngestService, ChatIngestService>();
        // Chat broadcaster (NoOp por defecto; el host puede reemplazar por SignalR).
        services.AddScoped<IChatBroadcaster, NoOpChatBroadcaster>();
        // Media reader real: lee el binario del recurso desde ai_agent_resources.file_content
        // (URLs /api/agent-resources/{id}/file), descarga URLs http(s) externas (imagenes de
        // catalogos FUXION, etc), y cae al filesystem local para URLs /uploads/*. Registrado con
        // AddHttpClient para tener HttpClient inyectado con soporte de retry/logging estandar.
        services.AddHttpClient<CubotRedManager.Application.Common.IAgentMediaReader,
            CubotRedManager.Infrastructure.Persistence.DbAgentMediaReader>();
        // Procesadores de markers del dispatcher.
        services.AddScoped<ILeadMarkerProcessor, LeadMarkerProcessor>();
        services.AddScoped<IPedidoMarkerProcessor, PedidoMarkerProcessor>();
        services.AddScoped<IPaymentLinkProcessor, PaymentLinkProcessor>();
        services.AddScoped<IPriceSyncService, PriceSyncService>();
        services.AddScoped<IProductLookupService, ProductLookupService>();
        // Cliente HTTP tipado para el API de app-aware.fuxion.com. La configuracion por agente
        // (token, userId, path) vive en AiAgent.Payment* y se pasa por request.
        services.AddHttpClient<CubotRedManager.Application.Common.IFuxionPaymentClient,
            CubotRedManager.Infrastructure.Fuxion.FuxionPaymentClient>();
        // Agent dispatcher (motor de respuesta del agente IA).
        services.AddScoped<IAgentDispatcher, AgentDispatcher>();
        // La cola real (BackgroundService) se registra en el host (Web/Program.cs). Aqui dejamos
        // el NoOp como fallback para procesos sin worker.
        services.AddSingleton<IAgentDispatchQueue, NoOpAgentDispatchQueue>();
        // Webhook Evolution: config + tunel dev. IDevTunnel real (cloudflared) se registra en el
        // Web host solo si esta instalado; por defecto usamos el NoOp para no romper prod.
        services.AddSingleton<IDevTunnel, NoOpDevTunnel>();
        services.AddScoped<IWebhookAdminService, WebhookAdminService>();
        // Proveedores OAuth de redes sociales (Modulo 2.2). TikTok via HttpClient.
        services.AddHttpClient<ISocialOAuthProvider, CubotRedManager.Infrastructure.Social.TikTokOAuthProvider>();
        // Cliente HTTP de datos TikTok (Modulo 2.4 Sync).
        services.AddHttpClient<ITikTokApiClient, CubotRedManager.Infrastructure.Social.TikTokApiClient>();
        services.AddScoped<ITaskBoardService, TaskBoardService>();
        services.AddScoped<ITaskCardService, TaskCardService>();
        services.AddScoped<ITenantUserService, TenantUserService>();
        services.AddScoped<IPublicationService, PublicationService>();
        services.AddScoped<IInboxService, InboxService>();
        services.AddScoped<IMessageTemplateService, MessageTemplateService>();
        services.AddScoped<IDataContainerService, DataContainerService>();
        services.AddScoped<IApiTokenService, ApiTokenService>();
        // MCP simplificado: expone DataContainers como placeholders del prompt para los agentes IA.
        services.AddScoped<IDataContainerMcpService, DataContainerMcpService>();
        services.AddScoped<IAiAgentService, AiAgentService>();
        services.AddScoped<IAiAgentLineBindingService, AiAgentLineBindingService>();
        services.AddScoped<IAiAgentCacheService, AiAgentCacheService>();
        services.AddScoped<IAiUsageService, AiUsageService>();
        services.AddScoped<IAiInferenceService, AiInferenceService>();
        services.AddHttpClient<IAiProviderClient, CubotRedManager.Infrastructure.Ai.AiProviderClient>();
        services.AddScoped<CubotRedManager.Application.Admin.IAiServerConfigService, CubotRedManager.Application.Admin.AiServerConfigService>();

        // Evolution (WhatsApp): cliente HTTP + config maestra.
        services.AddHttpClient<CubotRedManager.Application.Admin.IEvolutionApiClient, CubotRedManager.Infrastructure.Evolution.EvolutionApiClient>();
        services.AddScoped<CubotRedManager.Application.Admin.IEvolutionMasterConfigService, CubotRedManager.Application.Admin.EvolutionMasterConfigService>();

        // YCloud (BSP oficial de WhatsApp): cliente HTTP + servicio HSM. Timeout 25s.
        services.AddHttpClient<IYCloudApiClient, CubotRedManager.Infrastructure.YCloud.YCloudApiClient>(c =>
        {
            c.Timeout = TimeSpan.FromSeconds(25);
        });
        services.AddScoped<IWhatsAppTemplateService, WhatsAppTemplateService>();
        services.AddScoped<CubotRedManager.Application.Admin.IPlanAdminService, CubotRedManager.Application.Admin.PlanAdminService>();
        services.AddScoped<CubotRedManager.Application.Admin.ITenantAdminService, CubotRedManager.Application.Admin.TenantAdminService>();
        services.AddScoped<CubotRedManager.Application.Admin.IWompiConfigService, CubotRedManager.Application.Admin.WompiConfigService>();
        services.AddScoped<CubotRedManager.Application.Admin.IPaymentAdminService, CubotRedManager.Application.Admin.PaymentAdminService>();
        services.AddScoped<CubotRedManager.Application.Admin.IAuditAdminService, CubotRedManager.Application.Admin.AuditAdminService>();

        // "Mi cuenta" (Cuenta.razor): suscripciones autoservicio, checkout Wompi, debito automatico,
        // cambio de clave y API de integracion publica (stub).
        services.AddScoped<CubotRedManager.Application.Admin.ISubscriptionAdminService, CubotRedManager.Application.Admin.SubscriptionAdminService>();
        services.AddScoped<CubotRedManager.Application.Admin.IWompiCheckoutService, CubotRedManager.Application.Admin.WompiCheckoutService>();
        services.AddScoped<CubotRedManager.Application.Admin.IRecurringBillingService, CubotRedManager.Application.Admin.RecurringBillingService>();
        // Cliente HTTP de Wompi: por ahora un stub (sin Infrastructure/Wompi); cuando se porte el
        // cliente real (travels lo tiene en CubotTravels.Infrastructure.Wompi.WompiApiClient),
        // cambiar este registro por AddHttpClient<IWompiApiClient, WompiApiClient>().
        services.AddScoped<CubotRedManager.Application.Admin.IWompiApiClient, CubotRedManager.Application.Admin.StubWompiApiClient>();
        // Auth unificado portado verbatim desde travels (login real para SuperAdmin + Tenant).
        services.AddSingleton<CubotRedManager.Application.Common.Auth.IPasswordHasher, CubotRedManager.Infrastructure.Auth.Pbkdf2PasswordHasher>();
        services.AddSingleton<CubotRedManager.Application.Common.Auth.IJwtTokenService, CubotRedManager.Infrastructure.Auth.JwtTokenService>();
        services.AddHttpClient<CubotRedManager.Application.Auth.IGoogleOAuthClient, CubotRedManager.Infrastructure.Auth.GoogleOAuthClient>();
        services.AddScoped<CubotRedManager.Application.Auth.IAuthService, CubotRedManager.Application.Auth.AuthService>();
        services.AddScoped<CubotRedManager.Application.Auth.IGoogleSignInService, CubotRedManager.Application.Auth.GoogleSignInService>();
        services.AddScoped<CubotRedManager.Application.Auth.IPasswordResetService, CubotRedManager.Application.Auth.PasswordResetService>();
        services.AddScoped<CubotRedManager.Application.Admin.IPlatformBrandingService, CubotRedManager.Application.Admin.PlatformBrandingService>();
        services.AddScoped<CubotRedManager.Application.Admin.IGoogleAuthConfigService, CubotRedManager.Application.Admin.GoogleAuthConfigService>();

        services.AddScoped<CubotRedManager.Application.Tenancy.ITenantApiService, CubotRedManager.Application.Tenancy.TenantApiService>();

        return services;
    }

    /// <summary>
    /// Acepta la connection string en formato Npgsql (dev) o en formato URI de Railway/Heroku
    /// (postgres://user:pass@host:port/db). Npgsql no parsea el URI directamente, asi que se
    /// convierte aqui al arranque; el usuario solo referencia ${{Postgres.DATABASE_URL}}.
    /// </summary>
    internal static string? NormalizeConnectionString(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) { return raw; }
        if (!raw.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase)
            && !raw.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
        {
            return raw;
        }

        var uri = new Uri(raw);
        var userInfo = uri.UserInfo.Split(':', 2);
        var user = Uri.UnescapeDataString(userInfo[0]);
        var pass = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : string.Empty;
        var db = uri.AbsolutePath.TrimStart('/');
        var port = uri.Port > 0 ? uri.Port : 5432;

        // SSL Mode=Require: Railway expone Postgres con TLS; Trust Server Certificate evita
        // fallos por CA interna. En red privada de Railway tambien funciona.
        return $"Host={uri.Host};Port={port};Database={db};Username={user};Password={pass};SSL Mode=Require;Trust Server Certificate=true";
    }
}
