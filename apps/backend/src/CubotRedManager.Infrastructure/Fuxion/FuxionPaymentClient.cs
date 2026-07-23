using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using CubotRedManager.Application.Common;
using Microsoft.Extensions.Logging;

namespace CubotRedManager.Infrastructure.Fuxion;

public sealed class FuxionPaymentClient : IFuxionPaymentClient
{
    private readonly HttpClient _http;
    private readonly ILogger<FuxionPaymentClient> _logger;

    public FuxionPaymentClient(HttpClient http, ILogger<FuxionPaymentClient> logger)
    {
        _http = http;
        _logger = logger;
        // Timeout defensivo: el endpoint tomo ~1.4s en pruebas reales; 20s es un margen amplio.
        if (_http.Timeout == Timeout.InfiniteTimeSpan || _http.Timeout > TimeSpan.FromSeconds(20))
        {
            _http.Timeout = TimeSpan.FromSeconds(20);
        }
    }

    public async Task<FuxionGenerateLinkResult> GenerateSalesLinkAsync(FuxionGenerateLinkRequest req, CancellationToken cancellationToken = default)
    {
        // Validaciones basicas: si el operador dejo la config incompleta, no llamamos afuera.
        if (string.IsNullOrWhiteSpace(req.BaseUrl) || string.IsNullOrWhiteSpace(req.PathTemplate))
        {
            return FuxionGenerateLinkResult.Failure(FuxionGenerateLinkErrorKind.BadRequest, "BaseUrl o PathTemplate vacios en la configuracion del agente.");
        }
        if (string.IsNullOrWhiteSpace(req.UserId))
        {
            return FuxionGenerateLinkResult.Failure(FuxionGenerateLinkErrorKind.BadRequest, "PaymentUserId vacio en la configuracion del agente.");
        }
        if (string.IsNullOrWhiteSpace(req.BearerToken))
        {
            return FuxionGenerateLinkResult.Failure(FuxionGenerateLinkErrorKind.TokenExpired, "No hay token FUXION guardado o esta corrupto.");
        }
        if (req.Items.Count == 0)
        {
            return FuxionGenerateLinkResult.Failure(FuxionGenerateLinkErrorKind.BadRequest, "El link requiere al menos un item.");
        }

        // Construir URL sustituyendo {userId}. Si el operador uso otro placeholder, no rompemos.
        var pathResolved = req.PathTemplate.Replace("{userId}", Uri.EscapeDataString(req.UserId));
        var baseTrimmed = req.BaseUrl.TrimEnd('/');
        var fullUrl = baseTrimmed + (pathResolved.StartsWith('/') ? pathResolved : "/" + pathResolved);

        // Body EXACTAMENTE como lo emite la SPA de app-aware (verificado con interceptor XHR):
        // { "country": "pe", "description": "...", "items": [ {"itemId":"144175","amount":1}, ... ] }
        var payload = new
        {
            country = req.Country ?? "",
            description = req.Description ?? "",
            items = req.Items.Select(i => new { itemId = i.ItemId, amount = i.Amount }).ToArray()
        };
        var json = JsonSerializer.Serialize(payload);

        // Retry con backoff exponencial para errores transitorios (red / timeout / 5xx / 429).
        // Errores logicos (400, 401, 403) NO se reintentan: son deterministas al mismo request.
        // El token va SOLO en el header. Nunca en logs, nunca en URL, nunca en el body.
        int status = 0;
        string respBody = "";
        var backoffs = new[] { TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(3) };
        for (var attempt = 0; ; attempt++)
        {
            // Cada intento requiere una request nueva; HttpRequestMessage no es reusable tras SendAsync.
            using var attemptReq = new HttpRequestMessage(HttpMethod.Post, fullUrl) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
            attemptReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", req.BearerToken);
            attemptReq.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            try
            {
                using (var resp = await _http.SendAsync(attemptReq, cancellationToken))
                {
                    status = (int)resp.StatusCode;
                    try { respBody = await resp.Content.ReadAsStringAsync(cancellationToken); } catch { /* body opcional */ }
                }
                var transient = status >= 500 || status == 429 || status == (int)HttpStatusCode.RequestTimeout;
                if (!transient) { break; }
                if (attempt >= backoffs.Length) { break; }
                _logger.LogInformation("FuxionPayment: retry {Attempt} tras {Status} (backoff {Delay}s)", attempt + 1, status, backoffs[attempt].TotalSeconds);
                await Task.Delay(backoffs[attempt], cancellationToken);
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                if (attempt >= backoffs.Length)
                {
                    _logger.LogWarning("FuxionPayment: timeout tras {Attempts} intentos {Url}", attempt + 1, fullUrl);
                    return FuxionGenerateLinkResult.Failure(FuxionGenerateLinkErrorKind.ServerError, "timeout llamando al API de FUXION.");
                }
                await Task.Delay(backoffs[attempt], cancellationToken);
            }
            catch (HttpRequestException ex)
            {
                if (attempt >= backoffs.Length)
                {
                    _logger.LogWarning(ex, "FuxionPayment: fallo de red tras {Attempts} intentos {Url}", attempt + 1, fullUrl);
                    return FuxionGenerateLinkResult.Failure(FuxionGenerateLinkErrorKind.ServerError, $"error de red: {ex.Message}");
                }
                await Task.Delay(backoffs[attempt], cancellationToken);
            }
        }

        if (status == 401 || status == 403)
        {
            _logger.LogInformation("FuxionPayment: {Status} (token invalido/expirado) para userId {UserId}", status, req.UserId);
            return FuxionGenerateLinkResult.Failure(FuxionGenerateLinkErrorKind.TokenExpired,
                "token FUXION rechazado. Renuevalo en /agentes -> Pagos FUXION.", status);
        }
        if (status >= 500 || status == (int)HttpStatusCode.RequestTimeout || status == 429)
        {
            _logger.LogWarning("FuxionPayment: {Status} del servidor, body: {Body}", status, TruncateForLog(respBody));
            return FuxionGenerateLinkResult.Failure(FuxionGenerateLinkErrorKind.ServerError,
                $"FUXION respondio {status}.", status);
        }
        if (status >= 400)
        {
            _logger.LogWarning("FuxionPayment: {Status} en request, body: {Body}", status, TruncateForLog(respBody));
            return FuxionGenerateLinkResult.Failure(FuxionGenerateLinkErrorKind.BadRequest,
                $"FUXION respondio {status}: {TruncateForLog(respBody, 200)}", status);
        }

        // 2xx: parsear response usando el JsonPath configurado (default "data.url").
        try
        {
            using var doc = JsonDocument.Parse(respBody);
            var url = ExtractJsonPath(doc.RootElement, req.ResponseUrlPath);
            if (string.IsNullOrWhiteSpace(url))
            {
                _logger.LogWarning("FuxionPayment: response {Status} sin URL en path {Path}. Body: {Body}",
                    status, req.ResponseUrlPath, TruncateForLog(respBody));
                return FuxionGenerateLinkResult.Failure(FuxionGenerateLinkErrorKind.UnexpectedResponse,
                    $"respuesta sin URL en '{req.ResponseUrlPath}'. Revisar overrides en /agentes.", status);
            }
            return FuxionGenerateLinkResult.Success(url);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "FuxionPayment: response no es JSON valido. Body: {Body}", TruncateForLog(respBody));
            return FuxionGenerateLinkResult.Failure(FuxionGenerateLinkErrorKind.UnexpectedResponse,
                "respuesta de FUXION no es JSON.", status);
        }
    }

    /// <summary>Extrae un valor string siguiendo un path dot-separated (data.url, data.link.href, etc.).
    /// Devuelve null si el path no existe o el nodo no es string. Path vacio -> null.</summary>
    private static string? ExtractJsonPath(JsonElement root, string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) { return null; }
        var current = root;
        foreach (var segment in path.Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            if (current.ValueKind != JsonValueKind.Object) { return null; }
            if (!current.TryGetProperty(segment, out var next)) { return null; }
            current = next;
        }
        return current.ValueKind == JsonValueKind.String ? current.GetString() : null;
    }

    private static string TruncateForLog(string s, int max = 500)
        => string.IsNullOrEmpty(s) ? "" : (s.Length > max ? s.Substring(0, max) + "..." : s);

    public async Task<FuxionVerifySessionResult> VerifySessionAsync(string baseUrl, string bearerToken, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(bearerToken))
        {
            return new FuxionVerifySessionResult(FuxionVerifySessionOutcome.Rejected, null, "config incompleta");
        }
        var url = baseUrl.TrimEnd('/') + "/api/auth/user/verify-session";
        using var req = new HttpRequestMessage(HttpMethod.Post, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        try
        {
            using var resp = await _http.SendAsync(req, cancellationToken);
            var status = (int)resp.StatusCode;
            if (status == 401 || status == 403)
            {
                return new FuxionVerifySessionResult(FuxionVerifySessionOutcome.Rejected, status, "token rechazado");
            }
            if (status >= 200 && status < 300)
            {
                return new FuxionVerifySessionResult(FuxionVerifySessionOutcome.Valid, status, null);
            }
            return new FuxionVerifySessionResult(FuxionVerifySessionOutcome.Unreachable, status, $"HTTP {status}");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogInformation("FuxionPayment.VerifySession: red/timeout {Msg}", ex.Message);
            return new FuxionVerifySessionResult(FuxionVerifySessionOutcome.Unreachable, null, ex.Message);
        }
    }
}
