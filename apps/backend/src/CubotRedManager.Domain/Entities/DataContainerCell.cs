using CubotRedManager.Domain.Common;

namespace CubotRedManager.Domain.Entities;

/// <summary>
/// Celda EAV: valor de una columna en una fila concreta. El valor se persiste siempre
/// como string; la conversion al tipo correcto (numero, decimal, fecha, booleano) se
/// realiza al leer segun el DataColumnType de la columna asociada.
/// </summary>
public class DataContainerCell : TenantEntity
{
    public Guid RowId { get; set; }
    public DataContainerRow? Row { get; set; }

    public Guid ColumnId { get; set; }
    public DataContainerColumn? Column { get; set; }

    public string? Value { get; set; }
}
