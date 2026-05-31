using CubotRedManager.Domain.Common;

namespace CubotRedManager.Domain.Entities;

/// <summary>Columna de un tablero Kanban (configurable por tablero). Modulo 2.7.</summary>
public class TaskColumn : TenantEntity
{
    public Guid BoardId { get; set; }
    public TaskBoard? Board { get; set; }

    public string Name { get; set; } = null!;
    public int SortOrder { get; set; }
    public string ColorHex { get; set; } = "#A03DC9";

    public ICollection<TaskCard> Cards { get; set; } = new List<TaskCard>();
}
