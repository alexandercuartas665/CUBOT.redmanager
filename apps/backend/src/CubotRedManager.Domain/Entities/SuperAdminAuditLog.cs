using CubotRedManager.Domain.Common;
using CubotRedManager.Domain.Enums;

namespace CubotRedManager.Domain.Entities;

/// <summary>
/// Registro de auditoria de acciones sensibles del Super Admin / sistema. PreviousValue/NewValue
/// se almacenan como jsonb. Entidad global.
/// </summary>
public class SuperAdminAuditLog : BaseEntity
{
    public Guid ActorUserId { get; set; }
    public AuditActorType ActorType { get; set; } = AuditActorType.Human;
    public string ActionName { get; set; } = null!;
    public string EntityName { get; set; } = null!;
    public Guid? EntityId { get; set; }
    public Guid? TenantId { get; set; }
    public string? PreviousValue { get; set; }
    public string? NewValue { get; set; }
    public string? Reason { get; set; }
    public string? IpAddress { get; set; }
}
