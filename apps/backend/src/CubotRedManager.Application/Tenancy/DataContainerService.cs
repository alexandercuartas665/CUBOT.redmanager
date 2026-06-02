using System.Globalization;
using System.Text;
using ClosedXML.Excel;
using CubotRedManager.Application.Abstractions;
using CubotRedManager.Domain.Entities;
using CubotRedManager.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace CubotRedManager.Application.Tenancy;

/// <summary>
/// Servicio de Contenedor de Datos (modelos dinamicos EAV).
///
/// Reglas clave:
/// - Save de modelo: actualiza el set de columnas. NO permite borrar columnas que ya
///   tienen celdas asociadas; en ese caso devuelve error claro al usuario.
/// - SaveRow: upsert celda por celda segun el diccionario de valores recibido.
/// - Import Excel: la fila 1 son headers; el matching con columnas del modelo es
///   case-insensitive, sin acentos y sin espacios extra. Si el modelo tiene una
///   columna obligatoria que el Excel no trae, se reporta como error.
/// </summary>
public sealed class DataContainerService : IDataContainerService
{
    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly IAuditWriter _audit;

    public DataContainerService(IApplicationDbContext db, ITenantContext tenantContext, IAuditWriter audit)
    {
        _db = db;
        _tenantContext = tenantContext;
        _audit = audit;
    }

    public async Task<IReadOnlyList<DataContainerDto>> ListAsync(CancellationToken ct = default)
    {
        var containers = await _db.DataContainers.AsNoTracking()
            .OrderBy(c => c.Name)
            .ToListAsync(ct);
        if (containers.Count == 0) { return Array.Empty<DataContainerDto>(); }

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

        return containers.Select(c => new DataContainerDto(
            c.Id,
            c.Name,
            c.Description,
            colCounts.TryGetValue(c.Id, out var cc) ? cc : 0,
            rowCounts.TryGetValue(c.Id, out var rc) ? rc : 0,
            c.UpdatedAt ?? c.CreatedAt)).ToList();
    }

    public async Task<DataContainerDetailDto?> GetAsync(Guid id, CancellationToken ct = default)
    {
        var c = await _db.DataContainers.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (c is null) { return null; }
        var cols = await _db.DataContainerColumns.AsNoTracking()
            .Where(x => x.ContainerId == id)
            .OrderBy(x => x.SortOrder).ThenBy(x => x.Name)
            .ToListAsync(ct);
        return new DataContainerDetailDto(
            c.Id, c.Name, c.Description,
            cols.Select(MapColumn).ToList());
    }

    public async Task<DataContainerDetailDto?> SaveAsync(SaveDataContainerRequest req, Guid actorUserId, CancellationToken ct = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId) { return null; }
        var name = (req.Name ?? "").Trim();
        if (string.IsNullOrWhiteSpace(name)) { return null; }
        if (req.Columns is null || req.Columns.Count == 0) { return null; }

        DataContainer? entity;
        if (req.Id is { } id)
        {
            entity = await _db.DataContainers.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (entity is null) { return null; }
            entity.Name = name;
            entity.Description = string.IsNullOrWhiteSpace(req.Description) ? null : req.Description!.Trim();

            // Manejo de columnas: replaceAll por id.
            var existing = await _db.DataContainerColumns
                .Where(c => c.ContainerId == entity.Id)
                .ToListAsync(ct);
            var keepIds = req.Columns.Where(c => c.Id is not null).Select(c => c.Id!.Value).ToHashSet();
            var toRemove = existing.Where(c => !keepIds.Contains(c.Id)).ToList();

            // No se permite borrar una columna que tenga celdas asociadas.
            if (toRemove.Count > 0)
            {
                var removeIds = toRemove.Select(c => c.Id).ToList();
                var hasCells = await _db.DataContainerCells.AnyAsync(cell => removeIds.Contains(cell.ColumnId), ct);
                if (hasCells)
                {
                    // Devolvemos detalle actual sin guardar (el caller debe interpretar la falta de cambios).
                    // Para una senal mas explicita, lanzamos InvalidOperationException con texto claro.
                    throw new InvalidOperationException(
                        "No se puede borrar una columna que ya tiene datos. Vacia primero esa columna o elimina las filas afectadas.");
                }
                _db.DataContainerColumns.RemoveRange(toRemove);
            }

            // Update / add
            foreach (var input in req.Columns)
            {
                var cleanName = (input.Name ?? "").Trim();
                if (string.IsNullOrWhiteSpace(cleanName)) { continue; }
                var cleanDesc = string.IsNullOrWhiteSpace(input.Description) ? null : input.Description!.Trim();

                if (input.Id is { } cid)
                {
                    var col = existing.FirstOrDefault(c => c.Id == cid);
                    if (col is null) { continue; }
                    col.Name = cleanName;
                    col.Description = cleanDesc;
                    col.Type = input.Type;
                    col.SortOrder = input.SortOrder;
                    col.IsRequired = input.IsRequired;
                }
                else
                {
                    _db.DataContainerColumns.Add(new DataContainerColumn
                    {
                        TenantId = tenantId,
                        ContainerId = entity.Id,
                        Name = cleanName,
                        Description = cleanDesc,
                        Type = input.Type,
                        SortOrder = input.SortOrder,
                        IsRequired = input.IsRequired
                    });
                }
            }

            _audit.Write(actorUserId, "datacontainer.save", nameof(DataContainer), entity.Id,
                previousValue: null, newValue: new { entity.Name, Columns = req.Columns.Count }, tenantId: tenantId);
        }
        else
        {
            entity = new DataContainer
            {
                TenantId = tenantId,
                Name = name,
                Description = string.IsNullOrWhiteSpace(req.Description) ? null : req.Description!.Trim()
            };
            _db.DataContainers.Add(entity);
            foreach (var input in req.Columns)
            {
                var cleanName = (input.Name ?? "").Trim();
                if (string.IsNullOrWhiteSpace(cleanName)) { continue; }
                _db.DataContainerColumns.Add(new DataContainerColumn
                {
                    TenantId = tenantId,
                    ContainerId = entity.Id,
                    Name = cleanName,
                    Description = string.IsNullOrWhiteSpace(input.Description) ? null : input.Description!.Trim(),
                    Type = input.Type,
                    SortOrder = input.SortOrder,
                    IsRequired = input.IsRequired
                });
            }
            _audit.Write(actorUserId, "datacontainer.save", nameof(DataContainer), entity.Id,
                previousValue: null, newValue: new { entity.Name, Columns = req.Columns.Count }, tenantId: tenantId);
        }

        await _db.SaveChangesAsync(ct);
        return await GetAsync(entity.Id, ct);
    }

    public async Task<bool> DeleteAsync(Guid id, Guid actorUserId, CancellationToken ct = default)
    {
        var entity = await _db.DataContainers.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (entity is null) { return false; }
        _db.DataContainers.Remove(entity);
        _audit.Write(actorUserId, "datacontainer.delete", nameof(DataContainer), entity.Id,
            previousValue: new { entity.Name }, newValue: null, tenantId: entity.TenantId);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<IReadOnlyList<DataContainerRowDto>> ListRowsAsync(Guid containerId, string? search = null, int take = 500, CancellationToken ct = default)
    {
        var rowsQuery = _db.DataContainerRows.AsNoTracking()
            .Where(r => r.ContainerId == containerId)
            .OrderByDescending(r => r.CreatedAt)
            .Take(take);
        var rows = await rowsQuery.ToListAsync(ct);
        if (rows.Count == 0) { return Array.Empty<DataContainerRowDto>(); }

        var rowIds = rows.Select(r => r.Id).ToList();
        var cells = await _db.DataContainerCells.AsNoTracking()
            .Where(c => rowIds.Contains(c.RowId))
            .ToListAsync(ct);

        var grouped = cells.GroupBy(c => c.RowId)
            .ToDictionary(g => g.Key, g => g.ToDictionary(x => x.ColumnId, x => x.Value));

        var dtos = rows.Select(r => new DataContainerRowDto(
            r.Id,
            r.CreatedAt,
            grouped.TryGetValue(r.Id, out var d) ? d : new Dictionary<Guid, string?>()
        )).ToList();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLowerInvariant();
            dtos = dtos.Where(d => d.ValuesByColumnId.Values.Any(v =>
                !string.IsNullOrEmpty(v) && v!.ToLowerInvariant().Contains(s))).ToList();
        }

        return dtos;
    }

    public async Task<DataContainerRowDto?> SaveRowAsync(SaveDataRowRequest req, Guid actorUserId, CancellationToken ct = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId) { return null; }
        var container = await _db.DataContainers.FirstOrDefaultAsync(c => c.Id == req.ContainerId, ct);
        if (container is null) { return null; }

        DataContainerRow row;
        if (req.RowId is { } rid)
        {
            var existing = await _db.DataContainerRows.FirstOrDefaultAsync(r => r.Id == rid, ct);
            if (existing is null) { return null; }
            row = existing;
        }
        else
        {
            row = new DataContainerRow
            {
                TenantId = tenantId,
                ContainerId = req.ContainerId
            };
            _db.DataContainerRows.Add(row);
        }

        // Upsert celdas.
        var existingCells = req.RowId is { } existRid
            ? await _db.DataContainerCells.Where(c => c.RowId == existRid).ToListAsync(ct)
            : new List<DataContainerCell>();

        foreach (var kv in req.ValuesByColumnId)
        {
            var cell = existingCells.FirstOrDefault(c => c.ColumnId == kv.Key);
            if (cell is null)
            {
                _db.DataContainerCells.Add(new DataContainerCell
                {
                    TenantId = tenantId,
                    RowId = row.Id,
                    ColumnId = kv.Key,
                    Value = kv.Value
                });
            }
            else
            {
                cell.Value = kv.Value;
            }
        }

        _audit.Write(actorUserId, "datacontainer.row.save", nameof(DataContainerRow), row.Id,
            previousValue: null, newValue: new { ContainerId = req.ContainerId, Cells = req.ValuesByColumnId.Count }, tenantId: tenantId);

        await _db.SaveChangesAsync(ct);

        // Releer celdas para devolver el snapshot completo.
        var allCells = await _db.DataContainerCells.AsNoTracking()
            .Where(c => c.RowId == row.Id)
            .ToListAsync(ct);
        return new DataContainerRowDto(row.Id, row.CreatedAt,
            allCells.ToDictionary(c => c.ColumnId, c => c.Value));
    }

    public async Task<bool> DeleteRowAsync(Guid rowId, Guid actorUserId, CancellationToken ct = default)
    {
        var row = await _db.DataContainerRows.FirstOrDefaultAsync(r => r.Id == rowId, ct);
        if (row is null) { return false; }
        _db.DataContainerRows.Remove(row);
        _audit.Write(actorUserId, "datacontainer.row.delete", nameof(DataContainerRow), row.Id,
            previousValue: new { row.ContainerId }, newValue: null, tenantId: row.TenantId);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<DataImportResult> ImportFromExcelAsync(Guid containerId, Stream xlsxStream, Guid actorUserId, CancellationToken ct = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId)
        {
            return new DataImportResult(false, 0, 0, new[] { "Sin tenant activo." });
        }
        var container = await _db.DataContainers.FirstOrDefaultAsync(c => c.Id == containerId, ct);
        if (container is null)
        {
            return new DataImportResult(false, 0, 0, new[] { "Modelo no encontrado." });
        }
        var columns = await _db.DataContainerColumns.AsNoTracking()
            .Where(c => c.ContainerId == containerId)
            .OrderBy(c => c.SortOrder)
            .ToListAsync(ct);
        if (columns.Count == 0)
        {
            return new DataImportResult(false, 0, 0, new[] { "El modelo no tiene columnas definidas." });
        }

        XLWorkbook workbook;
        try
        {
            workbook = new XLWorkbook(xlsxStream);
        }
        catch (Exception ex)
        {
            return new DataImportResult(false, 0, 0, new[] { $"No se pudo leer el archivo Excel: {ex.Message}" });
        }

        using (workbook)
        {
            var sheet = workbook.Worksheets.FirstOrDefault();
            if (sheet is null)
            {
                return new DataImportResult(false, 0, 0, new[] { "El archivo no contiene hojas." });
            }

            var firstRow = sheet.FirstRowUsed();
            if (firstRow is null)
            {
                return new DataImportResult(false, 0, 0, new[] { "El archivo esta vacio." });
            }

            // Indexar headers por nombre normalizado -> ColumnAddress
            var headerMap = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var cell in firstRow.CellsUsed())
            {
                var key = NormalizeHeader(cell.GetString());
                if (!string.IsNullOrEmpty(key) && !headerMap.ContainsKey(key))
                {
                    headerMap[key] = cell.Address.ColumnNumber;
                }
            }

            // Mapear: cada columna del modelo -> indice de columna en el Excel (o -1).
            var colMap = new Dictionary<Guid, int>();
            var missing = new List<string>();
            foreach (var col in columns)
            {
                var key = NormalizeHeader(col.Name);
                if (headerMap.TryGetValue(key, out var idx))
                {
                    colMap[col.Id] = idx;
                }
                else
                {
                    missing.Add(col.Name);
                }
            }
            if (missing.Count > 0)
            {
                return new DataImportResult(false, 0, 0, new[]
                {
                    "El Excel no contiene las siguientes columnas requeridas por el modelo: " + string.Join(", ", missing)
                });
            }

            var imported = 0;
            var failed = 0;
            var errors = new List<string>();
            var lastRow = sheet.LastRowUsed();
            var startRow = firstRow.RowNumber() + 1;
            var endRow = lastRow?.RowNumber() ?? startRow - 1;

            for (var rowNumber = startRow; rowNumber <= endRow; rowNumber++)
            {
                var xlRow = sheet.Row(rowNumber);
                if (xlRow.IsEmpty()) { continue; }

                var rowValues = new Dictionary<Guid, string?>();
                string? rowError = null;

                foreach (var col in columns)
                {
                    if (!colMap.TryGetValue(col.Id, out var colIndex)) { continue; }
                    var cell = xlRow.Cell(colIndex);
                    var raw = ExtractValue(cell, col.Type);
                    if (col.IsRequired && string.IsNullOrWhiteSpace(raw))
                    {
                        rowError = $"Fila {rowNumber}: la columna obligatoria '{col.Name}' esta vacia.";
                        break;
                    }
                    rowValues[col.Id] = raw;
                }

                if (rowError is not null)
                {
                    failed++;
                    if (errors.Count < 20) { errors.Add(rowError); }
                    continue;
                }

                var row = new DataContainerRow
                {
                    TenantId = tenantId,
                    ContainerId = containerId
                };
                _db.DataContainerRows.Add(row);
                foreach (var kv in rowValues)
                {
                    _db.DataContainerCells.Add(new DataContainerCell
                    {
                        TenantId = tenantId,
                        RowId = row.Id,
                        ColumnId = kv.Key,
                        Value = kv.Value
                    });
                }
                imported++;
            }

            _audit.Write(actorUserId, "datacontainer.import", nameof(DataContainer), containerId,
                previousValue: null, newValue: new { Imported = imported, Failed = failed }, tenantId: tenantId);
            await _db.SaveChangesAsync(ct);

            return new DataImportResult(imported > 0 || failed == 0, imported, failed, errors);
        }
    }

    private static DataContainerColumnDto MapColumn(DataContainerColumn c) =>
        new(c.Id, c.Name, c.Description, c.Type, c.SortOrder, c.IsRequired);

    private static string NormalizeHeader(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) { return ""; }
        var lower = raw.Trim().ToLowerInvariant();
        // Remove diacritics.
        var normalized = lower.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(normalized.Length);
        foreach (var ch in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
            {
                sb.Append(ch);
            }
        }
        var noDiacritics = sb.ToString().Normalize(NormalizationForm.FormC);
        // Collapse whitespace.
        var collapsed = new StringBuilder(noDiacritics.Length);
        var lastWasSpace = false;
        foreach (var ch in noDiacritics)
        {
            if (char.IsWhiteSpace(ch))
            {
                if (!lastWasSpace && collapsed.Length > 0)
                {
                    collapsed.Append(' ');
                    lastWasSpace = true;
                }
            }
            else
            {
                collapsed.Append(ch);
                lastWasSpace = false;
            }
        }
        return collapsed.ToString().Trim();
    }

    private static string? ExtractValue(IXLCell cell, DataColumnType type)
    {
        if (cell is null || cell.IsEmpty()) { return null; }
        try
        {
            switch (type)
            {
                case DataColumnType.Date:
                    if (cell.DataType == XLDataType.DateTime)
                    {
                        return cell.GetDateTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                    }
                    var raw = cell.GetString().Trim();
                    if (DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt1))
                    {
                        return dt1.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                    }
                    if (DateTime.TryParse(raw, CultureInfo.CurrentCulture, DateTimeStyles.None, out var dt2))
                    {
                        return dt2.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                    }
                    return raw;
                case DataColumnType.Number:
                    if (cell.DataType == XLDataType.Number)
                    {
                        return ((long)cell.GetDouble()).ToString(CultureInfo.InvariantCulture);
                    }
                    return cell.GetString().Trim();
                case DataColumnType.Decimal:
                    if (cell.DataType == XLDataType.Number)
                    {
                        return cell.GetDouble().ToString(CultureInfo.InvariantCulture);
                    }
                    return cell.GetString().Trim();
                case DataColumnType.Boolean:
                    if (cell.DataType == XLDataType.Boolean)
                    {
                        return cell.GetBoolean() ? "true" : "false";
                    }
                    var bs = cell.GetString().Trim().ToLowerInvariant();
                    if (bs is "true" or "1" or "si" or "yes") { return "true"; }
                    if (bs is "false" or "0" or "no") { return "false"; }
                    return bs;
                case DataColumnType.Text:
                default:
                    return cell.GetString().Trim();
            }
        }
        catch
        {
            try { return cell.GetString().Trim(); } catch { return null; }
        }
    }
}
