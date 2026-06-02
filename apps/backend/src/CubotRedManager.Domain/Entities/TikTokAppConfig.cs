using CubotRedManager.Domain.Common;

namespace CubotRedManager.Domain.Entities;

/// <summary>
/// Credenciales de la app de TikTok Business registradas por la agencia (tenant). Una por tenant.
/// El secret se guarda cifrado con DataProtection (JAMAS en claro ni en logs). Equivale a los
/// campos CLIENT_KEY / CLIENT_SECRET_ENC / REDIRECT_URI / OAUTH_SCOPE del control VB.NET de
/// referencia (ctrlCuentasSociales). Origen del flujo OAuth de TikTok del Modulo 2.2.
/// </summary>
public class TikTokAppConfig : TenantEntity
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
