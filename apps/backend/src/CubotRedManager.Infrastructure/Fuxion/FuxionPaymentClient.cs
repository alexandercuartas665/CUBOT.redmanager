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
        using var body = new StringContent(json, Encoding.UTF8, "application/json");

        using var httpReq = new HttpRequestMessage(HttpMethod.Post, fullUrl);
        httpReq.Content = body;
        // El token va SOLO en el header. Nunca en logs, nunca en URL, nunca en el body.
        httpReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", req.BearerToken);
        httpReq.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        HttpResponseMessage resp;
        try
        {
            resp = await _http.SendAsync(httpReq, cancellationToken);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("FuxionPayment: timeout llamando {Url}", fullUrl);
            return FuxionGenerateLinkResult.Failure(FuxionGenerateLinkErrorKind.ServerError, "timeout llamando al API de FUXION.");
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "FuxionPayment: fallo de red llamando {Url}", fullUrl);
            return FuxionGenerateLinkResult.Failure(FuxionGenerateLinkErrorKind.ServerError, $"error de red: {ex.Message}");
        }

        var status = (int)resp.StatusCode;
        string respBody = "";
        try { respBody = await resp.Content.ReadAsStringAsync(cancellationToken); } catch { /* body opcional */ }

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
}
