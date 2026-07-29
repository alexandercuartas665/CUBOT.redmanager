using System.Globalization;
using System.Text;
using CubotRedManager.Application.Abstractions;
using CubotRedManager.Application.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CubotRedManager.Application.Tenancy;

/// <summary>Implementacion de <see cref="IProductLookupService"/>. Lee la config Payment del
/// agente (nombre del contenedor + columnas), filtra las filas cuyo campo Producto contenga la
/// query, opcionalmente por pais, y arma una tabla markdown con TODAS las columnas relevantes
/// (Pais, Producto, Precio, IdProducto, y hasta 3 columnas adicionales si existen).</summary>
public sealed class ProductLookupService : IProductLookupService
{
    // Limite defensivo: si un query mal escrito matchea 100+ filas, cortamos ahi para no explotar
    // el prompt del LLM en la re-invocacion. En la practica una busqueda tipica devuelve 1-5 filas.
    private const int MaxRowsReturned = 40;

    private readonly IApplicationDbContext _db;
    private readonly IDataContainerService _containers;
    private readonly ILogger<ProductLookupService> _logger;

    public ProductLookupService(
        IApplicationDbContext db,
        IDataContainerService containers,
        ILogger<ProductLookupService> logger)
    {
        _db = db;
        _containers = containers;
        _logger = logger;
    }

    public async Task<ProductLookupResult> LookupAsync(Guid agentId, string query, string? countryIso2, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return new ProductLookupResult(false, "", 0, "query vacia");
        }

        var cfg = await _db.AiAgents.AsNoTracking()
            .Where(a => a.Id == agentId)
            .Select(a => new
            {
                a.PaymentCatalogContainerName,
                a.PaymentCatalogNameColumn,
                a.PaymentCatalogProductIdColumn,
                a.PaymentCatalogCountryColumn,
            })
            .FirstOrDefaultAsync(ct);
        if (cfg is null || string.IsNullOrWhiteSpace(cfg.PaymentCatalogContainerName))
        {
            return new ProductLookupResult(false, "", 0, "no hay contenedor configurado en Pagos FUXION del agente");
        }

        var containers = await _containers.ListAsync(ct);
        var target = containers.FirstOrDefault(c => string.Equals(c.Name, cfg.PaymentCatalogContainerName, StringComparison.OrdinalIgnoreCase));
        if (target is null)
        {
            return new ProductLookupResult(false, "", 0, $"contenedor '{cfg.PaymentCatalogContainerName}' no existe");
        }
        var detail = await _containers.GetAsync(target.Id, ct);
        if (detail is null)
        {
            return new ProductLookupResult(false, "", 0, "no se pudo leer el contenedor");
        }

        var nameCol = detail.Columns.FirstOrDefault(c => string.Equals(c.Name, cfg.PaymentCatalogNameColumn ?? "Producto", StringComparison.OrdinalIgnoreCase));
        if (nameCol is null)
        {
            return new ProductLookupResult(false, "", 0, $"columna '{cfg.PaymentCatalogNameColumn ?? "Producto"}' no encontrada");
        }
        var countryCol = string.IsNullOrWhiteSpace(cfg.PaymentCatalogCountryColumn)
            ? null
            : detail.Columns.FirstOrDefault(c => string.Equals(c.Name, cfg.PaymentCatalogCountryColumn, StringComparison.OrdinalIgnoreCase));

        var rows = await _containers.ListRowsAsync(target.Id, search: null, take: 5000, ct);

        var qNorm = NormalizeLoose(query);
        var isoFilter = string.IsNullOrWhiteSpace(countryIso2) ? null : CountryIsoMapper.ToIso2(countryIso2);
        var matched = new List<DataContainerRowDto>();
        foreach (var r in rows)
        {
            var prodVal = r.ValuesByColumnId.GetValueOrDefault(nameCol.Id) ?? "";
            if (!NormalizeLoose(prodVal).Contains(qNorm)) { continue; }
            if (isoFilter is not null && countryCol is not null)
            {
                var rowIso = CountryIsoMapper.ToIso2(r.ValuesByColumnId.GetValueOrDefault(countryCol.Id) ?? "");
                if (!string.Equals(rowIso, isoFilter, StringComparison.OrdinalIgnoreCase)) { continue; }
            }
            matched.Add(r);
            if (matched.Count >= MaxRowsReturned) { break; }
        }

        if (matched.Count == 0)
        {
            var suffix = isoFilter is not null ? $" en pais '{isoFilter}'" : "";
            return new ProductLookupResult(true, $"Sin resultados para '{query}'{suffix} en el contenedor '{target.Name}'.", 0, null);
        }

        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture, $"Resultados de busqueda '{query}'");
        if (isoFilter is not null) { sb.Append(CultureInfo.InvariantCulture, $" pais='{isoFilter}'"); }
        sb.Append(CultureInfo.InvariantCulture, $" ({matched.Count} fila(s) de '{target.Name}'):");
        sb.AppendLine();
        // Encabezado con TODAS las columnas del contenedor. Asi la IA ve Precio, Beneficio,
        // UrlImagen, etc., sin depender de que estos nombres esten hardcodeados.
        sb.Append("| ").Append(string.Join(" | ", detail.Columns.Select(c => c.Name))).AppendLine(" |");
        sb.Append("|").Append(string.Join("|", detail.Columns.Select(_ => "---"))).AppendLine("|");
        foreach (var r in matched)
        {
            sb.Append("| ");
            sb.Append(string.Join(" | ", detail.Columns.Select(c =>
            {
                var v = r.ValuesByColumnId.GetValueOrDefault(c.Id) ?? "";
                // Aplanar celdas multilinea a espacios para no romper la tabla.
                return v.Replace("\r", " ").Replace("\n", " ").Replace("|", "/");
            })));
            sb.AppendLine(" |");
        }
        return new ProductLookupResult(true, sb.ToString(), matched.Count, null);
    }

    /// <summary>Normalizacion para el matching: minusculas, sin acentos, colapsar espacios.</summary>
    private static string NormalizeLoose(string s)
    {
        if (string.IsNullOrEmpty(s)) { return ""; }
        var sb = new StringBuilder(s.Length);
        foreach (var c in s.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) { continue; }
            sb.Append(char.ToLowerInvariant(c));
        }
        return sb.ToString();
    }
}
