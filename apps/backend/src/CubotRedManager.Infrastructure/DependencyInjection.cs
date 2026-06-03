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
        var connectionString = configuration.GetConnectionString("Default");

        services.AddDbContext<CubotRedManagerDbContext>(options =>
        {
            options.UseNpgsql(connectionString);
            options.UseSnakeCaseNamingConvention();
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
        services.AddScoped<ITikTokSyncService, TikTokSyncService>();
        services.AddScoped<IPublicationExecutorService, PublicationExecutorService>();
        services.AddScoped<IAutoReplyConfigService, AutoReplyConfigService>();
        services.AddScoped<IAutomationService, AutomationService>();
        services.AddScoped<ITenantMetricsService, TenantMetricsService>();
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
        // MCP simplificado: expone DataContainers como placeholders del prompt para los agentes IA.
        services.AddScoped<IDataContainerMcpService, DataContainerMcpService>();
        services.AddScoped<IAiAgentService, AiAgentService>();
        services.AddScoped<IAiAgentCacheService, AiAgentCacheService>();
        services.AddScoped<IAiUsageService, AiUsageService>();
        services.AddScoped<IAiInferenceService, AiInferenceService>();
        services.AddHttpClient<IAiProviderClient, CubotRedManager.Infrastructure.Ai.AiProviderClient>();
        services.AddScoped<CubotRedManager.Application.Admin.IAiServerConfigService, CubotRedManager.Application.Admin.AiServerConfigService>();

        // Evolution (WhatsApp): cliente HTTP + config maestra.
        services.AddHttpClient<CubotRedManager.Application.Admin.IEvolutionApiClient, CubotRedManager.Infrastructure.Evolution.EvolutionApiClient>();
        services.AddScoped<CubotRedManager.Application.Admin.IEvolutionMasterConfigService, CubotRedManager.Application.Admin.EvolutionMasterConfigService>();
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
}
