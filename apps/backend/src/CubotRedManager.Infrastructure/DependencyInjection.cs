using CubotRedManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CubotRedManager.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// Registra el DbContext con PostgreSQL + snake_case. El ITenantProvider lo registra
    /// la capa de presentacion (Api/Web) segun su fuente de claims.
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

        return services;
    }
}
