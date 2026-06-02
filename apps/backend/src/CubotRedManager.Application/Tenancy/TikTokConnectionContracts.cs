namespace CubotRedManager.Application.Tenancy;

/// <summary>Vista de la config de la app de TikTok del tenant (sin exponer el secret).</summary>
public sealed record TikTokAppConfigDto(
    string ClientKey,
    bool HasSecret,
    string RedirectUri,
    string Scope);

/// <summary>Datos para guardar la config de la app de TikTok. Secret vacio = no cambiar.</summary>
public sealed record SaveTikTokAppConfigRequest(
    string ClientKey,
    string? ClientSecret,
    string RedirectUri,
    string Scope);

/// <summary>Resultado de una operacion OAuth para mostrar al operador (con traza, sin secretos).</summary>
public sealed record TikTokOpResult(bool Success, string Trace, string? Error, SocialAccountDto? Account);

/// <summary>Un check individual del diagnostico de configuracion TikTok.</summary>
public sealed record TikTokConfigCheck(string Code, string Label, bool Ok, string Detail);

/// <summary>Resultado completo del diagnostico (todos los checks ejecutados).</summary>
public sealed record TikTokValidationResult(bool OverallOk, IReadOnlyList<TikTokConfigCheck> Checks);

/// <summary>
/// Conexion guiada de cuentas de TikTok via OAuth oficial (Modulo 2.2). Maneja la config de la app
/// del tenant, genera la URL de autorizacion, canjea el auth_code y renueva tokens. Los tokens se
/// persisten cifrados en SocialAccount; el open_id se guarda como ExternalId (= BusinessId).
/// </summary>
public interface ITikTokConnectionService
{
    Task<TikTokAppConfigDto?> GetAppConfigAsync(CancellationToken cancellationToken = default);
    Task<TikTokAppConfigDto> SaveAppConfigAsync(SaveTikTokAppConfigRequest request, Guid actorUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Ejecuta una bateria de checks para validar la configuracion ANTES de pelearse con auth_codes.
    /// Valida: campos completos, Redirect URI HTTPS, scopes conocidos, conectividad y -- el mas importante --
    /// que la pareja client_key/client_secret sea aceptada por TikTok (sondeo via refresh con dummy).
    /// </summary>
    Task<TikTokValidationResult> ValidateConfigAsync(CancellationToken cancellationToken = default);

    /// <summary>Genera la URL de autorizacion y devuelve (url, state). Requiere config de app.</summary>
    Task<(string? Url, string State, string? Error)> BuildAuthorizeUrlAsync(CancellationToken cancellationToken = default);

    /// <summary>Canjea el auth_code y crea/actualiza la cuenta social del cliente indicado.</summary>
    Task<TikTokOpResult> ExchangeCodeAsync(Guid clientId, string authCode, Guid actorUserId, CancellationToken cancellationToken = default);

    /// <summary>Renueva el token de una cuenta de TikTok ya conectada.</summary>
    Task<TikTokOpResult> RefreshAccountAsync(Guid accountId, Guid actorUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Llama a /v2/user/info/ y rellena handle/display_name/avatar_url/bio en la cuenta. Util cuando
    /// el canje OAuth no devolvio user info (early Content Posting) o para refrescar manualmente.
    /// </summary>
    Task<TikTokOpResult> RefreshProfileAsync(Guid accountId, Guid actorUserId, CancellationToken cancellationToken = default);
}
