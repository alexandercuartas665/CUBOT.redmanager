using CubotRedManager.Domain.Common;
using CubotRedManager.Domain.Enums;

namespace CubotRedManager.Domain.Entities;

/// <summary>
/// Columna de un DataContainer. Define el esquema (nombre y tipo) que validan las celdas.
/// </summary>
public class DataContainerColumn : TenantEntity
{
    public Guid ContainerId { get; set; }
    public DataContainer? Container { get; set; }

    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public DataColumnType Type { get; set; } = DataColumnType.Text;
    public int SortOrder { get; set; }
    public bool IsRequired { get; set; }
}
