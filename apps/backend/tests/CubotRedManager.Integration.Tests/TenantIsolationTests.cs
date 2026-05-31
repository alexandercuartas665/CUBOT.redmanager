using CubotRedManager.Domain.Entities;
using CubotRedManager.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace CubotRedManager.Integration.Tests;

/// <summary>
/// Criterio de aceptacion (Hoja de Ruta seccion 5 + Plan de Pruebas 2.1):
/// - Sin tenant activo, una consulta tenant-scoped devuelve 0 filas.
/// - Con agencia A activa no se ven datos de B y viceversa.
/// </summary>
public class TenantIsolationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("cubot_redmanager_test")
        .WithUsername("test")
        .WithPassword("test")
        .Build();

    private readonly TestTenantProvider _tenantProvider = new();
    private readonly Guid _agencyA = Guid.CreateVersion7();
    private readonly Guid _agencyB = Guid.CreateVersion7();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await SeedAsync();
    }

    public async Task DisposeAsync()
    {
        await _postgres.DisposeAsync();
    }

    private CubotRedManagerDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<CubotRedManagerDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .UseSnakeCaseNamingConvention()
            .Options;
        return new CubotRedManagerDbContext(options, _tenantProvider);
    }

    private async Task SeedAsync()
    {
        // Se siembra ignorando el filtro (sin tenant activo no insertariamos cruzado).
        _tenantProvider.CurrentTenantId = null;
        await using var db = CreateContext();
        await db.Database.EnsureCreatedAsync();

        db.Tenants.Add(new Tenant { Id = _agencyA, Name = "Studio Marketing A" });
        db.Tenants.Add(new Tenant { Id = _agencyB, Name = "Studio Marketing B" });
        await db.SaveChangesAsync();

        db.Clients.Add(new Client { TenantId = _agencyA, Name = "Cafe del Norte" });
        db.Clients.Add(new Client { TenantId = _agencyA, Name = "Boutique Rosa" });
        db.Clients.Add(new Client { TenantId = _agencyB, Name = "Gym Iron" });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task SinTenantActivo_NoDevuelveFilas()
    {
        _tenantProvider.CurrentTenantId = null;
        await using var db = CreateContext();

        var count = await db.Clients.CountAsync();

        count.Should().Be(0);
    }

    [Fact]
    public async Task ConAgenciaA_SoloVeClientesDeA()
    {
        _tenantProvider.CurrentTenantId = _agencyA;
        await using var db = CreateContext();

        var clients = await db.Clients.ToListAsync();

        clients.Should().HaveCount(2);
        clients.Should().OnlyContain(c => c.TenantId == _agencyA);
    }

    [Fact]
    public async Task ConAgenciaB_NoVeClientesDeA()
    {
        _tenantProvider.CurrentTenantId = _agencyB;
        await using var db = CreateContext();

        var clients = await db.Clients.ToListAsync();

        clients.Should().ContainSingle();
        clients[0].Name.Should().Be("Gym Iron");
    }
}
