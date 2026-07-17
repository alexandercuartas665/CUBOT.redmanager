using System.Security.Cryptography;
using CubotRedManager.Application.Abstractions;
using CubotRedManager.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CubotRedManager.Application.Tenancy;

public sealed class WebhookAdminService : IWebhookAdminService
{
    /// <summary>
    /// Puerto local que el tunel cloudflared expone hacia afuera. En redmanager el host de dev
    /// escucha en 5036 (launchSettings.json). Se puede sobreescribir con la env var
    /// <c>WEBHOOK_LOCAL_PORT</c> si el host corre en otro puerto.
    /// </summary>
    private const int DefaultAppPort = 5036;

    private readonly IApplicationDbContext _db;
    private readonly IDevTunnel _tunnel;
    private readonly IWhatsAppConnectorService _connector;
    private readonly int _appPort;

    public WebhookAdminService(IApplicationDbContext db, IDevTunnel tunnel, IWhatsAppConnectorService connector)
    {
        _db = db;
        _tunnel = tunnel;
        _connector = connector;

        var envPort = Environment.GetEnvironmentVariable("WEBHOOK_LOCAL_PORT");
        _appPort = int.TryParse(envPort, out var p) && p > 0 ? p : DefaultAppPort;
    }

    public async Task<WebhookConfigDto> GetAsync(CancellationToken cancellationToken = default)
    {
        var cfg = await GetOrCreateAsync(cancellationToken);
        return Map(cfg);
    }

    public async Task<WebhookConfigDto> SaveAsync(string mode, string? publicUrl, string? token, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var cfg = await GetOrCreateAsync(cancellationToken);
        cfg.WebhookMode = string.Equals(mode, "Production", StringComparison.OrdinalIgnoreCase) ? "Production" : "Development";
        cfg.WebhookPublicUrl = string.IsNullOrWhiteSpace(publicUrl) ? null : publicUrl.Trim().TrimEnd('/');
        if (string.IsNullOrWhiteSpace(token))
        {
            cfg.WebhookToken ??= GenerateToken();
        }
        else
        {
            cfg.WebhookToken = token.Trim();
        }
        await _db.SaveChangesAsync(cancellationToken);

        // Re-registrar el webhook en las instancias conectadas (igual que al iniciar el tunel en
        // modo desarrollo). Sin esto, cambiar a Produccion solo actualizaba la BD y Evolution
        // seguia entregando los entrantes a la URL anterior; las lineas no recibian en el nuevo
        // destino.
        await _connector.ApplyWebhookToConnectedLinesAsync(actorUserId, cancellationToken);
        return Map(cfg);
    }

    public async Task<WebhookConfigDto> StartTunnelAsync(Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var cfg = await GetOrCreateAsync(cancellationToken);
        cfg.WebhookToken ??= GenerateToken();
        cfg.WebhookMode = "Development";

        var url = await _tunnel.StartAsync(_appPort, cancellationToken);
        cfg.WebhookActiveUrl = url;
        await _db.SaveChangesAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(url))
        {
            await _connector.ApplyWebhookToConnectedLinesAsync(actorUserId, cancellationToken);
        }
        return Map(cfg);
    }

    public async Task<WebhookConfigDto> StopTunnelAsync(Guid actorUserId, CancellationToken cancellationToken = default)
    {
        _tunnel.Stop();
        var cfg = await GetOrCreateAsync(cancellationToken);
        cfg.WebhookActiveUrl = null;
        await _db.SaveChangesAsync(cancellationToken);
        return Map(cfg);
    }

    private async Task<EvolutionMasterConfig> GetOrCreateAsync(CancellationToken ct)
    {
        var cfg = await _db.EvolutionMasterConfigs.FirstOrDefaultAsync(ct);
        if (cfg is null)
        {
            cfg = new EvolutionMasterConfig { WebhookMode = "Development" };
            _db.EvolutionMasterConfigs.Add(cfg);
            await _db.SaveChangesAsync(ct);
        }
        return cfg;
    }

    private WebhookConfigDto Map(EvolutionMasterConfig c)
    {
        var effectiveBase = string.Equals(c.WebhookMode, "Production", StringComparison.OrdinalIgnoreCase)
            ? c.WebhookPublicUrl
            : c.WebhookActiveUrl;
        // La URL real por linea es /webhooks/evolution/{tenantId}. Aqui mostramos la base sin
        // el tenantId (informativo para la UI); el registrador la completa por linea.
        var effective = string.IsNullOrWhiteSpace(effectiveBase) ? null : $"{effectiveBase!.TrimEnd('/')}/webhooks/evolution/{{tenantId}}";
        return new WebhookConfigDto(c.WebhookMode, c.WebhookPublicUrl, c.WebhookToken, c.WebhookActiveUrl, _tunnel.IsRunning, effective);
    }

    private static string GenerateToken()
        => Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
}
