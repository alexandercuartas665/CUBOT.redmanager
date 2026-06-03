using CubotRedManager.Domain.Common;

namespace CubotRedManager.Domain.Entities;

/// <summary>
/// Tarjeta (tarea) en el tablero Kanban del tenant. Una tarjeta vive en una columna y se mueve
/// entre columnas durante su ciclo de vida. Entidad TENANT-SCOPED.
/// </summary>
public class TaskCard : TenantEntity
{
    public Guid BoardId { get; set; }
    public TaskBoard? Board { get; set; }

    public Guid ColumnId { get; set; }
    public TaskBoardColumn? Column { get; set; }

    /// <summary>Titulo corto de la tarea (mostrado en la tarjeta del Kanban).</summary>
    public string Title { get; set; } = null!;

    /// <summary>Descripcion detallada (markdown libre).</summary>
    public string? Description { get; set; }

    /// <summary>Fecha de vencimiento. Si pasa y la tarjeta no esta en columna IsDone, se marca atrasada.</summary>
    public DateTimeOffset? DueAt { get; set; }

    /// <summary>Orden vertical dentro de la columna (0 arriba).</summary>
    public int SortOrder { get; set; }

    /// <summary>Tarjetas archivadas no se ven en el tablero por defecto pero conservan historia.</summary>
    public bool IsArchived { get; set; }

    /// <summary>Color HEX para acentuar el titulo de la tarjeta. Null = sin color especifico.</summary>
    public string? Color { get; set; }
}
