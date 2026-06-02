using System.Text;
using System.Text.Json;
using CubotRedManager.Application.Abstractions;

namespace CubotRedManager.Infrastructure.Social;

/// <summary>
/// Proveedor OAuth de TikTok for Business. Porta fielmente la logica del control VB.NET de
/// referencia (ctrlCuentasSociales.ascx.vb):
///  - authorize:  https://www.tiktok.com/v2/auth/authorize
///  - canje en cascada: /tt_user/oauth2/token/ (JSON) -> fallback /oauth2/access_token/ (JSON)
///  - refresh en cascada: open.tiktokapis.com/v2/oauth/token/ (form) -> /tt_user/oauth2/token/ (JSON)
///       -> /oauth2/refresh_token/ (JSON)
/// La respuesta de TikTok Business usa { "code": 0, "data": { ... } }; code != 0 = error.
/// Nunca se loggea el access/refresh token (la traza solo describe pasos, no secretos).
/// </summary>
public sealed class TikTokOAuthProvider : ISocialOAuthProvider
{
    private const string AuthorizeBase = "https://www.tiktok.com/v2/auth/authorize";
    private const string ExchangePrimary = "https://business-api.tiktok.com/open_api/v1.3/tt_user/oauth2/token/";
    private const string ExchangeFallback = "https://business-api.tiktok.com/open_api/v1.3/oauth2/access_token/";
    private const string RefreshV2 = "https://open.tiktokapis.com/v2/oauth/token/";
    private const string RefreshBiz = "https://business-api.tiktok.com/open_api/v1.3/tt_user/oauth2/token/";
    private const string RefreshBiz2 = "https://business-api.tiktok.com/open_api/v1.3/oauth2/refresh_token/";

    private readonly HttpClient _http;

    public TikTokOAuthProvider(HttpClient http)
    {
        _http = http;
    }

    public string NetworkCode => "tiktok";

    /// <summary>
    /// Catalogo de scopes conocidos de TikTok Business/Login Kit. Se usa para validacion estatica
    /// (advertir scopes typos antes del flujo OAuth). Lista derivada de docs publicas de TikTok.
    /// </summary>
    public IReadOnlySet<string> KnownScopes { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "user.info.basic", "user.info.profile", "user.info.username", "user.info.stats",
        "biz.creator.info", "biz.creator.insights",
        "video.list", "video.publish", "video.upload",
        "comment.list", "comment.list.manage",
        "research.adlib.basic", "research.data.basic",
        "tto.campaign.link"
    };

    public string BuildAuthorizeUrl(string clientKey, string redirectUri, string scope, string state) =>
        AuthorizeBase +
        "?client_key=" + Uri.EscapeDataString(clientKey) +
        "&state=" + Uri.EscapeDataString(state) +
        "&redirect_uri=" + Uri.EscapeDataString(redirectUri) +
        "&scope=" + Uri.EscapeDataString(scope) +
        "&response_type=code";

    private static string San(string? s) => TokenSanitizer.Sanitize(s) ?? "";

    public async Task<OAuthTokenResult> ExchangeCodeAsync(
        string clientKey, string clientSecret, string redirectUri, string authCode, CancellationToken cancellationToken = default)
    {
        var trace = new StringBuilder();
        try
        {
            // Endpoint primario: /tt_user/oauth2/token/
            trace.AppendLine("[INFO] Canjeando auth_code en /tt_user/oauth2/token/ ...");
            var bodyPrimary = JsonSerializer.Serialize(new
            {
                client_id = clientKey,
                client_secret = clientSecret,
                grant_type = "authorization_code",
                auth_code = authCode,
                redirect_uri = redirectUri
            });
            var respPrimary = await PostJsonAsync(ExchangePrimary, bodyPrimary, cancellationToken);

            // Si trae code != 0, intentar fallback /oauth2/access_token/
            var (okP, dataP, codeP, msgP) = ParseBusiness(respPrimary);
            if (okP)
            {
                trace.AppendLine("[OK] Token obtenido en endpoint primario.");
                return BuildFromData(dataP!.Value, trace.ToString());
            }

            trace.AppendLine($"[INFO] Primario respondio code={codeP} ({San(msgP)}). Probando /oauth2/access_token/ ...");
            var bodyFallback = JsonSerializer.Serialize(new { app_id = clientKey, secret = clientSecret, auth_code = authCode });
            var respFallback = await PostJsonAsync(ExchangeFallback, bodyFallback, cancellationToken);
            var (okF, dataF, codeF, msgF) = ParseBusiness(respFallback);
            if (okF)
            {
                trace.AppendLine("[OK] Token obtenido en endpoint fallback.");
                return BuildFromData(dataF!.Value, trace.ToString());
            }

            trace.AppendLine($"[ERROR] Fallback respondio code={codeF}: {San(msgF)}");
            return new OAuthTokenResult(false, null, null, null, null, San(trace.ToString())!, San(msgF) ?? San(msgP) ?? "Error desconocido");
        }
        catch (Exception ex)
        {
            trace.AppendLine("[ERROR] " + San(ex.Message));
            return new OAuthTokenResult(false, null, null, null, null, San(trace.ToString())!, San(ex.Message));
        }
    }

    public async Task<OAuthTokenResult> RefreshAsync(
        string clientKey, string clientSecret, string refreshToken, CancellationToken cancellationToken = default)
    {
        var trace = new StringBuilder();

        // Intento 1: TikTok v2 (form-encoded) en open.tiktokapis.com
        try
        {
            trace.AppendLine("[INFO] Renovando en v2 (open.tiktokapis.com) ...");
            var form =
                "client_key=" + Uri.EscapeDataString(clientKey) +
                "&client_secret=" + Uri.EscapeDataString(clientSecret) +
                "&grant_type=refresh_token" +
                "&refresh_token=" + Uri.EscapeDataString(refreshToken);
            var respV2 = await PostFormAsync(RefreshV2, form, cancellationToken);
            using var doc = JsonDocument.Parse(respV2);
            var root = doc.RootElement;
            var at = GetString(root, "access_token");
            if (!string.IsNullOrEmpty(at))
            {
                trace.AppendLine("[OK] Token renovado via v2.");
                var rt = GetString(root, "refresh_token");
                return new OAuthTokenResult(true, at, string.IsNullOrEmpty(rt) ? refreshToken : rt,
                    GetString(root, "open_id"), GetInt(root, "expires_in"), trace.ToString(), null);
            }
        }
        catch (Exception ex)
        {
            trace.AppendLine("[INFO] v2 fallo: " + San(ex.Message) + ". Probando Business API ...");
        }

        // Intento 2: Business API /tt_user/oauth2/token/
        try
        {
            trace.AppendLine("[INFO] Renovando en /tt_user/oauth2/token/ ...");
            var bodyBiz = JsonSerializer.Serialize(new
            {
                client_id = clientKey,
                client_secret = clientSecret,
                grant_type = "refresh_token",
                refresh_token = refreshToken
            });
            var respBiz = await PostJsonAsync(RefreshBiz, bodyBiz, cancellationToken);
            var (okB, dataB, codeB, msgB) = ParseBusiness(respBiz);
            if (okB)
            {
                trace.AppendLine("[OK] Token renovado via Business API.");
                return BuildFromData(dataB!.Value, trace.ToString(), fallbackRefresh: refreshToken);
            }
            trace.AppendLine($"[INFO] /tt_user respondio code={codeB} ({San(msgB)}). Probando /oauth2/refresh_token/ ...");

            // Intento 3: /oauth2/refresh_token/
            var bodyBiz2 = JsonSerializer.Serialize(new { app_id = clientKey, secret = clientSecret, refresh_token = refreshToken });
            var respBiz2 = await PostJsonAsync(RefreshBiz2, bodyBiz2, cancellationToken);
            var (okB2, dataB2, codeB2, msgB2) = ParseBusiness(respBiz2);
            if (okB2)
            {
                trace.AppendLine("[OK] Token renovado via /oauth2/refresh_token/.");
                return BuildFromData(dataB2!.Value, trace.ToString(), fallbackRefresh: refreshToken);
            }

            trace.AppendLine($"[ERROR] Todos los endpoints fallaron. Ultimo code={codeB2}: {San(msgB2)}");
            return new OAuthTokenResult(false, null, null, null, null, San(trace.ToString())!, San(msgB2) ?? "Renovacion fallida");
        }
        catch (Exception ex)
        {
            trace.AppendLine("[ERROR] " + San(ex.Message));
            return new OAuthTokenResult(false, null, null, null, null, San(trace.ToString())!, San(ex.Message));
        }
    }

    public async Task<bool> CheckReachabilityAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(8));
            using var req = new HttpRequestMessage(HttpMethod.Get, "https://business-api.tiktok.com/");
            using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cts.Token);
            // Cualquier respuesta HTTP (incluso 404) prueba que el dominio esta vivo y la red llega.
            return (int)resp.StatusCode > 0;
        }
        catch { return false; }
    }

    /// <summary>
    /// Sondea credenciales llamando a refresh con un refresh_token dummy. TikTok devuelve:
    ///  - error="invalid_client" o similar  -> client_key/secret invalidos
    ///  - error="invalid_grant"/"invalid_request" sobre el refresh -> credenciales OK, solo el refresh es invalido
    ///  - code != 0 con mensaje sobre app_id/secret -> credenciales invalidas
    /// Esto valida la pareja sin necesitar que el usuario autorice manualmente.
    /// </summary>
    public async Task<OAuthCredentialsProbe> ProbeCredentialsAsync(
        string clientKey, string clientSecret, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(clientKey) || string.IsNullOrWhiteSpace(clientSecret))
        {
            return new OAuthCredentialsProbe(false, "missing", "App Key o App Secret vacios.");
        }

        // Intento A: TikTok v2 OAuth (form). Da el codigo de error mas claro.
        try
        {
            var form =
                "client_key=" + Uri.EscapeDataString(clientKey) +
                "&client_secret=" + Uri.EscapeDataString(clientSecret) +
                "&grant_type=refresh_token" +
                "&refresh_token=cubot-probe-invalid-token";
            var body = await PostFormAsync(RefreshV2, form, cancellationToken);
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            var error = GetString(root, "error") ?? GetString(root, "error_code");
            if (!string.IsNullOrEmpty(error))
            {
                var description = GetString(root, "error_description") ?? "";
                // invalid_client / invalid_client_id / invalid_secret = credenciales mal
                if (error.Contains("client", StringComparison.OrdinalIgnoreCase) ||
                    error.Contains("secret", StringComparison.OrdinalIgnoreCase))
                {
                    return new OAuthCredentialsProbe(false, error, $"TikTok rechazo la pareja App Key/Secret: {description}");
                }
                // invalid_grant / invalid_request = credenciales OK (solo el refresh es invalido)
                return new OAuthCredentialsProbe(true, error, "TikTok acepto las credenciales (rechazo el refresh_token de prueba, que era el resultado esperado).");
            }
            // Si responde sin error, algo raro
            return new OAuthCredentialsProbe(true, null, "TikTok respondio sin error (inesperado).");
        }
        catch (Exception ex)
        {
            return new OAuthCredentialsProbe(false, "network", "No se pudo verificar: " + ex.Message);
        }
    }

    // --- Helpers de parseo (tolerantes a campos faltantes) ---

    /// <summary>Parsea respuesta Business { code, data, message }. ok = code == 0 y hay data.access_token.</summary>
    private static (bool ok, JsonElement? data, int code, string? message) ParseBusiness(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            var code = GetInt(root, "code") ?? -1;
            var message = GetString(root, "message");
            if (code == 0 && root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object
                && !string.IsNullOrEmpty(GetString(data, "access_token")))
            {
                // Clonar el elemento para usarlo fuera del using.
                return (true, data.Clone(), 0, message);
            }
            return (false, null, code, message);
        }
        catch (Exception ex)
        {
            return (false, null, -1, ex.Message);
        }
    }

    private static OAuthTokenResult BuildFromData(JsonElement data, string trace, string? fallbackRefresh = null)
    {
        var at = GetString(data, "access_token");
        var rt = GetString(data, "refresh_token");
        return new OAuthTokenResult(
            true,
            at,
            string.IsNullOrEmpty(rt) ? fallbackRefresh : rt,
            GetString(data, "open_id"),
            GetInt(data, "expires_in"),
            trace,
            null);
    }

    private static string? GetString(JsonElement el, string name)
    {
        if (el.ValueKind == JsonValueKind.Object && el.TryGetProperty(name, out var p))
        {
            return p.ValueKind switch
            {
                JsonValueKind.String => p.GetString(),
                JsonValueKind.Number => p.ToString(),
                _ => null
            };
        }
        return null;
    }

    private static int? GetInt(JsonElement el, string name)
    {
        if (el.ValueKind == JsonValueKind.Object && el.TryGetProperty(name, out var p))
        {
            if (p.ValueKind == JsonValueKind.Number && p.TryGetInt32(out var n)) { return n; }
            if (p.ValueKind == JsonValueKind.String && int.TryParse(p.GetString(), out var s)) { return s; }
        }
        return null;
    }

    private async Task<string> PostJsonAsync(string url, string json, CancellationToken ct)
    {
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        return await PostAsync(url, content, ct);
    }

    private async Task<string> PostFormAsync(string url, string form, CancellationToken ct)
    {
        using var content = new StringContent(form, Encoding.UTF8, "application/x-www-form-urlencoded");
        return await PostAsync(url, content, ct);
    }

    private async Task<string> PostAsync(string url, HttpContent content, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };
        req.Headers.TryAddWithoutValidation("Accept", "application/json");
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(30));
        // Devuelve el cuerpo aunque sea error HTTP (TikTok manda el detalle en el body).
        using var resp = await _http.SendAsync(req, cts.Token);
        return await resp.Content.ReadAsStringAsync(cts.Token);
    }
}
