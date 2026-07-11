namespace CubotRedManager.Application.Abstractions;

/// <summary>Resultado de un canje o renovacion de token OAuth.</summary>
/// <param name="Success">True si se obtuvo un access token valido.</param>
/// <param name="AccessToken">Token de acceso (en claro; el llamador lo cifra antes de persistir).</param>
/// <param name="RefreshToken">Token de refresco, si la red lo entrega.</param>
/// <param name="OpenId">Identificador de la cuenta (open_id en TikTok = BusinessId).</param>
/// <param name="ExpiresInSeconds">Vigencia del access token en segundos, si se conoce.</param>
/// <param name="Trace">Traza tecnica legible (sin secretos) para mostrar al operador.</param>
/// <param name="Error">Mensaje de error si Success es false.</param>
public sealed record OAuthTokenResult(
    bool Success,
    string? AccessToken,
    string? RefreshToken,
    string? OpenId,
    int? ExpiresInSeconds,
    string Trace,
    string? Error,
    /// <summary>Solo TikTok: familia OAuth (0=BusinessV13, 1=OpenV2) del endpoint que emitio el
    /// token. Se persiste en SocialAccount para que futuros refresh usen el endpoint correcto sin
    /// cascada (los dos flavors son incompatibles entre si). Nulo si el proveedor no aplica.</summary>
    int? OAuthFlavor = null,
    /// <summary>URL exacta del endpoint golpeado en el ultimo intento (para persistir en el log).</summary>
    string? EndpointUsed = null,
    /// <summary>Codigo de estado HTTP recibido. 0 si no llego a haber respuesta (excepcion / timeout).</summary>
    int? HttpStatus = null,
    /// <summary>Codigo de negocio de la respuesta (ej. TikTok Business code=X). Nulo si no aplica.</summary>
    string? ResponseCode = null,
    /// <summary>Cuerpo crudo de la respuesta de TikTok, SANITIZADO (sin tokens ni auth_codes),
    /// truncado a 2KB. Sirve para diagnosticar detalles del error (log_id, request_id).</summary>
    string? RawResponse = null,
    /// <summary>Solo relevante para exchange: intentos previos que fallaron antes del que
    /// finalmente triunfo (o antes del fallo total). Cada uno se persiste como fila en el
    /// TokenRefreshLog para diagnostico completo. Nulo si no aplica.</summary>
    IReadOnlyList<OAuthAttemptRecord>? PriorAttempts = null);

/// <summary>Un intento OAuth intermedio (que fallo antes de que otro triunfara). Registrado
/// solo para diagnostico — nunca se toma como resultado.</summary>
public sealed record OAuthAttemptRecord(
    string Endpoint,
    string Flavor,
    int? HttpStatus,
    string? ResponseCode,
    string? ErrorMessage,
    string? RawResponse,
    int DurationMs);

/// <summary>Resultado de un sondeo de credenciales (sin canje real).</summary>
/// <param name="CredentialsOk">true si client_key+secret fueron aceptados por el endpoint.</param>
/// <param name="ProviderErrorCode">Codigo de error del proveedor (ej. "invalid_client", "invalid_grant").</param>
/// <param name="Detail">Mensaje legible para el operador (sin secretos).</param>
public sealed record OAuthCredentialsProbe(bool CredentialsOk, string? ProviderErrorCode, string Detail);

/// <summary>
/// Proveedor OAuth de una red social. Encapsula la cascada de endpoints (canje/refresh) y la
/// tolerancia de campos. La implementacion concreta (p.ej. TikTok) vive en Infrastructure.
/// </summary>
public interface ISocialOAuthProvider
{
    /// <summary>Codigo de red que atiende este proveedor (ej. "tiktok").</summary>
    string NetworkCode { get; }

    /// <summary>Construye la URL de autorizacion (authorize) con state aleatorio.</summary>
    string BuildAuthorizeUrl(string clientKey, string redirectUri, string scope, string state);

    /// <summary>Canjea el auth_code por un access token (cascada de endpoints).</summary>
    Task<OAuthTokenResult> ExchangeCodeAsync(
        string clientKey, string clientSecret, string redirectUri, string authCode, CancellationToken cancellationToken = default);

    /// <summary>Renueva el access token con el refresh token. El caller pasa el flavor guardado
    /// en SocialAccount para dirigir el POST al endpoint correcto (sin cascada). Si el flavor es
    /// null (cuentas historicas), se usa el default segun proveedor.</summary>
    Task<OAuthTokenResult> RefreshAsync(
        string clientKey, string clientSecret, string refreshToken, int? oauthFlavor = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sondea las credenciales (client_key + client_secret) sin necesidad de auth_code real.
    /// Lo hace llamando a refresh con un refresh_token dummy: el proveedor distinguira
    /// "credenciales malas" (invalid_client) de "refresh malo" (invalid_grant). Asi sabemos
    /// si la pareja client_key/secret es valida ANTES de pedir al usuario que autorice.
    /// </summary>
    Task<OAuthCredentialsProbe> ProbeCredentialsAsync(
        string clientKey, string clientSecret, CancellationToken cancellationToken = default);

    /// <summary>Verifica conectividad basica al dominio del proveedor (HEAD/GET sin auth).</summary>
    Task<bool> CheckReachabilityAsync(CancellationToken cancellationToken = default);

    /// <summary>Catalogo de scopes conocidos del proveedor (para validacion estatica).</summary>
    IReadOnlySet<string> KnownScopes { get; }
}
