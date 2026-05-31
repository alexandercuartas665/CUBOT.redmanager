using CubotRedManager.Domain.Common;
using CubotRedManager.Domain.Enums;

namespace CubotRedManager.Domain.Entities;

/// <summary>
/// Identidad global de una persona. Entidad GLOBAL (no tenant-scoped). Una misma
/// identidad puede ser miembro (TenantUser) de varias agencias.
/// </summary>
public class PlatformUser : BaseEntity
{
    public string Email { get; set; } = string.Empty;
    public bool EmailVerified { get; set; }
    public string? DisplayName { get; set; }
    public string? AvatarUrl { get; set; }
    public string? GoogleSubject { get; set; }
    public AuthProvider AuthProvider { get; set; } = AuthProvider.Local;
    public string? PasswordHash { get; set; }

    /// <summary>Rol global. Null = usuario de agencia (sin privilegios de plataforma).</summary>
    public PlatformRole? PlatformRole { get; set; }

    public ICollection<TenantUser> Memberships { get; set; } = new List<TenantUser>();
}
