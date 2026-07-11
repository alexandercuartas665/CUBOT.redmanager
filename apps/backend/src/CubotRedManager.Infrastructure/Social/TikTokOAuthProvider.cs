using System.Text;
using System.Text.Json;
using CubotRedManager.Application.Abstractions;

namespace CubotRedManager.Infrastructure.Social;

/// <summary>
/// Proveedor OAuth de TikTok for Business.
///
/// **Refresh determinista por flavor (ADR anti-cascade):** el sistema conserva cuando el token
/// original fue emitido por el flujo <b>BusinessV13</b> (business-api.tiktok.com) o por el flujo
/// <b>OpenV2</b> (open.tiktokapis.com). El refresh apunta SIEMPRE al endpoint correcto segun ese
/// flavor. La cascada de 3 endpoints que existia antes causaba dos problemas:
///  1. Golpeaba <c>/tt_user/oauth2/token/</c> con grant_type=refresh_token, endpoint que solo
///     acepta authorization_code y respondia "Missing data for required field. auth_code".
///  2. Cuando el primer endpoint SI aceptaba el refresh y devolvia uno nuevo, la cascada seguia
///     y podia consumir tambien el nuevo, dejando el token invalidado en el proximo ciclo.
///
/// Endpoints:
///  - authorize:            https://www.tiktok.com/v2/auth/authorize
///  - canje BusinessV13:    /tt_user/oauth2/token/ (JSON) -> fallback /oauth2/access_token/ (JSON)
///  - refresh BusinessV13:  /oauth2/refresh_token/ (JSON con app_id + secret + refresh_token)
///  - refresh OpenV2:       open.tiktokapis.com/v2/oauth/token/ (form)
/// La respuesta de TikTok Business usa { "code": 0, "data": { ... } }; code != 0 = error.
/// Nunca se loggea el access/refresh token (la traza solo describe pasos, no secretos).
/// </summary>
public sealed class TikTokOAuthProvider : ISocialOAuthProvider
{
    private const string AuthorizeBase = "https://www.tiktok.com/v2/auth/authorize";
    // Exchange: preferimos SIEMPRE Open v2 (open.tiktokapis.com/v2/oauth/token/) porque casa con
    // el authorize (www.tiktok.com/v2/auth/authorize) y su refresh_token SI es aceptado por el
    // refresh endpoint de Open v2. Si Open v2 rechaza el code (app registrada como Business en
    // developers.tiktok.com), caemos a Business v1.3 como respaldo.
    private const string ExchangeOpenV2 = "https://open.tiktokapis.com/v2/oauth/token/";
    private const string ExchangeBusinessV13 = "https://business-api.tiktok.com/open_api/v1.3/tt_user/oauth2/token/";
    private const string ExchangeBusinessV13Fallback = "https://business-api.tiktok.com/open_api/v1.3/oauth2/access_token/";
    // Refresh: un solo endpoint por flavor. No hay cascada — si el flavor esta mal, se falla y se
    // notifica al operador para que reconecte manualmente. Un fallback silencioso puede invalidar
    // el refresh_token en el server side (rotacion) y dejar la cuenta inrecuperable.
    private const string RefreshOpenV2 = "https://open.tiktokapis.com/v2/oauth/token/";
    private const string RefreshBusinessV13 = "https://business-api.tiktok.com/open_api/v1.3/oauth2/refresh_token/";

    // Marcadores del enum TikTokOAuthFlavor. El proveedor OAuth vive en Infrastructure y no puede
    // referenciar Domain directamente, asi que replicamos los codigos aqui como constantes.
    private const int FlavorBusinessV13 = 0;
    private const int FlavorOpenV2 = 1;

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

    /// <summary>
    /// Sanitiza el body crudo de TikTok para persistirlo como diagnostico:
    /// 1) Aplica TokenSanitizer (patterns rft./act./atk./JWT).
    /// 2) Enmascara valores JSON de campos sensibles (access_token, refresh_token, code, auth_code, id_token).
    /// 3) Trunca a 2KB para no inflar la tabla.
    /// </summary>
    private static string? SanRaw(string? body)
    {
        if (string.IsNullOrEmpty(body)) { return body; }
        var s = TokenSanitizer.Sanitize(body) ?? "";
        s = System.Text.RegularExpressions.Regex.Replace(s,
            "\"(access_token|refresh_token|auth_code|code|id_token)\"\\s*:\\s*\"[^\"]*\"",
            "\"$1\":\"[REDACTED]\"");
        if (s.Length > 2048) { s = s.Substring(0, 2048) + "...[truncated]"; }
        return s;
    }

    public async Task<OAuthTokenResult> ExchangeCodeAsync(
        string clientKey, string clientSecret, string redirectUri, string authCode, CancellationToken cancellationToken = default)
    {
        var trace = new StringBuilder();
        int lastHttpStatus = 0;
        string? lastEndpoint = null;
        string? lastResponseCode = null;
        string? lastRaw = null;
        var attempts = new List<OAuthAttemptRecord>();
        try
        {
            // ===== Endpoint primario: Open v2 (open.tiktokapis.com/v2/oauth/token/) =====
            trace.AppendLine("[INFO] Canjeando auth_code en open.tiktokapis.com/v2/oauth/token/ ...");
            var openForm =
                "client_key=" + Uri.EscapeDataString(clientKey) +
                "&client_secret=" + Uri.EscapeDataString(clientSecret) +
                "&code=" + Uri.EscapeDataString(authCode) +
                "&grant_type=authorization_code" +
                "&redirect_uri=" + Uri.EscapeDataString(redirectUri);
            var swOpen = System.Diagnostics.Stopwatch.StartNew();
            var openResp = await PostFormWithStatusAsync(ExchangeOpenV2, openForm, cancellationToken);
            swOpen.Stop();
            lastEndpoint = ExchangeOpenV2;
            lastHttpStatus = openResp.Status;
            lastRaw = SanRaw(openResp.Body);
            try
            {
                using var doc = JsonDocument.Parse(openResp.Body);
                var root = doc.RootElement;
                var at = GetString(root, "access_token");
                if (!string.IsNullOrEmpty(at))
                {
                    trace.AppendLine("[OK] Token obtenido en Open v2 (flavor=OpenV2).");
                    var rt = GetString(root, "refresh_token");
                    return new OAuthTokenResult(true, at, rt, GetString(root, "open_id"),
                        GetInt(root, "expires_in"), trace.ToString(), null,
                        FlavorOpenV2, ExchangeOpenV2, openResp.Status, "0", lastRaw, attempts);
                }
                var openErr = GetString(root, "error") ?? GetString(root, "error_description") ?? "sin detalle";
                var openErrCode = GetString(root, "error") ?? "unknown";
                lastResponseCode = openErrCode;
                trace.AppendLine($"[INFO] Open v2 rechazo: {San(openErr)}. Probando fallback Business v1.3 ...");
                attempts.Add(new OAuthAttemptRecord(ExchangeOpenV2, "OpenV2", openResp.Status,
                    openErrCode, San(openErr), lastRaw, (int)swOpen.ElapsedMilliseconds));
            }
            catch (Exception parseEx)
            {
                trace.AppendLine($"[INFO] Open v2 respondio no-JSON (HTTP {openResp.Status}): {San(parseEx.Message)}. Probando Business v1.3 ...");
                attempts.Add(new OAuthAttemptRecord(ExchangeOpenV2, "OpenV2", openResp.Status,
                    "parse-error", San(parseEx.Message), lastRaw, (int)swOpen.ElapsedMilliseconds));
            }

            // ===== Fallback 1: Business v1.3 /tt_user/oauth2/token/ =====
            trace.AppendLine("[INFO] Canjeando en /tt_user/oauth2/token/ (Business v1.3) ...");
            var bodyPrimary = JsonSerializer.Serialize(new
            {
                client_id = clientKey,
                client_secret = clientSecret,
                grant_type = "authorization_code",
                auth_code = authCode,
                redirect_uri = redirectUri
            });
            var swBiz1 = System.Diagnostics.Stopwatch.StartNew();
            var respPrimary = await PostJsonWithStatusAsync(ExchangeBusinessV13, bodyPrimary, cancellationToken);
            swBiz1.Stop();
            lastEndpoint = ExchangeBusinessV13;
            lastHttpStatus = respPrimary.Status;
            lastRaw = SanRaw(respPrimary.Body);
            var (okP, dataP, codeP, msgP) = ParseBusiness(respPrimary.Body);
            lastResponseCode = codeP.ToString();
            if (okP)
            {
                trace.AppendLine("[OK] Token obtenido en /tt_user/oauth2/token/ (flavor=BusinessV13).");
                return BuildFromData(dataP!.Value, trace.ToString(), oauthFlavor: FlavorBusinessV13,
                    endpoint: ExchangeBusinessV13, httpStatus: respPrimary.Status, rawResponse: lastRaw, priorAttempts: attempts);
            }
            attempts.Add(new OAuthAttemptRecord(ExchangeBusinessV13, "BusinessV13", respPrimary.Status,
                codeP.ToString(), San(msgP), lastRaw, (int)swBiz1.ElapsedMilliseconds));

            // ===== Fallback 2: Business v1.3 /oauth2/access_token/ =====
            trace.AppendLine($"[INFO] /tt_user/... respondio code={codeP} ({San(msgP)}). Probando /oauth2/access_token/ ...");
            var bodyFallback = JsonSerializer.Serialize(new { app_id = clientKey, secret = clientSecret, auth_code = authCode });
            var swBiz2 = System.Diagnostics.Stopwatch.StartNew();
            var respFallback = await PostJsonWithStatusAsync(ExchangeBusinessV13Fallback, bodyFallback, cancellationToken);
            swBiz2.Stop();
            lastEndpoint = ExchangeBusinessV13Fallback;
            lastHttpStatus = respFallback.Status;
            lastRaw = SanRaw(respFallback.Body);
            var (okF, dataF, codeF, msgF) = ParseBusiness(respFallback.Body);
            lastResponseCode = codeF.ToString();
            if (okF)
            {
                trace.AppendLine("[OK] Token obtenido en /oauth2/access_token/ (flavor=BusinessV13).");
                return BuildFromData(dataF!.Value, trace.ToString(), oauthFlavor: FlavorBusinessV13,
                    endpoint: ExchangeBusinessV13Fallback, httpStatus: respFallback.Status, rawResponse: lastRaw, priorAttempts: attempts);
            }
            attempts.Add(new OAuthAttemptRecord(ExchangeBusinessV13Fallback, "BusinessV13", respFallback.Status,
                codeF.ToString(), San(msgF), lastRaw, (int)swBiz2.ElapsedMilliseconds));

            trace.AppendLine($"[ERROR] Todos los endpoints rechazaron el auth_code. Ultimo: code={codeF}: {San(msgF)}");
            return new OAuthTokenResult(false, null, null, null, null, San(trace.ToString())!,
                San(msgF) ?? San(msgP) ?? "Error desconocido", null,
                lastEndpoint, lastHttpStatus, lastResponseCode, lastRaw, attempts);
        }
        catch (Exception ex)
        {
            trace.AppendLine("[ERROR] " + San(ex.Message));
            return new OAuthTokenResult(false, null, null, null, null, San(trace.ToString())!, San(ex.Message),
                null, lastEndpoint, lastHttpStatus, lastResponseCode, lastRaw, attempts);
        }
    }

    public async Task<OAuthTokenResult> RefreshAsync(
        string clientKey, string clientSecret, string refreshToken, int? oauthFlavor = null, CancellationToken cancellationToken = default)
    {
        // Flavor deterministico. Si el llamador no lo pasa (cuentas historicas antes del campo),
        // asumimos BusinessV13 — que era el flujo por defecto de canje. Nunca hacemos cascada:
        // un fallback puede consumir el nuevo refresh_token del endpoint que SI acepto y dejar la
        // cuenta con un token invalidado en el siguiente ciclo.
        var flavor = oauthFlavor ?? FlavorBusinessV13;
        var trace = new StringBuilder();

        if (flavor == FlavorOpenV2)
        {
            return await RefreshOpenV2Async(clientKey, clientSecret, refreshToken, trace, cancellationToken);
        }
        return await RefreshBusinessV13Async(clientKey, clientSecret, refreshToken, trace, cancellationToken);
    }

    private async Task<OAuthTokenResult> RefreshBusinessV13Async(
        string clientKey, string clientSecret, string refreshToken, StringBuilder trace, CancellationToken ct)
    {
        try
        {
            trace.AppendLine("[INFO] Renovando (flavor=BusinessV13) en /oauth2/refresh_token/ ...");
            var body = JsonSerializer.Serialize(new { app_id = clientKey, secret = clientSecret, refresh_token = refreshToken });
            var resp = await PostJsonWithStatusAsync(RefreshBusinessV13, body, ct);
            var rawSan = SanRaw(resp.Body);
            var (ok, data, code, msg) = ParseBusiness(resp.Body);
            if (ok)
            {
                trace.AppendLine("[OK] Token renovado.");
                return BuildFromData(data!.Value, trace.ToString(), fallbackRefresh: refreshToken,
                    oauthFlavor: FlavorBusinessV13, endpoint: RefreshBusinessV13, httpStatus: resp.Status,
                    rawResponse: rawSan);
            }
            trace.AppendLine($"[ERROR] /oauth2/refresh_token/ respondio code={code}: {San(msg)}");
            var errorWithCode = $"code={code} {San(msg) ?? "Renovacion fallida"}";
            return new OAuthTokenResult(false, null, null, null, null, San(trace.ToString())!, errorWithCode,
                null, RefreshBusinessV13, resp.Status, code.ToString(), rawSan);
        }
        catch (Exception ex)
        {
            trace.AppendLine("[ERROR] " + San(ex.Message));
            return new OAuthTokenResult(false, null, null, null, null, San(trace.ToString())!, San(ex.Message),
                null, RefreshBusinessV13, 0, null);
        }
    }

    private async Task<OAuthTokenResult> RefreshOpenV2Async(
        string clientKey, string clientSecret, string refreshToken, StringBuilder trace, CancellationToken ct)
    {
        try
        {
            trace.AppendLine("[INFO] Renovando (flavor=OpenV2) en open.tiktokapis.com/v2/oauth/token/ ...");
            var form =
                "client_key=" + Uri.EscapeDataString(clientKey) +
                "&client_secret=" + Uri.EscapeDataString(clientSecret) +
                "&grant_type=refresh_token" +
                "&refresh_token=" + Uri.EscapeDataString(refreshToken);
            var resp = await PostFormWithStatusAsync(RefreshOpenV2, form, ct);
            var rawSan = SanRaw(resp.Body);
            using var doc = JsonDocument.Parse(resp.Body);
            var root = doc.RootElement;
            var at = GetString(root, "access_token");
            if (!string.IsNullOrEmpty(at))
            {
                trace.AppendLine("[OK] Token renovado.");
                var rt = GetString(root, "refresh_token");
                return new OAuthTokenResult(true, at, string.IsNullOrEmpty(rt) ? refreshToken : rt,
                    GetString(root, "open_id"), GetInt(root, "expires_in"), trace.ToString(), null,
                    FlavorOpenV2, RefreshOpenV2, resp.Status, "0", rawSan);
            }
            var err = GetString(root, "error") ?? GetString(root, "error_description") ?? "sin detalle";
            var errCode = GetString(root, "error");
            trace.AppendLine($"[ERROR] v2 respondio sin access_token: {San(err)}");
            return new OAuthTokenResult(false, null, null, null, null, San(trace.ToString())!, San(err),
                null, RefreshOpenV2, resp.Status, errCode, rawSan);
        }
        catch (Exception ex)
        {
            trace.AppendLine("[ERROR] " + San(ex.Message));
            return new OAuthTokenResult(false, null, null, null, null, San(trace.ToString())!, San(ex.Message),
                null, RefreshOpenV2, 0, null);
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
            var body = await PostFormAsync(RefreshOpenV2, form, cancellationToken);
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

    private static OAuthTokenResult BuildFromData(JsonElement data, string trace, string? fallbackRefresh = null,
        int? oauthFlavor = null, string? endpoint = null, int? httpStatus = null,
        string? rawResponse = null, IReadOnlyList<OAuthAttemptRecord>? priorAttempts = null)
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
            null,
            oauthFlavor,
            endpoint,
            httpStatus,
            "0",
            rawResponse,
            priorAttempts);
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
        var (body, _) = await PostWithStatusAsync(url, content, ct);
        return body;
    }

    private async Task<(string Body, int Status)> PostJsonWithStatusAsync(string url, string json, CancellationToken ct)
    {
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        return await PostWithStatusAsync(url, content, ct);
    }

    private async Task<(string Body, int Status)> PostFormWithStatusAsync(string url, string form, CancellationToken ct)
    {
        // TikTok Open v2 rechaza requests con "; charset=utf-8" en Content-Type ("invalid_request:
        // parameters are malformed"). Su curl oficial usa el Content-Type exacto sin sufijos, y
        // exige el header Cache-Control: no-cache. Construimos el content manual para poder omitir
        // el charset que StringContent(..., Encoding.UTF8, ...) siempre adjunta.
        var bytes = Encoding.UTF8.GetBytes(form);
        using var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/x-www-form-urlencoded");
        return await PostWithStatusAsync(url, content, ct, cacheControlNoCache: true);
    }

    private async Task<(string Body, int Status)> PostWithStatusAsync(string url, HttpContent content, CancellationToken ct,
        bool cacheControlNoCache = false)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };
        req.Headers.TryAddWithoutValidation("Accept", "application/json");
        if (cacheControlNoCache)
        {
            req.Headers.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue { NoCache = true };
        }
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(30));
        using var resp = await _http.SendAsync(req, cts.Token);
        var body = await resp.Content.ReadAsStringAsync(cts.Token);
        return (body, (int)resp.StatusCode);
    }
}
