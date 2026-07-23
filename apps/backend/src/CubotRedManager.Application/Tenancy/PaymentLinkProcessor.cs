using System.Text;
using System.Text.RegularExpressions;
using CubotRedManager.Application.Abstractions;
using CubotRedManager.Application.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CubotRedManager.Application.Tenancy;

public sealed class PaymentLinkProcessor : IPaymentLinkProcessor
{
    // [[link_pago: NOMBRE:qty, NOMBRE:qty, ...]]  |  [[link_pago]]  (=fallback, sin items)
    // Case-insensitive en el nombre del marker. Contenido opcional; si va, todo hasta ]] es args.
    private static readonly Regex MarkerRegex = new(
        @"\[\[\s*link_pago\s*(?::\s*(?<args>[^\]]*?))?\s*\]\]",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private const string FallbackMessage = "un asesor te contactara en un momento para completar tu pago";

    private readonly IApplicationDbContext _db;
    private readonly IFuxionPaymentClient _fuxion;
    private readonly IAiAgentService _agentService;
    private readonly IDataContainerService _containers;
    private readonly ILogger<PaymentLinkProcessor> _logger;

    public PaymentLinkProcessor(
        IApplicationDbContext db,
        IFuxionPaymentClient fuxion,
        IAiAgentService agentService,
        IDataContainerService containers,
        ILogger<PaymentLinkProcessor> logger)
    {
        _db = db;
        _fuxion = fuxion;
        _agentService = agentService;
        _containers = containers;
        _logger = logger;
    }

    public async Task<PaymentLinkResult> ProcessAsync(Guid tenantId, Guid agentId, string agentText, CancellationToken cancellationToken = default)
    {
        var matches = MarkerRegex.Matches(agentText ?? string.Empty);
        if (matches.Count == 0)
        {
            return new PaymentLinkResult(agentText ?? string.Empty, 0, 0, 0, Array.Empty<string>());
        }

        // Cargar config del agente (una sola vez para todos los markers). No hace falta
        // IgnoreQueryFilters aqui: el dispatcher ya seteo el ambient tenant override antes de
        // llamarnos, asi que el HasQueryFilter matchea.
        var cfg = await _db.AiAgents.AsNoTracking()
            .Where(a => a.Id == agentId)
            .Select(a => new AgentPaymentSnapshot(
                a.PaymentEnabled, a.PaymentUserId, a.PaymentCountry,
                a.PaymentCatalogContainerName, a.PaymentCatalogNameColumn, a.PaymentCatalogProductIdColumn,
                a.PaymentApiBaseUrl, a.PaymentApiPathTemplate, a.PaymentResponseUrlPath))
            .FirstOrDefaultAsync(cancellationToken);

        if (cfg is null || !cfg.PaymentEnabled)
        {
            // Feature apagada: sustituye markers por fallback para no exponer sintaxis al cliente.
            _logger.LogInformation("PaymentLink: markers detectados pero feature desactivada en agente {AgentId}", agentId);
            return SubstituteAllWithFallback(agentText!, matches, "PaymentEnabled=false");
        }

        // Diccionario nombre -> productId (case-insensitive). Cargado una vez por invocacion.
        Dictionary<string, string> catalog;
        try
        {
            catalog = await LoadCatalogAsync(cfg, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "PaymentLink: fallo al cargar catalogo del agente {AgentId}", agentId);
            return SubstituteAllWithFallback(agentText!, matches, $"catalogo no accesible: {ex.Message}");
        }

        var token = await _agentService.GetDecryptedPaymentTokenAsync(agentId, cancellationToken);
        var errors = new List<string>();
        int generated = 0, failed = 0;
        var sb = new StringBuilder();
        int cursor = 0;

        foreach (Match match in matches)
        {
            sb.Append(agentText!, cursor, match.Index - cursor);
            cursor = match.Index + match.Length;

            var args = match.Groups["args"].Success ? match.Groups["args"].Value : "";
            var items = ParseItems(args, catalog, out var unresolved);
            if (unresolved.Count > 0)
            {
                errors.Add($"productos no encontrados en catalogo '{cfg.PaymentCatalogContainerName}': {string.Join(", ", unresolved)}");
            }
            if (items.Count == 0)
            {
                sb.Append(FallbackMessage);
                failed++;
                continue;
            }

            var description = $"WA {DateTime.UtcNow:yyyyMMdd-HHmmss}";
            var req = new FuxionGenerateLinkRequest(
                BaseUrl: cfg.PaymentApiBaseUrl ?? "https://api-aware.fuxion.com",
                PathTemplate: cfg.PaymentApiPathTemplate ?? "/api/products/user/{userId}/generate-power-link",
                ResponseUrlPath: cfg.PaymentResponseUrlPath ?? "data.url",
                UserId: cfg.PaymentUserId ?? "",
                BearerToken: token ?? "",
                Country: cfg.PaymentCountry ?? "pe",
                Description: description,
                Items: items);

            var result = await _fuxion.GenerateSalesLinkAsync(req, cancellationToken);
            if (result.Ok && !string.IsNullOrWhiteSpace(result.Url))
            {
                sb.Append(result.Url);
                generated++;
            }
            else
            {
                sb.Append(FallbackMessage);
                failed++;
                errors.Add(result.ErrorDetail ?? result.ErrorKind.ToString());
            }
        }
        sb.Append(agentText!, cursor, agentText!.Length - cursor);

        return new PaymentLinkResult(sb.ToString(), matches.Count, generated, failed, errors);
    }

    private sealed record AgentPaymentSnapshot(
        bool PaymentEnabled, string? PaymentUserId, string? PaymentCountry,
        string? PaymentCatalogContainerName, string? PaymentCatalogNameColumn, string? PaymentCatalogProductIdColumn,
        string? PaymentApiBaseUrl, string? PaymentApiPathTemplate, string? PaymentResponseUrlPath);

    private static PaymentLinkResult SubstituteAllWithFallback(string text, MatchCollection matches, string reason)
    {
        var sb = new StringBuilder();
        int cursor = 0;
        foreach (Match m in matches)
        {
            sb.Append(text, cursor, m.Index - cursor);
            sb.Append(FallbackMessage);
            cursor = m.Index + m.Length;
        }
        sb.Append(text, cursor, text.Length - cursor);
        return new PaymentLinkResult(sb.ToString(), matches.Count, 0, matches.Count, new[] { reason });
    }

    /// <summary>Parsea "REXET:2, PRUNEX1:1" contra el diccionario case-insensitive nombre->productId.
    /// Nombres sin ":qty" se toman como cantidad 1. Items no encontrados se agregan a "unresolved"
    /// y se omiten del resultado. Cantidades <= 0 se saltan.</summary>
    private static List<FuxionSalesLinkItem> ParseItems(string args, Dictionary<string, string> catalog, out List<string> unresolved)
    {
        unresolved = new List<string>();
        var items = new List<FuxionSalesLinkItem>();
        if (string.IsNullOrWhiteSpace(args)) { return items; }

        foreach (var raw in args.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = raw.Split(':', 2, StringSplitOptions.TrimEntries);
            var name = parts[0];
            var qty = 1;
            if (parts.Length == 2 && !int.TryParse(parts[1], out qty)) { qty = 1; }
            if (qty <= 0) { continue; }
            if (string.IsNullOrWhiteSpace(name)) { continue; }
            if (catalog.TryGetValue(name.Trim(), out var productId))
            {
                items.Add(new FuxionSalesLinkItem(productId, qty));
            }
            else
            {
                unresolved.Add(name);
            }
        }
        return items;
    }

    /// <summary>Carga el catalogo agente->productId del DataContainer configurado. Case-insensitive
    /// en el nombre. Devuelve diccionario vacio si algo falla, con log a nivel Warning.</summary>
    private async Task<Dictionary<string, string>> LoadCatalogAsync(AgentPaymentSnapshot cfg, CancellationToken ct)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(cfg.PaymentCatalogContainerName)) { return result; }

        var containers = await _containers.ListAsync(ct);
        var target = containers.FirstOrDefault(c => string.Equals(c.Name, cfg.PaymentCatalogContainerName, StringComparison.OrdinalIgnoreCase));
        if (target is null) { throw new InvalidOperationException($"contenedor '{cfg.PaymentCatalogContainerName}' no existe"); }

        var detail = await _containers.GetAsync(target.Id, ct);
        if (detail is null) { throw new InvalidOperationException("detalle del contenedor no accesible"); }

        var nameColName = cfg.PaymentCatalogNameColumn ?? "nombre";
        var idColName = cfg.PaymentCatalogProductIdColumn ?? "productId";
        var nameCol = detail.Columns.FirstOrDefault(c => string.Equals(c.Name, nameColName, StringComparison.OrdinalIgnoreCase));
        var idCol = detail.Columns.FirstOrDefault(c => string.Equals(c.Name, idColName, StringComparison.OrdinalIgnoreCase));
        if (nameCol is null || idCol is null)
        {
            throw new InvalidOperationException($"columnas '{nameColName}' o '{idColName}' no encontradas en el contenedor");
        }

        var rows = await _containers.ListRowsAsync(target.Id, search: null, take: 2000, ct);
        foreach (var row in rows)
        {
            if (!row.ValuesByColumnId.TryGetValue(nameCol.Id, out var nameVal) || string.IsNullOrWhiteSpace(nameVal)) { continue; }
            if (!row.ValuesByColumnId.TryGetValue(idCol.Id, out var idVal) || string.IsNullOrWhiteSpace(idVal)) { continue; }
            // Si hay duplicados, gana el primero. Es raro y logueable como advertencia futura.
            result.TryAdd(nameVal.Trim(), idVal.Trim());
        }
        return result;
    }
}
