using CubotRedManager.Application.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace CubotRedManager.Infrastructure.Persistence;

/// <summary>
/// Factory de tiempo de diseno para `dotnet ef migrations`. Construye el DbContext sin DI ni
/// tenant activo (el filtro global queda fail-closed). La cadena se puede sobreescribir con
/// la variable de entorno CUBOTRM_MIGRATIONS_CONNECTION.
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<CubotRedManagerDbContext>
{
    public CubotRedManagerDbContext CreateDbContext(string[] args)
    {
        var conn = Environment.GetEnvironmentVariable("CUBOTRM_MIGRATIONS_CONNECTION")
            ?? "Host=localhost;Port=5436;Database=cubot_redmanager_dev;Username=cubotrm;Password=dev_pg_redmanager_2026";

        var options = new DbContextOptionsBuilder<CubotRedManagerDbContext>()
            .UseNpgsql(conn)
            .UseSnakeCaseNamingConvention()
            .Options;

        return new CubotRedManagerDbContext(options, new NullTenantProvider());
    }

    private sealed class NullTenantProvider : ITenantProvider
    {
        public Guid? CurrentTenantId => null;
    }
}
