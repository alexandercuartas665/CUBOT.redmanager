using System.Text.RegularExpressions;
using CubotRedManager.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace CubotRedManager.Application.Tenancy;

public sealed record PedidoMarkerResult(string CleanText, int NotificationsSent, int NotificationsFailed);

/// <summary>
/// Detecta los marcadores [[pedido: texto]], los retira del mensaje al cliente y envia a los
/// destinos de notificacion configurados. Portado 1:1 desde CUBOT.travels.
/// </summary>
public interface IPedidoMarkerProcessor
{
    Task<PedidoMarkerResult> ProcessAsync(Guid tenantId, Guid agentId, string rawText, string? footer = null, CancellationToken cancellationToken = default);
    Task<(int Sent, int Failed)> NotifyTargetsAsync(Guid tenantId, Guid agentId, string body, string? footer = null, CancellationToken cancellationToken = default);
    string StripMarkers(string raw);
}

public sealed class PedidoMarkerProcessor : IPedidoMarkerProcessor
{
    private static readonly Regex MarkerRegex = new(
        @"\[\[\s*pedido\s*:\s*(?<body>.+?)\s*\]\]",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    private readonly IApplicationDbContext _db;
    private readonly IWhatsAppConnectorService _connector;

    public PedidoMarkerProcessor(IApplicationDbContext db, IWhatsAppConnectorService connector)
    {
        _db = db;
        _connector = connector;
    }

    public async Task<PedidoMarkerResult> ProcessAsync(Guid tenantId, Guid agentId, string rawText, string? footer = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(rawText)) { return new PedidoMarkerResult(rawText, 0, 0); }
        var matches = MarkerRegex.Matches(rawText);
        if (matches.Count == 0) { return new PedidoMarkerResult(rawText, 0, 0); }

        var targets = await _db.AiAgentNotificationTargets
            .IgnoreQueryFilters()
            .Where(t => t.TenantId == tenantId && t.AgentId == agentId)
            .OrderBy(t => t.SortOrder)
            .Select(t => new { t.FromWhatsAppLineId, t.TargetKind, t.TargetValue })
            .ToListAsync(cancellationToken);

        int sent = 0, failed = 0;
        if (targets.Count > 0)
        {
            foreach (Match m in matches)
            {
                var body = m.Groups["body"].Value.Trim().Trim('"');
                if (!string.IsNullOrWhiteSpace(footer)) { body = body + "\n\n" + footer; }
                foreach (var t in targets)
                {
                    try
                    {
                        var result = await _connector.SendTestAsync(t.FromWhatsAppLineId, t.TargetValue, body, Guid.Empty, cancellationToken);
                        if (result.Ok) { sent++; } else { failed++; }
                    }
                    catch { failed++; }
                }
            }
        }
        return new PedidoMarkerResult(StripMarkers(rawText), sent, failed);
    }

    public async Task<(int Sent, int Failed)> NotifyTargetsAsync(Guid tenantId, Guid agentId, string body, string? footer = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(body)) { return (0, 0); }
        if (!string.IsNullOrWhiteSpace(footer)) { body = body + "\n\n" + footer; }
        var targets = await _db.AiAgentNotificationTargets
            .IgnoreQueryFilters()
            .Where(t => t.TenantId == tenantId && t.AgentId == agentId)
            .OrderBy(t => t.SortOrder)
            .Select(t => new { t.FromWhatsAppLineId, t.TargetValue })
            .ToListAsync(cancellationToken);
        if (targets.Count == 0) { return (0, 0); }

        int sent = 0, failed = 0;
        foreach (var t in targets)
        {
            try
            {
                var result = await _connector.SendTestAsync(t.FromWhatsAppLineId, t.TargetValue, body, Guid.Empty, cancellationToken);
                if (result.Ok) { sent++; } else { failed++; }
            }
            catch { failed++; }
        }
        return (sent, failed);
    }

    public string StripMarkers(string raw)
    {
        if (string.IsNullOrEmpty(raw)) { return raw; }
        var clean = MarkerRegex.Replace(raw, string.Empty);
        clean = Regex.Replace(clean, @"[ \t]+\n", "\n");
        return clean.Trim();
    }
}
