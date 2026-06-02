using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using CubotRedManager.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace CubotRedManager.Application.Tenancy;

/// <summary>
/// Implementacion del "MCP" simplificado para DataContainers: NO levanta un servidor MCP HTTP;
/// resuelve placeholders del prompt del agente con datos REALES del tenant antes de enviar
/// al LLM. Tenant-scoped via IApplicationDbContext (los DbSets ya tienen filtro por tenant).
/// </summary>
public sealed class DataContainerMcpService : IDataContainerMcpService
{
    private readonly IApplicationDbContext _db;

    // Regex compilados (case-insensitive) para los dos placeholders soportados.
    private static readonly Regex ListContainersRegex = new(
        @"\{\{\s*LIST\.CONTAINERS\s*\}\}",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex ContainerRegex = new(
        @"\{\{\s*CONTAINER\s*:\s*([^}]+?)\s*\}\}",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public DataContainerMcpService(IApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<string> ListContainersAsync(CancellationToken ct = default)
    {
        var containers = await _db.DataContainers.AsNoTracking()
            .OrderBy(c => c.Name)
            .Select(c => new { c.Id, c.Name, c.Description })
            .ToListAsync(ct);

        if (containers.Count == 0)
        {
            return "Containers disponibles: (ninguno en este tenant).";
        }

        var ids = containers.Select(c => c.Id).ToList();

        var colCounts = await _db.DataContainerColumns.AsNoTracking()
            .Where(c => ids.Contains(c.ContainerId))
            .GroupBy(c => c.ContainerId)
            .Select(g => new { g.Key, C = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.C, ct);

        var rowCounts = await _db.DataContainerRows.AsNoTracking()
            .Where(r => ids.Contains(r.ContainerId))
            .GroupBy(r => r.ContainerId)
            .Select(g => new { g.Key, C = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.C, ct);

        var sb = new StringBuilder();
        sb.AppendLine("Containers disponibles:");
        foreach (var c in containers)
        {
            var cols = colCounts.TryGetValue(c.Id, out var cc) ? cc : 0;
            var rows = rowCounts.TryGetValue(c.Id, out var rc) ? rc : 0;
            var desc = string.IsNullOrWhiteSpace(c.Description) ? "sin descripcion" : c.Description!.Trim();
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "- \"{0}\" ({1} columnas, {2} filas) - {3}",
                c.Name, cols, rows, desc));
        }
        return sb.ToString().TrimEnd();
    }

    public async Task<string> QueryContainerAsync(string containerName, int limit = 100, CancellationToken ct = default)
    {
        if (limit <= 0) { limit = 100; }
        var normalizedTarget = NormalizeName(containerName);

        // Cargo todos los containers del tenant y comparo por nombre normalizado (case-insensitive,
        // espacios colapsados). Asi cubrimos pequenas variaciones (mayusculas, espacios extra).
        var all = await _db.DataContainers.AsNoTracking()
            .Select(c => new { c.Id, c.Name })
            .ToListAsync(ct);

        var container = all.FirstOrDefault(c => NormalizeName(c.Name) == normalizedTarget);
        if (container is null)
        {
            var available = string.Join(", ", all.Select(c => "\"" + c.Name + "\""));
            return string.Format(CultureInfo.InvariantCulture,
                "Container '{0}' no existe. Containers disponibles: {1}",
                containerName, string.IsNullOrEmpty(available) ? "(ninguno)" : available);
        }

        var columns = await _db.DataContainerColumns.AsNoTracking()
            .Where(c => c.ContainerId == container.Id)
            .OrderBy(c => c.SortOrder).ThenBy(c => c.Name)
            .Select(c => new { c.Id, c.Name })
            .ToListAsync(ct);

        if (columns.Count == 0)
        {
            return string.Format(CultureInfo.InvariantCulture,
                "Container \"{0}\" no tiene columnas definidas.", container.Name);
        }

        var totalRowCount = await _db.DataContainerRows.AsNoTracking()
            .CountAsync(r => r.ContainerId == container.Id, ct);

        var rows = await _db.DataContainerRows.AsNoTracking()
            .Where(r => r.ContainerId == container.Id)
            .OrderBy(r => r.CreatedAt)
            .Take(limit)
            .Select(r => r.Id)
            .ToListAsync(ct);

        var cellMap = new Dictionary<Guid, Dictionary<Guid, string?>>();
        if (rows.Count > 0)
        {
            var cells = await _db.DataContainerCells.AsNoTracking()
                .Where(c => rows.Contains(c.RowId))
                .Select(c => new { c.RowId, c.ColumnId, c.Value })
                .ToListAsync(ct);

            cellMap = cells
                .GroupBy(c => c.RowId)
                .ToDictionary(g => g.Key, g => g.ToDictionary(x => x.ColumnId, x => x.Value));
        }

        var sb = new StringBuilder();
        var shown = Math.Min(totalRowCount, rows.Count);
        if (totalRowCount > shown)
        {
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "Container \"{0}\" ({1} filas totales, mostrando {2}):",
                container.Name, totalRowCount, shown));
        }
        else
        {
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "Container \"{0}\" ({1} filas):", container.Name, totalRowCount));
        }

        if (totalRowCount == 0)
        {
            sb.AppendLine("(sin filas).");
            return sb.ToString().TrimEnd();
        }

        // Header markdown.
        sb.Append('|');
        foreach (var col in columns) { sb.Append(' ').Append(EscapeCell(col.Name)).Append(" |"); }
        sb.AppendLine();
        sb.Append('|');
        foreach (var _ in columns) { sb.Append("---|"); }
        sb.AppendLine();

        // Rows.
        foreach (var rowId in rows)
        {
            sb.Append('|');
            cellMap.TryGetValue(rowId, out var rowCells);
            foreach (var col in columns)
            {
                string? val = null;
                rowCells?.TryGetValue(col.Id, out val);
                sb.Append(' ').Append(EscapeCell(val)).Append(" |");
            }
            sb.AppendLine();
        }

        return sb.ToString().TrimEnd();
    }

    public async Task<string> ResolvePlaceholdersAsync(string text, bool mcpEnabled, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(text)) { return text; }
        if (!text.Contains("{{", StringComparison.Ordinal)) { return text; }

        // Si el MCP esta DESHABILITADO para este agente, sustituimos los placeholders por una nota
        // corta. Asi el LLM no ve el placeholder literal (que confunde) ni datos del tenant que
        // este agente no deberia leer.
        if (!mcpEnabled)
        {
            const string note = "(Acceso al MCP de Contenedores deshabilitado para este agente.)";
            text = ListContainersRegex.Replace(text, _ => note);
            text = ContainerRegex.Replace(text, _ => note);
            return text;
        }

        // 1) {{LIST.CONTAINERS}}
        if (ListContainersRegex.IsMatch(text))
        {
            var list = await ListContainersAsync(ct);
            text = ListContainersRegex.Replace(text, _ => list);
        }

        // 2) {{CONTAINER:nombre}} (puede haber varios distintos)
        var matches = ContainerRegex.Matches(text);
        if (matches.Count == 0) { return text; }

        // Resolver cada placeholder UNICO una sola vez.
        var unique = matches
            .Select(m => m.Groups[1].Value.Trim())
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var resolved = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in unique)
        {
            resolved[name] = await QueryContainerAsync(name, 100, ct);
        }

        text = ContainerRegex.Replace(text, m =>
        {
            var key = m.Groups[1].Value.Trim();
            return resolved.TryGetValue(key, out var content) ? content : m.Value;
        });

        return text;
    }

    private static string NormalizeName(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) { return ""; }
        var lower = raw.Trim().ToLowerInvariant();
        // Colapsar espacios.
        var sb = new StringBuilder(lower.Length);
        var lastWasSpace = false;
        foreach (var ch in lower)
        {
            if (char.IsWhiteSpace(ch))
            {
                if (!lastWasSpace && sb.Length > 0) { sb.Append(' '); lastWasSpace = true; }
            }
            else
            {
                sb.Append(ch);
                lastWasSpace = false;
            }
        }
        return sb.ToString().Trim();
    }

    private static string EscapeCell(string? raw)
    {
        if (string.IsNullOrEmpty(raw)) { return ""; }
        // Solo escapamos el caracter que rompe la tabla markdown.
        return raw.Replace("|", "\\|").Replace("\r", " ").Replace("\n", " ").Trim();
    }
}
