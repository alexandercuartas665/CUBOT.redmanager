using CubotRedManager.Domain.Common;

namespace CubotRedManager.Domain.Entities;

/// <summary>
/// Asignacion de un operador (TenantUser) a un cliente (marca). Un Operator solo ve los
/// clientes a los que esta asignado, ademas del filtro global por tenant.
/// </summary>
public class UserClientLink : TenantEntity
{
    public Guid TenantUserId { get; set; }
    public TenantUser? TenantUser { get; set; }

    public Guid ClientId { get; set; }
    public Client? Client { get; set; }

    public DateTimeOffset AssignedAt { get; set; }
}
