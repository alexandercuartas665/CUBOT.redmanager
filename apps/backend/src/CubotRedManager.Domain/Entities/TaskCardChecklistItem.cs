using CubotRedManager.Domain.Common;

namespace CubotRedManager.Domain.Entities;

/// <summary>Item del checklist de una tarjeta. TENANT-SCOPED.</summary>
public class TaskCardChecklistItem : TenantEntity
{
    public Guid TaskCardId { get; set; }
    public TaskCard? TaskCard { get; set; }

    public string Text { get; set; } = null!;

    public bool IsCompleted { get; set; }

    /// <summary>Cuando se marco como completado (si aplica).</summary>
    public DateTimeOffset? CompletedAt { get; set; }

    /// <summary>Quien lo completo (PlatformUser id, no TenantUser, asi sirve para Super Admin).</summary>
    public Guid? CompletedBy { get; set; }

    public int SortOrder { get; set; }
}
