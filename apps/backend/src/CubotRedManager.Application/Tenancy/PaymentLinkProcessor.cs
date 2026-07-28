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
            return new PaymentLinkResult(agentText ?? string.Empty, 0, 0, 0, Array.Empty<string>(), Array.Empty<string>());
        }

        // Cargar config del agente (una sola vez para todos los markers). No hace falta
        // IgnoreQueryFilters aqui: el dispatcher ya seteo el ambient tenant override antes de
        // llamarnos, asi que el HasQueryFilter matchea.
        var cfg = await _db.AiAgents.AsNoTracking()
            .Where(a => a.Id == agentId)
            .Select(a => new AgentPaymentSnapshot(
                a.PaymentEnabled, a.PaymentUserId, a.PaymentCountry,
                a.PaymentCatalogContainerName, a.PaymentCatalogNameColumn, a.PaymentCatalogProductIdColumn,
                a.PaymentCatalogCountryColumn,
                a.PaymentApiBaseUrl, a.PaymentApiPathTemplate, a.PaymentResponseUrlPath))
            .FirstOrDefaultAsync(cancellationToken);

        if (cfg is null || !cfg.PaymentEnabled)
        {
            // Feature apagada: sustituye markers por fallback para no exponer sintaxis al cliente.
            _logger.LogInformation("PaymentLink: markers detectados pero feature desactivada en agente {AgentId}", agentId);
            return SubstituteAllWithFallback(agentText!, matches, "PaymentEnabled=false");
        }

        // Diccionario nombre -> lista de (productId, pais). Un mismo nombre puede aparecer en varios
        // paises (misma marca en Bolivia, Colombia, Peru...) y guardamos TODAS para que ParseItems
        // elija la variante correcta segun el pais del agente o el override @pais del marcador.
        Dictionary<string, List<CatalogEntry>> catalog;
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
        var defaultCountry = cfg.PaymentCountry ?? "pe";
        var errors = new List<string>();
        var allGeneratedUrls = new List<string>();
        int generated = 0, failed = 0;
        var sb = new StringBuilder();
        int cursor = 0;

        foreach (Match match in matches)
        {
            sb.Append(agentText!, cursor, match.Index - cursor);
            cursor = match.Index + match.Length;

            var args = match.Groups["args"].Success ? match.Groups["args"].Value : "";
            var parsed = ParseItems(args, catalog, defaultCountry, out var unresolved);
            if (unresolved.Count > 0)
            {
                errors.Add($"productos no encontrados en catalogo '{cfg.PaymentCatalogContainerName}': {string.Join(", ", unresolved)}");
            }
            if (parsed.Count == 0)
            {
                sb.Append(FallbackMessage);
                failed++;
                continue;
            }

            // Agrupar por pais efectivo: 1 POST por grupo. Un carrito FUXION solo lleva 1 country,
            // asi que si el LLM pidio productos de PE + CO en un solo marker generamos 2 URLs y las
            // pegamos separadas por salto de linea. En el caso comun (todos mismo pais) es 1 solo POST.
            var groups = parsed.GroupBy(p => p.Country, StringComparer.OrdinalIgnoreCase).ToList();
            var description = $"WA {DateTime.UtcNow:yyyyMMdd-HHmmss}";
            var groupUrls = new List<string>();
            int groupOk = 0, groupErr = 0;

            foreach (var g in groups)
            {
                var req = new FuxionGenerateLinkRequest(
                    BaseUrl: cfg.PaymentApiBaseUrl ?? "https://api-aware.fuxion.com",
                    PathTemplate: cfg.PaymentApiPathTemplate ?? "/api/products/user/{userId}/generate-power-link",
                    ResponseUrlPath: cfg.PaymentResponseUrlPath ?? "data.url",
                    UserId: cfg.PaymentUserId ?? "",
                    BearerToken: token ?? "",
                    Country: g.Key,
                    Description: groups.Count > 1 ? $"{description} {g.Key}" : description,
                    Items: g.Select(x => new FuxionSalesLinkItem(x.ProductId, x.Amount)).ToList());

                var result = await _fuxion.GenerateSalesLinkAsync(req, cancellationToken);
                if (result.Ok && !string.IsNullOrWhiteSpace(result.Url))
                {
                    groupUrls.Add(result.Url);
                    groupOk++;
                }
                else
                {
                    groupErr++;
                    errors.Add($"[{g.Key}] {result.ErrorDetail ?? result.ErrorKind.ToString()}");
                }
            }

            if (groupUrls.Count > 0)
            {
                sb.Append(string.Join('\n', groupUrls));
                // Traza detallada: cada URL con el resumen de items (productId x qty en el country) que se
                // pidieron a FUXION. Sirve para diagnosticar cuando FUXION emite un URL "valido" (201 OK)
                // pero la tienda publica no carga el carrito (ej. item deshabilitado en la tienda pese a
                // estar en el catalogo, o country/item combo no permitido).
                var groupsList = groups.ToList();
                for (int i = 0; i < groupsList.Count && i < groupUrls.Count; i++)
                {
                    var g = groupsList[i];
                    var itemsStr = string.Join(",", g.Select(x => $"{x.ProductId}x{x.Amount}"));
                    allGeneratedUrls.Add($"{groupUrls[i]} (pais={g.Key} items=[{itemsStr}])");
                }
                generated += groupOk;
                failed += groupErr;
            }
            else
            {
                sb.Append(FallbackMessage);
                failed += groups.Count;
            }
        }
        sb.Append(agentText!, cursor, agentText!.Length - cursor);

        return new PaymentLinkResult(sb.ToString(), matches.Count, generated, failed, errors, allGeneratedUrls);
    }

    private sealed record CatalogEntry(string ProductId, string? Country);
    private sealed record ParsedItem(string ProductId, int Amount, string Country);

    private sealed record AgentPaymentSnapshot(
        bool PaymentEnabled, string? PaymentUserId, string? PaymentCountry,
        string? PaymentCatalogContainerName, string? PaymentCatalogNameColumn, string? PaymentCatalogProductIdColumn,
        string? PaymentCatalogCountryColumn,
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
        return new PaymentLinkResult(sb.ToString(), matches.Count, 0, matches.Count, new[] { reason }, Array.Empty<string>());
    }

    // Nombres de pais -> ISO2 (compartido con PriceSyncService via CountryIsoMapper).
    private static string NormalizeCountryToIso2(string raw) => CountryIsoMapper.ToIso2(raw);

    /// <summary>Parsea "REXET:2, PRUNEX1:1, OFF@co:1" contra el catalogo. Resuelve nombre->productId y
    /// deriva el pais efectivo:
    ///  - Sintaxis extendida: "NOMBRE@pais:qty" fuerza el pais (ISO2 o nombre completo).
    ///  - Sin @: si el catalogo tiene varias filas para el mismo nombre (mismo producto en varios paises),
    ///    prefiere la fila cuyo pais coincida con <paramref name="defaultCountry"/>. Si ninguna coincide,
    ///    toma la primera del catalogo.
    /// Nombres sin ":qty" se toman como cantidad 1. No encontrados se agregan a "unresolved" y se omiten.</summary>
    private static List<ParsedItem> ParseItems(string args, Dictionary<string, List<CatalogEntry>> catalog, string defaultCountry, out List<string> unresolved)
    {
        unresolved = new List<string>();
        var items = new List<ParsedItem>();
        if (string.IsNullOrWhiteSpace(args)) { return items; }

        var defaultIso = NormalizeCountryToIso2(defaultCountry);
        foreach (var raw in args.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = raw.Split(':', 2, StringSplitOptions.TrimEntries);
            var namePart = parts[0];
            var qty = 1;
            if (parts.Length == 2 && !int.TryParse(parts[1], out qty)) { qty = 1; }
            if (qty <= 0) { continue; }
            if (string.IsNullOrWhiteSpace(namePart)) { continue; }

            // Separar override de pais: "OFF@co" -> name=OFF, forcedIso=co
            string name = namePart;
            string? forcedIso = null;
            var atIdx = namePart.LastIndexOf('@');
            if (atIdx > 0 && atIdx < namePart.Length - 1)
            {
                name = namePart.Substring(0, atIdx).Trim();
                forcedIso = NormalizeCountryToIso2(namePart.Substring(atIdx + 1).Trim());
            }

            if (!catalog.TryGetValue(name.Trim(), out var candidates) || candidates.Count == 0)
            {
                unresolved.Add(name);
                continue;
            }

            // Seleccion de la variante:
            //   1) Si el marcador incluyo @pais, matchea exacto por ISO.
            //   2) Sino, prefiere la fila cuyo pais coincida con el default del agente.
            //   3) Fallback: primera del catalogo (comportamiento historico).
            CatalogEntry pick;
            if (forcedIso is not null && forcedIso.Length > 0)
            {
                var byForced = candidates.FirstOrDefault(e => !string.IsNullOrWhiteSpace(e.Country)
                    && string.Equals(NormalizeCountryToIso2(e.Country!), forcedIso, StringComparison.OrdinalIgnoreCase));
                pick = byForced ?? candidates[0];
            }
            else if (!string.IsNullOrEmpty(defaultIso))
            {
                var byDefault = candidates.FirstOrDefault(e => !string.IsNullOrWhiteSpace(e.Country)
                    && string.Equals(NormalizeCountryToIso2(e.Country!), defaultIso, StringComparison.OrdinalIgnoreCase));
                pick = byDefault ?? candidates[0];
            }
            else
            {
                pick = candidates[0];
            }

            var rawCountry = string.IsNullOrWhiteSpace(pick.Country) ? defaultCountry : pick.Country!;
            var country = forcedIso ?? NormalizeCountryToIso2(rawCountry);
            items.Add(new ParsedItem(pick.ProductId, qty, country));
        }
        return items;
    }

    /// <summary>Carga el catalogo del DataContainer configurado. Case-insensitive en el nombre.
    /// Si el operador configuro PaymentCatalogCountryColumn Y esa columna existe, se lee el pais
    /// por fila (permite un mismo agente vender en varios paises). Si no, el pais efectivo se
    /// cae al PaymentCountry del agente en ParseItems.</summary>
    private async Task<Dictionary<string, List<CatalogEntry>>> LoadCatalogAsync(AgentPaymentSnapshot cfg, CancellationToken ct)
    {
        var result = new Dictionary<string, List<CatalogEntry>>(StringComparer.OrdinalIgnoreCase);
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
        // Columna pais es opcional. Si el operador la nombro pero no existe en el contenedor,
        // no fallamos: solo omitimos el pais por fila y caemos al default del agente.
        var countryCol = string.IsNullOrWhiteSpace(cfg.PaymentCatalogCountryColumn)
            ? null
            : detail.Columns.FirstOrDefault(c => string.Equals(c.Name, cfg.PaymentCatalogCountryColumn, StringComparison.OrdinalIgnoreCase));

        var rows = await _containers.ListRowsAsync(target.Id, search: null, take: 2000, ct);
        foreach (var row in rows)
        {
            if (!row.ValuesByColumnId.TryGetValue(nameCol.Id, out var nameVal) || string.IsNullOrWhiteSpace(nameVal)) { continue; }
            if (!row.ValuesByColumnId.TryGetValue(idCol.Id, out var idVal) || string.IsNullOrWhiteSpace(idVal)) { continue; }
            string? country = null;
            if (countryCol is not null && row.ValuesByColumnId.TryGetValue(countryCol.Id, out var cVal) && !string.IsNullOrWhiteSpace(cVal))
            {
                country = cVal.Trim();
            }
            var key = nameVal.Trim();
            if (!result.TryGetValue(key, out var list))
            {
                list = new List<CatalogEntry>();
                result[key] = list;
            }
            // El mismo producto vale en varios paises: preservamos TODAS las variantes.
            // La seleccion final (que fila usar) la hace ParseItems segun el pais del agente.
            list.Add(new CatalogEntry(idVal.Trim(), country));
        }
        return result;
    }
}
