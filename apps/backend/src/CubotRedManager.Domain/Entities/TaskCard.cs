using CubotRedManager.Domain.Common;
using CubotRedManager.Domain.Enums;

namespace CubotRedManager.Domain.Entities;

/// <summary>Tarjeta (tarea) del tablero Kanban (Modulo 2.7). Puede enlazar a publicacion o cuenta social.</summary>
public class TaskCard : TenantEntity
{
    public Guid BoardId { get; set; }
    public Guid ColumnId { get; set; }

    public string Title { get; set; } = null!;
    public string? DescriptionMd { get; set; }
    public string? TagsJson { get; set; }
    public DateOnly? DueDate { get; set; }
    public TaskPriority Priority { get; set; } = TaskPriority.Normal;

    public Guid? RelatedPublicationId { get; set; }
    public Guid? RelatedSocialAccountId { get; set; }

    public int SortOrder { get; set; }
}
