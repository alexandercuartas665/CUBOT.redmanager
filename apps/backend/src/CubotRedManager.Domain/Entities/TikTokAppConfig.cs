using CubotRedManager.Domain.Common;

namespace CubotRedManager.Domain.Entities;

/// <summary>
/// Credenciales de la app de TikTok Business del SaaS. Entidad GLOBAL de plataforma (singleton):
/// UNA sola app de TikTok registrada por CUBOT.redmanager sirve el OAuth de todas las agencias.
/// La administra el Super Admin (patron Buffer/Hootsuite/Later); las agencias solo conectan cuentas.
///
/// NO hereda de TenantEntity ni implementa ITenantScoped: no lleva TenantId ni HasQueryFilter.
/// El secret se guarda cifrado con DataProtection (JAMAS en claro ni en logs).
/// </summary>
public class TikTokAppConfig : BaseEntity
{
    /// <summary>client_key (App Key) de la app de TikTok for Business.</summary>
    public string ClientKey { get; set; } = "";

    /// <summary>client_secret (App Secret) cifrado. JAMAS en logs.</summary>
    public string? ClientSecretEncrypted { get; set; }

    /// <summary>URI de redireccion registrada en el portal de TikTok.</summary>
    public string RedirectUri { get; set; } = "";

    /// <summary>Scopes solicitados, separados por coma.</summary>
    public string Scope { get; set; } = "user.info.basic,biz.creator.info,biz.creator.insights,video.list";
}
