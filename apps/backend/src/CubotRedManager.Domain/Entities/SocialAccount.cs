using CubotRedManager.Domain.Common;
using CubotRedManager.Domain.Enums;

namespace CubotRedManager.Domain.Entities;

/// <summary>
/// Cuenta social conectada por cliente y red (Modulo 2.3). Tenant-scoped. Tokens cifrados con
/// DataProtection (jamas en claro ni en logs). Constraint: unica por (tenant, client, network, external_id).
/// </summary>
public class SocialAccount : TenantEntity
{
    public Guid ClientId { get; set; }
    public Client? Client { get; set; }

    public string NetworkCode { get; set; } = null!;
    public string ExternalId { get; set; } = "";
    public string? Handle { get; set; }
    public string? DisplayName { get; set; }
    public string? AvatarUrl { get; set; }

    /// <summary>Access token cifrado. JAMAS en logs.</summary>
    public string? AccessTokenEncrypted { get; set; }
    public string? RefreshTokenEncrypted { get; set; }
    public string? TokenScope { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }

    public SocialAccountStatus Status { get; set; } = SocialAccountStatus.Disconnected;
    public DateTimeOffset? LastSyncAt { get; set; }
    public string? LastSyncError { get; set; }

    /// <summary>Numero de seguidores conocido (snapshot del ultimo sync). Para KPIs.</summary>
    public long? FollowersCount { get; set; }
    /// <summary>Bio o descripcion publica de la cuenta.</summary>
    public string? Bio { get; set; }
}
