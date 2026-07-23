using CubotRedManager.Application.Abstractions;
using CubotRedManager.Application.Common;
using CubotRedManager.Application.Tenancy;
using CubotRedManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CubotRedManager.Web.BackgroundJobs;

/// <summary>
/// Vigila el estado del token FUXION de cada agente con Payments habilitados. Cada
/// <see cref="FuxionPaymentMaintenanceOptions.CheckInterval"/> (4h por defecto):
///   1. Descifra el token del agente.
///   2. Llama POST /api/auth/user/verify-session.
///   3. Marca PaymentTokenLastVerifiedAt=NOW si OK.
///   4. Si el token expira en menos de <see cref="FuxionPaymentMaintenanceOptions.NotifyThreshold"/>
///      (24h por defecto) o fue rechazado por FUXION, notifica al operador via TenantAlertConfig
///      (misma via que las alertas de refresh TikTok). Dedupe con
///      PaymentTokenExpiryNotifiedAt: no re-notifica en las siguientes 24h.
///
/// Errores de red / 5xx NO cambian el estado del token (Unreachable -> log y sigue).
/// </summary>
public sealed class FuxionPaymentMaintenanceWorker : BackgroundService
{
    // Usuario "sistema" para la auditoria de mensajes enviados. Mismo Guid que TikTokMaintenanceWorker.
    private static readonly Guid SystemUserId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<FuxionPaymentMaintenanceWorker> _logger;
    private readonly FuxionPaymentMaintenanceOptions _options;
    private readonly TimeProvider _time;

    public FuxionPaymentMaintenanceWorker(
        IServiceScopeFactory scopeFactory,
        ILogger<FuxionPaymentMaintenanceWorker> logger,
        FuxionPaymentMaintenanceOptions options,
        TimeProvider time)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _options = options;
        _time = time;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("FuxionPaymentMaintenance: deshabilitado por config.");
            return;
        }
        _logger.LogInformation("FuxionPaymentMaintenance: arranca. Check cada {Interval}. Umbral de aviso {Notify}.",
            _options.CheckInterval, _options.NotifyThreshold);

        // Delay inicial para dejar que el arranque de la app termine.
        try { await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try { await TickAsync(stoppingToken); }
            catch (Exception ex) { _logger.LogError(ex, "FuxionPaymentMaintenance: error en tick."); }

            try { await Task.Delay(_options.CheckInterval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task TickAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CubotRedManagerDbContext>();
        var client = scope.ServiceProvider.GetRequiredService<IFuxionPaymentClient>();
        var protector = scope.ServiceProvider.GetRequiredService<ISecretProtector>();
        var ambient = scope.ServiceProvider.GetRequiredService<IAmbientTenantOverride>();

        // Solo agentes con Payment habilitado, con token guardado y activos. IgnoreQueryFilters
        // porque somos un job del sistema: barremos todos los tenants.
        var candidates = await db.AiAgents.IgnoreQueryFilters()
            .Where(a => a.PaymentEnabled && a.PaymentTokenEncrypted != null && a.PaymentUserId != null)
            .Select(a => new AgentPaymentSlim(
                a.Id, a.TenantId, a.Name, a.PaymentTokenEncrypted!,
                a.PaymentApiBaseUrl, a.PaymentTokenExpiresAt,
                a.PaymentTokenExpiryNotifiedAt))
            .ToListAsync(ct);

        if (candidates.Count == 0) { return; }
        _logger.LogInformation("FuxionPaymentMaintenance: revisando {Count} agentes.", candidates.Count);

        foreach (var c in candidates)
        {
            if (ct.IsCancellationRequested) { break; }

            // Ambient tenant override para que las escrituras via IApplicationDbContext respeten el
            // filtro por tenant del agente actual.
            ambient.Set(c.TenantId, null);
            try
            {
                await CheckOneAsync(db, client, protector, c, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "FuxionPaymentMaintenance: fallo revisando agente {AgentId}", c.AgentId);
            }
            finally { ambient.Set(null, null); }
        }
    }

    private async Task CheckOneAsync(CubotRedManagerDbContext db, IFuxionPaymentClient client, ISecretProtector protector, AgentPaymentSlim c, CancellationToken ct)
    {
        string? token;
        try { token = protector.Unprotect(c.TokenEncrypted); }
        catch { token = null; }
        if (string.IsNullOrEmpty(token))
        {
            // Token corrupto (llave DataProtection rotada, etc). No podemos verificarlo -> tratar
            // como rechazado.
            await NotifyAsync(db, c, "Token FUXION en la BD no se puede descifrar (llave rotada?). Renuevalo en /agentes -> Pagos FUXION.", ct);
            return;
        }

        var baseUrl = string.IsNullOrWhiteSpace(c.ApiBaseUrl) ? "https://api-aware.fuxion.com" : c.ApiBaseUrl;
        var result = await client.VerifySessionAsync(baseUrl, token, ct);
        var now = _time.GetUtcNow();

        switch (result.Outcome)
        {
            case FuxionVerifySessionOutcome.Valid:
                // Actualizar timestamp de verificacion. Si esta por expirar dentro del umbral, avisar.
                await db.AiAgents.IgnoreQueryFilters().Where(a => a.Id == c.AgentId)
                    .ExecuteUpdateAsync(s => s.SetProperty(a => a.PaymentTokenLastVerifiedAt, (DateTimeOffset?)now), ct);
                if (c.TokenExpiresAt is DateTimeOffset exp && (exp - now) < _options.NotifyThreshold && exp > now)
                {
                    var hours = Math.Max(1, (int)(exp - now).TotalHours);
                    await NotifyAsync(db, c, $"Token FUXION del agente {c.Name} expira en {hours}h. Renuevalo en /agentes -> Pagos FUXION antes de que caduque.", ct);
                }
                break;

            case FuxionVerifySessionOutcome.Rejected:
                await NotifyAsync(db, c, $"Token FUXION del agente {c.Name} fue rechazado por FUXION (HTTP {result.HttpStatus}). Renuevalo en /agentes -> Pagos FUXION para que el agente pueda seguir generando links.", ct);
                break;

            case FuxionVerifySessionOutcome.Unreachable:
                // Red / 5xx / timeout: no sabemos si el token es valido. NO cambiar estado y NO
                // avisar (no queremos falsos positivos).
                _logger.LogInformation("FuxionPaymentMaintenance: verify-session no alcanzable agente {AgentId}: {Detail}",
                    c.AgentId, result.ErrorDetail);
                break;
        }
    }

    /// <summary>Envia mensaje al destinatario configurado en TenantAlertConfig (misma via que
    /// las alertas de TikTok). Idempotente: no re-notifica si ya avisamos en las ultimas 24h.</summary>
    private async Task NotifyAsync(CubotRedManagerDbContext db, AgentPaymentSlim c, string message, CancellationToken ct)
    {
        var now = _time.GetUtcNow();
        if (c.NotifiedAt is { } last && (now - last) < TimeSpan.FromHours(24))
        {
            _logger.LogInformation("FuxionPaymentMaintenance: skip notify agente {AgentId} (ultima alerta hace {Age})", c.AgentId, now - last);
            return;
        }

        var cfg = await db.TenantAlertConfigs.IgnoreQueryFilters()
            .Where(x => x.TenantId == c.TenantId).FirstOrDefaultAsync(ct);
        if (cfg is null || !cfg.IsActive || cfg.WhatsAppLineId is null || string.IsNullOrWhiteSpace(cfg.Target))
        {
            _logger.LogInformation("FuxionPaymentMaintenance: agente {AgentId} necesita alerta pero el tenant no tiene TenantAlertConfig activo.", c.AgentId);
            // Igual marcamos como notificado para no reintentarlo cada tick (24h de silencio).
            await db.AiAgents.IgnoreQueryFilters().Where(a => a.Id == c.AgentId)
                .ExecuteUpdateAsync(s => s.SetProperty(a => a.PaymentTokenExpiryNotifiedAt, (DateTimeOffset?)now), ct);
            return;
        }

        // Usamos IServiceScopeFactory alcance actual para resolver el connector.
        using var scope = _scopeFactory.CreateScope();
        var connector = scope.ServiceProvider.GetRequiredService<CubotRedManager.Application.Tenancy.IWhatsAppConnectorService>();
        var send = await connector.SendTestAsync(cfg.WhatsAppLineId.Value, cfg.Target!, "[CUBOT.redmanager] " + message, SystemUserId, ct);
        if (send.Ok)
        {
            await db.AiAgents.IgnoreQueryFilters().Where(a => a.Id == c.AgentId)
                .ExecuteUpdateAsync(s => s.SetProperty(a => a.PaymentTokenExpiryNotifiedAt, (DateTimeOffset?)now), ct);
            _logger.LogInformation("FuxionPaymentMaintenance: alerta enviada al operador del agente {AgentId}", c.AgentId);
        }
        else
        {
            _logger.LogWarning("FuxionPaymentMaintenance: no se pudo enviar alerta agente {AgentId}: {Err}", c.AgentId, send.Error);
        }
    }

    private sealed record AgentPaymentSlim(
        Guid AgentId, Guid TenantId, string Name, string TokenEncrypted,
        string? ApiBaseUrl, DateTimeOffset? TokenExpiresAt, DateTimeOffset? NotifiedAt);
}

public sealed class FuxionPaymentMaintenanceOptions
{
    public bool Enabled { get; init; } = true;
    public TimeSpan CheckInterval { get; init; } = TimeSpan.FromHours(4);
    public TimeSpan NotifyThreshold { get; init; } = TimeSpan.FromHours(24);
}
