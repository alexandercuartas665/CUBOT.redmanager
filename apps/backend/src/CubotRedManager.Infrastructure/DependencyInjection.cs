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
        services.AddScoped<IAuditWriter, NoOpAuditWriter>();

        // Servicios de Application portados.
        services.AddScoped<IWhatsAppLineService, WhatsAppLineService>();
        services.AddScoped<IAiAgentService, AiAgentService>();
        services.AddScoped<CubotRedManager.Application.Admin.IAiServerConfigService, CubotRedManager.Application.Admin.AiServerConfigService>();

        // Evolution (WhatsApp): cliente HTTP + config maestra.
        services.AddHttpClient<CubotRedManager.Application.Admin.IEvolutionApiClient, CubotRedManager.Infrastructure.Evolution.EvolutionApiClient>();
        services.AddScoped<CubotRedManager.Application.Admin.IEvolutionMasterConfigService, CubotRedManager.Application.Admin.EvolutionMasterConfigService>();
        services.AddScoped<CubotRedManager.Application.Admin.IPlanAdminService, CubotRedManager.Application.Admin.PlanAdminService>();
        services.AddScoped<CubotRedManager.Application.Admin.ITenantAdminService, CubotRedManager.Application.Admin.TenantAdminService>();

        return services;
    }
}
