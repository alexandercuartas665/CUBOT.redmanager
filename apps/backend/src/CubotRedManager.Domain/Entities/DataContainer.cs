using CubotRedManager.Domain.Common;

namespace CubotRedManager.Domain.Entities;

/// <summary>
/// Modelo de datos dinamico (tabla logica) creado por el operador. Tenant-scoped.
/// Diseno EAV: las filas (DataContainerRow) y celdas (DataContainerCell) almacenan
/// los datos respetando el esquema definido por DataContainerColumn.
/// </summary>
public class DataContainer : TenantEntity
{
    public string Name { get; set; } = "";
    public string? Description { get; set; }

    public ICollection<DataContainerColumn> Columns { get; set; } = new List<DataContainerColumn>();
    public ICollection<DataContainerRow> Rows { get; set; } = new List<DataContainerRow>();
}
