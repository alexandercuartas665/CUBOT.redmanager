using CubotRedManager.Application.Abstractions;
using CubotRedManager.Application.Tenancy;
using CubotRedManager.Domain.Enums;
using CubotRedManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CubotRedManager.Web.BackgroundJobs;

/// <summary>
/// Worker periodico que mantiene saludables las cuentas TikTok conectadas:
///  1) Refresca proactivamente tokens que expiran en menos de RefreshIfExpiresWithin (24h por defecto).
///  2) Cada SyncEvery (30min por defecto), corre TikTokSyncService.SyncAllAsync sobre cada cuenta Connected.
/// Itera tenant-por-tenant usando IAmbientTenantOverride para que los servicios scoped puedan
/// resolver el TenantId correcto sin HTTP context. Vive aqui (proyecto Web) por simplicidad
/// hasta que migremos a proyecto Workers + MassTransit segun el plan de retoma.
/// </summary>
public sealed class TikTokMaintenanceWorker : BackgroundService
{
    private static readonly Guid SystemUserId = Guid.Empty;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<TikTokMaintenanceWorker> _logger;
    private readonly TikTokMaintenanceOptions _options;

    private DateTimeOffset _nextSyncAt = DateTimeOffset.MinValue;

    public TikTokMaintenanceWorker(IServiceScopeFactory scopeFactory, ILogger<TikTokMaintenanceWorker> logger, TikTokMaintenanceOptions options)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _options = options;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Pequeno delay inicial para que la app y la migracion terminen de inicializar.
        try { await Task.Delay(TimeSpan.FromSeconds(20), stoppingToken); }
        catch (OperationCanceledException) { return; }

        _logger.LogInformation("TikTokMaintenanceWorker arrancado. CheckInterval={ci} SyncEvery={se} RefreshWindow={rw}",
            _options.CheckInterval, _options.SyncEvery, _options.RefreshIfExpiresWithin);

        while (!stoppingToken.IsCancellationRequested)
        {
            try { await RunCycleAsync(stoppingToken); }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { _logger.LogError(ex, "Ciclo del worker TikTok fallo."); }

            try { await Task.Delay(_options.CheckInterval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task RunCycleAsync(CancellationToken ct)
    {
        // Listamos cuentas TikTok globalmente (ignorando filtro tenant; somos un worker de sistema).
        List<AccountTuple> accounts;
        using (var scope = _scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CubotRedManagerDbContext>();
            accounts = await db.SocialAccounts
                .IgnoreQueryFilters()
                .Where(a => a.NetworkCode == "tiktok"
                            && (a.Status == SocialAccountStatus.Connected || a.Status == SocialAccountStatus.Expired))
                .Select(a => new AccountTuple(a.Id, a.TenantId, a.ExpiresAt, a.Status))
                .ToListAsync(ct);
        }

        if (accounts.Count == 0) { return; }

        var now = DateTimeOffset.UtcNow;
        var dueSync = now >= _nextSyncAt;

        foreach (var account in accounts)
        {
            if (ct.IsCancellationRequested) { return; }

            // 1) Refresh proactivo: token sin fecha conocida, o que expira dentro de la ventana,
            //    o ya marcado Expired.
            var needsRefresh = account.Status == SocialAccountStatus.Expired
                            || account.ExpiresAt is null
                            || (account.ExpiresAt.Value - now) <= _options.RefreshIfExpiresWithin;

            if (needsRefresh)
            {
                await RunInTenantScopeAsync(account.TenantId, async sp =>
                {
                    var tk = sp.GetRequiredService<ITikTokConnectionService>();
                    var r = await tk.RefreshAccountAsync(account.Id, SystemUserId, ct);
                    if (r.Success) { _logger.LogInformation("Refresh OK cuenta {Id}", account.Id); }
                    else
                    {
                        _logger.LogWarning("Refresh fallido cuenta {Id}: {Err}", account.Id, r.Error);
                        try { await NotifyRefreshFailureAsync(sp, account.Id, r.Error ?? "sin detalle", ct); }
                        catch (Exception nfx) { _logger.LogWarning(nfx, "No se pudo enviar alerta WA para cuenta {Id}", account.Id); }
                    }
                });
            }

            // 2) Sync periodico (si esta en la ventana).
            if (dueSync)
            {
                await RunInTenantScopeAsync(account.TenantId, async sp =>
                {
                    var sync = sp.GetRequiredService<ITikTokSyncService>();
                    var r = await sync.SyncAllAsync(account.Id, _options.MaxVideosPerSync, SystemUserId, cancellationToken: ct);
                    if (r.Success)
                    {
                        _logger.LogInformation(
                            "Sync OK cuenta {Id}: V+{vi}/{vu} C+{ci}/{cu} R+{ri}",
                            account.Id, r.VideosInserted, r.VideosUpdated, r.CommentsInserted, r.CommentsUpdated, r.RepliesInserted);
                    }
                    else
                    {
                        _logger.LogWarning("Sync con errores cuenta {Id}: {Err}", account.Id, r.ErrorMessage);
                    }
                });
            }
        }

        if (dueSync) { _nextSyncAt = now + _options.SyncEvery; }
    }

    private async Task RunInTenantScopeAsync(Guid tenantId, Func<IServiceProvider, Task> action)
    {
        using var scope = _scopeFactory.CreateScope();
        var ambient = scope.ServiceProvider.GetRequiredService<IAmbientTenantOverride>();
        ambient.Set(tenantId, SystemUserId);
        try { await action(scope.ServiceProvider); }
        finally { ambient.Set(null, null); }
    }

    /// <summary>
    /// Envia una alerta al admin del tenant via WhatsApp (linea Evolution) informando que el
    /// refresh de la cuenta TikTok fallo. Idempotente: no re-notifica si ya se envio en las
    /// ultimas 24h (LastRefreshFailureNotifiedAt). Reset del marcador ocurre al reconectar la
    /// cuenta (ver TikTokConnectionService.ExchangeCode / RefreshAccountAsync exitoso).
    /// </summary>
    private async Task NotifyRefreshFailureAsync(IServiceProvider sp, Guid accountId, string error, CancellationToken ct)
    {
        var db = sp.GetRequiredService<CubotRedManagerDbContext>();
        var cfg = await db.TenantAlertConfigs.FirstOrDefaultAsync(ct);
        if (cfg is null || !cfg.IsActive || cfg.WhatsAppLineId is null || string.IsNullOrWhiteSpace(cfg.Target))
        {
            return;
        }

        var acc = await db.SocialAccounts.FirstOrDefaultAsync(a => a.Id == accountId, ct);
        if (acc is null) { return; }
        var now = DateTimeOffset.UtcNow;
        if (acc.LastRefreshFailureNotifiedAt is { } last && (now - last) < TimeSpan.FromHours(24))
        {
            return; // ya notificamos hace poco; anti-spam.
        }

        var handle = acc.Handle ?? acc.ExternalId ?? "(cuenta)";
        var msg = $"[CUBOT.redmanager] Refresh de token TikTok fallido para la cuenta *{handle}*.\n" +
                  $"Estado: {acc.Status}. Motivo: {error}\n" +
                  "Reconecta la cuenta manualmente desde /cuentas-sociales para evitar que el access_token expire por completo.";

        var connector = sp.GetRequiredService<CubotRedManager.Application.Tenancy.IWhatsAppConnectorService>();
        var send = await connector.SendTestAsync(cfg.WhatsAppLineId.Value, cfg.Target!, msg, SystemUserId, ct);
        if (send.Ok)
        {
            acc.LastRefreshFailureNotifiedAt = now;
            await db.SaveChangesAsync(ct);
            _logger.LogInformation("Alerta WA enviada por refresh fallido cuenta {Id}", accountId);
        }
        else
        {
            _logger.LogWarning("Alerta WA fallo cuenta {Id}: {Err}", accountId, send.Error);
        }
    }
}

internal sealed record AccountTuple(Guid Id, Guid TenantId, DateTimeOffset? ExpiresAt, SocialAccountStatus Status);

public sealed class TikTokMaintenanceOptions
{
    public bool Enabled { get; init; } = true;
    public TimeSpan CheckInterval { get; init; } = TimeSpan.FromMinutes(10);
    public TimeSpan SyncEvery { get; init; } = TimeSpan.FromMinutes(30);
    public TimeSpan RefreshIfExpiresWithin { get; init; } = TimeSpan.FromHours(24);
    // Cada 30min el worker sincroniza esta cantidad por cuenta. Con 100 videos y 20 por pagina son
    // 5 requests -> ~seg. Cubre creadores activos que publican varias veces al dia sin dejar huecos.
    public int MaxVideosPerSync { get; init; } = 100;
}
