using CubotRedManager.Application.Abstractions;
using CubotRedManager.Application.Tenancy;
using CubotRedManager.Domain.Enums;
using CubotRedManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CubotRedManager.Web.BackgroundJobs;

/// <summary>
/// Worker de disparo automatico del Calendario editorial. Cada CheckInterval consulta
/// publicaciones Status=Scheduled con ScheduledAt <= NOW y las ejecuta via
/// IPublicationExecutorService.ExecuteAsync (mismo path que el boton "Publicar" manual).
///
/// Sin este worker una publicacion programada para "manana 10am" se queda como Scheduled hasta
/// que un humano abra /calendario y haga clic. El disparo es "mejor tarde que nunca": si el
/// container estuvo caido, todo lo vencido se ejecuta en el proximo ciclo (sin ventana de
/// tolerancia). Los targets fallidos quedan como Failed con FailureReason y el operador puede
/// reintentar desde la UI.
///
/// Convive con TikTokMaintenanceWorker: cada uno tiene su ciclo independiente.
/// </summary>
public sealed class PublicationScheduleWorker : BackgroundService
{
    private static readonly Guid SystemUserId = Guid.Empty;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PublicationScheduleWorker> _logger;
    private readonly PublicationScheduleOptions _options;

    public PublicationScheduleWorker(IServiceScopeFactory scopeFactory, ILogger<PublicationScheduleWorker> logger, PublicationScheduleOptions options)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _options = options;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Delay inicial para dar espacio a que la app y las migraciones terminen.
        try { await Task.Delay(TimeSpan.FromSeconds(25), stoppingToken); }
        catch (OperationCanceledException) { return; }

        _logger.LogInformation("PublicationScheduleWorker arrancado. CheckInterval={ci}", _options.CheckInterval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try { await RunCycleAsync(stoppingToken); }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { _logger.LogError(ex, "Ciclo del PublicationScheduleWorker fallo."); }

            try { await Task.Delay(_options.CheckInterval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task RunCycleAsync(CancellationToken ct)
    {
        // Consulta cross-tenant usando IgnoreQueryFilters (somos worker de sistema).
        var now = DateTimeOffset.UtcNow;
        List<DuePublication> due;
        using (var scope = _scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CubotRedManagerDbContext>();
            due = await db.Publications
                .IgnoreQueryFilters()
                .Where(p => p.Status == PublicationStatus.Scheduled && p.ScheduledAt != null && p.ScheduledAt <= now)
                .OrderBy(p => p.ScheduledAt)
                .Take(_options.MaxPerCycle)
                .Select(p => new DuePublication(p.Id, p.TenantId, p.ScheduledAt!.Value))
                .ToListAsync(ct);
        }

        if (due.Count == 0) { return; }
        _logger.LogInformation("Ciclo: {Count} publicaciones vencidas listas para disparar", due.Count);

        foreach (var pub in due)
        {
            if (ct.IsCancellationRequested) { return; }
            var scheduledAgo = now - pub.ScheduledAt;
            _logger.LogInformation("Disparando publicacion {Id} (tenant={Tenant}, programada hace {Ago})",
                pub.Id, pub.TenantId, scheduledAgo);
            try
            {
                await RunInTenantScopeAsync(pub.TenantId, async sp =>
                {
                    var exec = sp.GetRequiredService<IPublicationExecutorService>();
                    var result = await exec.ExecuteAsync(pub.Id, SystemUserId, ct);
                    if (result.Success)
                    {
                        _logger.LogInformation("Publicacion {Id} ejecutada OK (estado final {S})", pub.Id, result.FinalStatus);
                    }
                    else
                    {
                        _logger.LogWarning("Publicacion {Id} fallida (estado final {S})", pub.Id, result.FinalStatus);
                    }
                });
            }
            catch (Exception ex)
            {
                // El ExecuteAsync ya persiste Status=Failed con FailureReason; este log es solo por si
                // el throw es antes de eso (bug en el service, conexion caida, etc).
                _logger.LogError(ex, "Excepcion al ejecutar publicacion {Id}", pub.Id);
            }
        }
    }

    private async Task RunInTenantScopeAsync(Guid tenantId, Func<IServiceProvider, Task> action)
    {
        using var scope = _scopeFactory.CreateScope();
        var ambient = scope.ServiceProvider.GetRequiredService<IAmbientTenantOverride>();
        ambient.Set(tenantId, SystemUserId);
        try { await action(scope.ServiceProvider); }
        finally { ambient.Set(null, null); }
    }

    private sealed record DuePublication(Guid Id, Guid TenantId, DateTimeOffset ScheduledAt);
}

public sealed class PublicationScheduleOptions
{
    /// <summary>Frecuencia del ciclo. Default 1 minuto = precision ~30s en disparo.</summary>
    public TimeSpan CheckInterval { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>Cap por ciclo para evitar un unico ciclo largo si se acumularon muchas vencidas
    /// (ej. tras un downtime). Las restantes se ejecutan en el siguiente ciclo.</summary>
    public int MaxPerCycle { get; set; } = 20;

    /// <summary>Feature flag: en tests o entornos donde no queremos disparo automatico.</summary>
    public bool Enabled { get; set; } = true;
}
