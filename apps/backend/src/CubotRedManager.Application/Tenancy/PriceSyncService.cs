using System.Globalization;
using System.Text.RegularExpressions;
using CubotRedManager.Application.Abstractions;
using CubotRedManager.Application.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CubotRedManager.Application.Tenancy;

/// <summary>
/// Implementacion del "Sincronizar precios ahora" del boton en /agentes. Baja el catalogo real de
/// FUXION agrupado por pais (uno por pais que aparece en el DataContainer), compara con el precio
/// actual y hace PATCH SOLO donde difiere. Al terminar guarda <c>AiAgent.PaymentLastPriceSyncAt</c>
/// para que la UI muestre "Ultima sync: hace X".
///
/// Requiere una columna "Precio" en el contenedor (default por nombre; el operador puede tener otro
/// nombre — extraeriamos la columna a config si algun dia hace falta).
/// </summary>
public sealed class PriceSyncService : IPriceSyncService
{
    // Los precios en el contenedor a veces vienen con separadores de miles o simbolos de moneda
    // ($, €, etc.). Este regex saca todo lo que no sea digito o punto para poder parsear.
    private static readonly Regex CleanPriceRegex = new(@"[^\d\.]", RegexOptions.Compiled);

    private readonly IApplicationDbContext _db;
    private readonly IFuxionPaymentClient _fuxion;
    private readonly IAiAgentService _agentService;
    private readonly IDataContainerService _containers;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<PriceSyncService> _logger;

    public PriceSyncService(
        IApplicationDbContext db,
        IFuxionPaymentClient fuxion,
        IAiAgentService agentService,
        IDataContainerService containers,
        TimeProvider timeProvider,
        ILogger<PriceSyncService> logger)
    {
        _db = db;
        _fuxion = fuxion;
        _agentService = agentService;
        _containers = containers;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<PriceSyncResult> SyncPricesAsync(Guid agentId, Guid actorUserId, CancellationToken ct = default)
    {
        // 1) Config del agente y token descifrado (mismo camino que PaymentLinkProcessor).
        var agent = await _db.AiAgents.FirstOrDefaultAsync(a => a.Id == agentId, ct);
        if (agent is null) { return Fail("agente no existe"); }
        if (!agent.PaymentEnabled) { return Fail("PaymentEnabled=false en el agente"); }
        if (string.IsNullOrWhiteSpace(agent.PaymentCatalogContainerName)) { return Fail("no hay contenedor configurado en Pagos FUXION"); }

        var token = await _agentService.GetDecryptedPaymentTokenAsync(agentId, ct);
        if (string.IsNullOrWhiteSpace(token)) { return Fail("no hay token FUXION guardado o esta corrupto"); }
        var baseUrl = string.IsNullOrWhiteSpace(agent.PaymentApiBaseUrl) ? "https://api-aware.fuxion.com" : agent.PaymentApiBaseUrl!;

        // 2) Localizar contenedor y columnas relevantes (nombre, productId, precio, pais).
        var containers = await _containers.ListAsync(ct);
        var target = containers.FirstOrDefault(c => string.Equals(c.Name, agent.PaymentCatalogContainerName, StringComparison.OrdinalIgnoreCase));
        if (target is null) { return Fail($"contenedor '{agent.PaymentCatalogContainerName}' no existe"); }
        var detail = await _containers.GetAsync(target.Id, ct);
        if (detail is null) { return Fail("no se pudo leer el contenedor"); }

        var idCol = detail.Columns.FirstOrDefault(c => string.Equals(c.Name, agent.PaymentCatalogProductIdColumn ?? "productId", StringComparison.OrdinalIgnoreCase));
        if (idCol is null) { return Fail($"columna '{agent.PaymentCatalogProductIdColumn ?? "productId"}' no encontrada"); }
        // La columna Precio la buscamos por nombre "Precio" (tolerante a mayusculas). Si el operador
        // la nombro distinto, hay que agregar un campo de config; hoy no lo pedimos.
        var priceCol = detail.Columns.FirstOrDefault(c => string.Equals(c.Name, "Precio", StringComparison.OrdinalIgnoreCase));
        if (priceCol is null) { return Fail("no encontre una columna llamada 'Precio' en el contenedor"); }
        var countryCol = string.IsNullOrWhiteSpace(agent.PaymentCatalogCountryColumn)
            ? null
            : detail.Columns.FirstOrDefault(c => string.Equals(c.Name, agent.PaymentCatalogCountryColumn, StringComparison.OrdinalIgnoreCase));

        // 3) Filas: filtrar solo las que tienen IdProducto Y pais reconocible.
        var rows = await _containers.ListRowsAsync(target.Id, search: null, take: 5000, ct);
        var candidates = new List<(DataContainerRowDto Row, string ProductId, string Iso)>();
        var defaultIso = CountryIsoMapper.ToIso2(agent.PaymentCountry ?? "");
        foreach (var r in rows)
        {
            var pid = (r.ValuesByColumnId.GetValueOrDefault(idCol.Id) ?? "").Trim();
            if (string.IsNullOrWhiteSpace(pid)) { continue; }
            var isoRaw = countryCol is not null
                ? (r.ValuesByColumnId.GetValueOrDefault(countryCol.Id) ?? "").Trim()
                : "";
            var iso = string.IsNullOrWhiteSpace(isoRaw) ? defaultIso : CountryIsoMapper.ToIso2(isoRaw);
            if (string.IsNullOrWhiteSpace(iso)) { continue; }
            candidates.Add((r, pid, iso));
        }

        if (candidates.Count == 0)
        {
            return Fail("ninguna fila con IdProducto + pais reconocido");
        }

        // 4) Bajar catalogo real por cada pais distinto (1 llamada por pais, no por producto).
        var countries = candidates.Select(c => c.Iso).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var pricesByCountry = new Dictionary<string, IReadOnlyDictionary<string, decimal>>(StringComparer.OrdinalIgnoreCase);
        var errors = new List<string>();
        foreach (var iso in countries)
        {
            var res = await _fuxion.GetProductsByCountryAsync(baseUrl, token!, iso, ct);
            if (res.Ok)
            {
                pricesByCountry[iso] = res.Prices;
            }
            else
            {
                errors.Add($"[{iso}] {res.ErrorDetail ?? "error desconocido"}");
                pricesByCountry[iso] = new Dictionary<string, decimal>();
            }
        }
        // Si TODOS los paises fallaron, cortamos aca antes de tocar el DataContainer.
        if (pricesByCountry.Values.All(d => d.Count == 0))
        {
            return new PriceSyncResult(false, candidates.Count, 0, 0, candidates.Count, errors, null);
        }

        // 5) Comparar y patchear solo lo que cambia.
        int updated = 0, alreadyOk = 0, skipped = 0;
        foreach (var (row, pid, iso) in candidates)
        {
            if (!pricesByCountry.TryGetValue(iso, out var dict) || !dict.TryGetValue(pid, out var realPrice))
            {
                skipped++;
                continue;
            }
            var currentRaw = row.ValuesByColumnId.GetValueOrDefault(priceCol.Id);
            var current = ParsePrice(currentRaw);
            if (current is not null && Math.Abs(current.Value - realPrice) < 0.5m)
            {
                alreadyOk++;
                continue;
            }
            // PATCH: merge sobre valores existentes (preservamos otras columnas).
            var merged = new Dictionary<Guid, string?>(row.ValuesByColumnId);
            merged[priceCol.Id] = FormatPrice(realPrice);
            try
            {
                await _containers.SaveRowAsync(new SaveDataRowRequest(target.Id, row.Id, merged), actorUserId, ct);
                updated++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "PriceSync: fallo PATCH row {RowId}", row.Id);
                skipped++;
                errors.Add($"row {row.Id:N} PATCH fail: {ex.Message}");
            }
        }

        // 6) Persistir timestamp en el agente (siempre, incluso si updated=0, para reflejar la corrida).
        var syncedAt = _timeProvider.GetUtcNow();
        agent.PaymentLastPriceSyncAt = syncedAt;
        await _db.SaveChangesAsync(ct);

        return new PriceSyncResult(
            Ok: true,
            RowsChecked: candidates.Count,
            RowsUpdated: updated,
            RowsAlreadyOk: alreadyOk,
            RowsSkipped: skipped,
            Errors: errors,
            SyncedAt: syncedAt);
    }

    private static PriceSyncResult Fail(string reason)
        => new(false, 0, 0, 0, 0, new[] { reason }, null);

    private static decimal? ParsePrice(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) { return null; }
        var cleaned = CleanPriceRegex.Replace(s.Replace(",", ""), "");
        return decimal.TryParse(cleaned, NumberStyles.Any, CultureInfo.InvariantCulture, out var p) ? p : (decimal?)null;
    }

    private static string FormatPrice(decimal p)
    {
        // Enteros van como enteros; decimales sin ceros a la derecha.
        return p == Math.Truncate(p)
            ? ((long)p).ToString(CultureInfo.InvariantCulture)
            : p.ToString("0.##", CultureInfo.InvariantCulture);
    }
}
