using CubotRedManager.Domain.Common;
using CubotRedManager.Domain.Enums;

namespace CubotRedManager.Domain.Entities;

/// <summary>Tarea de seguimiento/recordatorio asociada a un lead. Tenant-scoped. Alertas por
/// inactividad quedaran para una fase posterior (worker + notificaciones).</summary>
public class FollowUpTask : TenantEntity
{
    public Guid LeadId { get; set; }
    public Lead? Lead { get; set; }
    public string Title { get; set; } = null!;
    public string? Notes { get; set; }
    public DateTimeOffset DueAt { get; set; }
    public FollowUpTaskStatus Status { get; set; } = FollowUpTaskStatus.Pending;
    public Guid? AssignedToTenantUserId { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}
