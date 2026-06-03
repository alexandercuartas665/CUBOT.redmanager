using CubotRedManager.Domain.Common;

namespace CubotRedManager.Domain.Entities;

/// <summary>
/// Asignacion de un usuario del tenant a una tarjeta. Una tarjeta puede tener varios asignados
/// (miembros responsables de avanzarla). Entidad TENANT-SCOPED.
/// </summary>
public class TaskCardAssignment : TenantEntity
{
    public Guid TaskCardId { get; set; }
    public TaskCard? TaskCard { get; set; }

    /// <summary>Usuario del tenant asignado a la tarjeta (referencia a TenantUser, no a PlatformUser).</summary>
    public Guid TenantUserId { get; set; }
    public TenantUser? TenantUser { get; set; }
}
