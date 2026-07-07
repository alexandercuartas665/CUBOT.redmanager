using CubotRedManager.Domain.Enums;

namespace CubotRedManager.Application.Tenancy;

public sealed record DataContainerDto(
    Guid Id,
    string Name,
    string? Description,
    int ColumnCount,
    int RowCount,
    DateTimeOffset UpdatedAt);

public sealed record DataContainerColumnDto(
    Guid Id,
    string Name,
    string? Description,
    DataColumnType Type,
    int SortOrder,
    bool IsRequired);

public sealed record DataContainerDetailDto(
    Guid Id,
    string Name,
    string? Description,
    IReadOnlyList<DataContainerColumnDto> Columns);

public sealed record DataContainerRowDto(
    Guid Id,
    DateTimeOffset CreatedAt,
    Dictionary<Guid, string?> ValuesByColumnId);

public sealed record SaveDataColumnInput(
    Guid? Id,
    string Name,
    string? Description,
    DataColumnType Type,
    int SortOrder,
    bool IsRequired);

public sealed record SaveDataContainerRequest(
    Guid? Id,
    string Name,
    string? Description,
    IReadOnlyList<SaveDataColumnInput> Columns);

public sealed record SaveDataRowRequest(
    Guid ContainerId,
    Guid? RowId,
    Dictionary<Guid, string?> ValuesByColumnId);

public sealed record DataImportResult(
    bool Success,
    int RowsImported,
    int RowsFailed,
    IReadOnlyList<string> Errors);

/// <summary>
/// Contenedor de Datos: modelos (tablas) dinamicos creados por el operador y sus filas.
/// EAV tenant-scoped. Permite importar datos desde Excel.
/// </summary>
public interface IDataContainerService
{
    Task<IReadOnlyList<DataContainerDto>> ListAsync(CancellationToken ct = default);
    Task<DataContainerDetailDto?> GetAsync(Guid id, CancellationToken ct = default);
    Task<DataContainerDetailDto?> SaveAsync(SaveDataContainerRequest req, Guid actorUserId, CancellationToken ct = default);
    Task<bool> DeleteAsync(Guid id, Guid actorUserId, CancellationToken ct = default);
    Task<IReadOnlyList<DataContainerRowDto>> ListRowsAsync(Guid containerId, string? search = null, int take = 500, CancellationToken ct = default);
    Task<DataContainerRowDto?> SaveRowAsync(SaveDataRowRequest req, Guid actorUserId, CancellationToken ct = default);
    Task<bool> DeleteRowAsync(Guid rowId, Guid actorUserId, CancellationToken ct = default);
    Task<DataImportResult> ImportFromExcelAsync(Guid containerId, Stream xlsxStream, Guid actorUserId, CancellationToken ct = default);

    /// <summary>Genera un xlsx con las columnas del contenedor como encabezados y una fila por
    /// cada registro. El formato producido es re-importable con <see cref="ImportFromExcelAsync"/>.
    /// Devuelve null si el contenedor no existe o no hay tenant activo.</summary>
    Task<DataExportResult?> ExportToExcelAsync(Guid containerId, CancellationToken ct = default);
}

public sealed record DataExportResult(string FileName, byte[] Bytes);
