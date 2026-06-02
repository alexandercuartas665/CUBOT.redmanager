using CubotRedManager.Domain.Common;

namespace CubotRedManager.Domain.Entities;

/// <summary>
/// Fila de datos de un DataContainer. Sus valores viven en DataContainerCell (modelo EAV).
/// </summary>
public class DataContainerRow : TenantEntity
{
    public Guid ContainerId { get; set; }
    public DataContainer? Container { get; set; }

    public ICollection<DataContainerCell> Cells { get; set; } = new List<DataContainerCell>();
}
