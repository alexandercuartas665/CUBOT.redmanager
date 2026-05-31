using CubotRedManager.Domain.Common;
using CubotRedManager.Domain.Enums;

namespace CubotRedManager.Domain.Entities;

/// <summary>
/// Membresia de una identidad (PlatformUser) dentro de una agencia (tenant) con un rol.
/// Tenant-scoped: el filtro global por TenantId aplica.
/// </summary>
public class TenantUser : TenantEntity
{
    public Guid PlatformUserId { get; set; }
    public PlatformUser? PlatformUser { get; set; }

    public TenantRole TenantRole { get; set; } = TenantRole.Operator;
    public PlatformUserStatus Status { get; set; } = PlatformUserStatus.Invited;

    public string? InvitationToken { get; set; }
    public DateTimeOffset? InvitationExpiresAt { get; set; }

    public ICollection<UserClientLink> ClientLinks { get; set; } = new List<UserClientLink>();
}
