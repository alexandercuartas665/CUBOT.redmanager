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

    /// <summary>Fallos consecutivos del refresh de token. Se resetea a 0 cuando un refresh termina
    /// bien. Cuando llega a un umbral (3) el servicio marca la cuenta como Expired aunque el
    /// access_token en DB parezca vivo — evita el false positive de "Conectada" con refresh_token
    /// invalidado por TikTok pero access_token nominalmente vigente.</summary>
    public int RefreshFailureCount { get; set; }

    /// <summary>Solo TikTok: familia de endpoints OAuth con la que se canjeo el token. Determina
    /// el endpoint de refresh (evita la cascada que corrompe el refresh_token). Default BusinessV13
    /// para cuentas historicas (que es lo que el sistema usaba antes de este campo).</summary>
    public TikTokOAuthFlavor OAuthFlavor { get; set; } = TikTokOAuthFlavor.BusinessV13;

    /// <summary>Timestamp del ultimo aviso por WhatsApp al admin del tenant sobre un refresh
    /// fallido de esta cuenta. Nulo si nunca se ha avisado. Sirve para evitar spam (1 aviso por dia).</summary>
    public DateTimeOffset? LastRefreshFailureNotifiedAt { get; set; }

    /// <summary>Numero de seguidores conocido (snapshot del ultimo sync). Para KPIs.</summary>
    public long? FollowersCount { get; set; }
    /// <summary>Bio o descripcion publica de la cuenta.</summary>
    public string? Bio { get; set; }
}
