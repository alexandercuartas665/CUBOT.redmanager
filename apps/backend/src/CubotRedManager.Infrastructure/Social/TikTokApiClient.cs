using System.Text;
using System.Text.Json;
using CubotRedManager.Application.Abstractions;

namespace CubotRedManager.Infrastructure.Social;

/// <summary>
/// Implementacion HttpClient del cliente de datos de TikTok Business API. Porta fielmente los
/// endpoints del control VB.NET de referencia (ctrlVideos / ctrlBandeja):
///  - /business/video/list/ (cursor + max_count)
///  - /business/comment/list/ (page + page_size + status=ALL)
///  - /business/comment/reply/list/ (cursor + count)
/// Header: "Access-Token" (NO Bearer). TLS lo negocia .NET 10 automaticamente.
/// </summary>
public sealed class TikTokApiClient : ITikTokApiClient
{
    private const string Base = "https://business-api.tiktok.com/open_api/v1.3";
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    private readonly HttpClient _http;

    public TikTokApiClient(HttpClient http)
    {
        _http = http;
    }

    public Task<TikTokApiPage> ListVideosAsync(
        string accessToken, string businessId, long cursor, int maxCount, CancellationToken cancellationToken = default)
    {
        // Fields debe ir como JSON-array stringified. ATENCION: TikTok rechaza "video_description"
        // (no esta en su catalogo permitido); la descripcion viene en "caption". Lista exacta
        // validada en produccion via ctrlVideos.ascx.vb.
        var fields = "[\"item_id\",\"thumbnail_url\",\"caption\",\"likes\",\"comments\",\"shares\",\"video_views\",\"create_time\",\"share_url\",\"embed_url\"]";
        var url = Base + "/business/video/list/" +
                  "?business_id=" + Uri.EscapeDataString(businessId) +
                  "&fields=" + Uri.EscapeDataString(fields) +
                  "&max_count=" + maxCount +
                  "&cursor=" + cursor;
        return GetAsync(url, accessToken, cancellationToken);
    }

    public Task<TikTokApiPage> ListCommentsAsync(
        string accessToken, string businessId, string videoId, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var url = Base + "/business/comment/list/" +
                  "?business_id=" + Uri.EscapeDataString(businessId) +
                  "&video_id=" + Uri.EscapeDataString(videoId) +
                  "&page=" + page +
                  "&page_size=" + pageSize +
                  "&status=ALL";
        return GetAsync(url, accessToken, cancellationToken);
    }

    public Task<TikTokApiPage> ListRepliesAsync(
        string accessToken, string businessId, string videoId, string commentId, long cursor, int count, CancellationToken cancellationToken = default)
    {
        var url = Base + "/business/comment/reply/list/" +
                  "?business_id=" + Uri.EscapeDataString(businessId) +
                  "&video_id=" + Uri.EscapeDataString(videoId) +
                  "&comment_id=" + Uri.EscapeDataString(commentId) +
                  "&cursor=" + cursor +
                  "&count=" + count;
        return GetAsync(url, accessToken, cancellationToken);
    }

    public Task<TikTokApiPage> PostCommentReplyAsync(
        string accessToken, string businessId, string videoId, string commentId, string text, CancellationToken cancellationToken = default)
    {
        // TikTok limita a 2000 chars y exige escape JSON manual del texto.
        var safe = text.Length > 2000 ? text[..2000] : text;
        var url = Base + "/business/comment/reply/create/";
        var body = JsonSerializer.Serialize(new
        {
            business_id = businessId,
            video_id = videoId,
            comment_id = commentId,
            text = safe
        });
        return PostJsonAsync(url, body, accessToken, cancellationToken);
    }

    private async Task<TikTokApiPage> PostJsonAsync(string url, string json, string accessToken, CancellationToken cancellationToken)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        req.Headers.TryAddWithoutValidation("Access-Token", accessToken);
        req.Headers.TryAddWithoutValidation("Accept", "application/json");

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(Timeout);
        string body;
        try
        {
            using var resp = await _http.SendAsync(req, cts.Token);
            body = await resp.Content.ReadAsStringAsync(cts.Token);
        }
        catch (Exception ex)
        {
            return new TikTokApiPage(-1, ex.Message, "{}");
        }
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            var code = root.TryGetProperty("code", out var c) && c.ValueKind == JsonValueKind.Number ? c.GetInt32() : -1;
            var message = root.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String ? m.GetString() : null;
            return new TikTokApiPage(code, message, body);
        }
        catch
        {
            return new TikTokApiPage(-1, "Respuesta no es JSON valido", body);
        }
    }

    // ====== Content Posting API ======
    // Host distinto + Bearer en lugar de Access-Token.

    private const string ContentApiBase = "https://open.tiktokapis.com";

    public Task<TikTokApiPage> PublishVideoInitAsync(
        string accessToken, string caption, long videoSize, string privacyLevel, CancellationToken cancellationToken = default)
    {
        // Apps en sandbox (no auditadas) no pueden Direct Post. Usamos INBOX UPLOAD:
        // el video llega a la "bandeja de borradores" de la cuenta TikTok y el usuario lo
        // publica manualmente desde la app de TikTok. Sin post_info (TikTok lo rechaza aqui).
        var body = JsonSerializer.Serialize(new
        {
            source_info = new
            {
                source = "FILE_UPLOAD",
                video_size = videoSize,
                chunk_size = videoSize,
                total_chunk_count = 1
            }
        });
        return PostJsonBearerAsync(ContentApiBase + "/v2/post/publish/inbox/video/init/", body, accessToken, cancellationToken);
    }

    public async Task<(bool Ok, string? Error)> PublishVideoUploadAsync(
        string uploadUrl, Stream videoStream, long videoSize, string contentType, CancellationToken cancellationToken = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Put, uploadUrl);
        var sc = new StreamContent(videoStream);
        sc.Headers.TryAddWithoutValidation("Content-Type", contentType);
        sc.Headers.TryAddWithoutValidation("Content-Length", videoSize.ToString());
        // TikTok exige Content-Range incluso en single-chunk.
        sc.Headers.TryAddWithoutValidation("Content-Range", $"bytes 0-{videoSize - 1}/{videoSize}");
        req.Content = sc;

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromMinutes(5));
        try
        {
            using var resp = await _http.SendAsync(req, cts.Token);
            if (resp.IsSuccessStatusCode) { return (true, null); }
            var body = await resp.Content.ReadAsStringAsync(cts.Token);
            return (false, $"HTTP {(int)resp.StatusCode}: {Trim(body)}");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    public Task<TikTokApiPage> PublishStatusAsync(
        string accessToken, string publishId, CancellationToken cancellationToken = default)
    {
        var body = JsonSerializer.Serialize(new { publish_id = publishId });
        return PostJsonBearerAsync(ContentApiBase + "/v2/post/publish/status/fetch/", body, accessToken, cancellationToken);
    }

    public async Task<TikTokApiPage> GetUserInfoAsync(string accessToken, CancellationToken cancellationToken = default)
    {
        // El endpoint v2/user/info acepta lista de fields como query param. Pedimos los mas utiles
        // para identificar la cuenta visualmente.
        var url = ContentApiBase + "/v2/user/info/?fields=" + Uri.EscapeDataString("open_id,union_id,avatar_url,display_name,username,bio_description,is_verified,follower_count,following_count");
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.TryAddWithoutValidation("Authorization", "Bearer " + accessToken);
        req.Headers.TryAddWithoutValidation("Accept", "application/json");
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(Timeout);
        string body;
        try
        {
            using var resp = await _http.SendAsync(req, cts.Token);
            body = await resp.Content.ReadAsStringAsync(cts.Token);
        }
        catch (Exception ex) { return new TikTokApiPage(-1, ex.Message, "{}"); }
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            int code = -1; string? message = null;
            if (root.TryGetProperty("error", out var err) && err.ValueKind == JsonValueKind.Object)
            {
                var c = err.TryGetProperty("code", out var cp) && cp.ValueKind == JsonValueKind.String ? cp.GetString() : null;
                message = err.TryGetProperty("message", out var mp) && mp.ValueKind == JsonValueKind.String ? mp.GetString() : null;
                code = string.Equals(c, "ok", StringComparison.OrdinalIgnoreCase) ? 0 : 1;
                if (code == 1 && string.IsNullOrEmpty(message)) { message = c; }
            }
            else { code = root.TryGetProperty("data", out _) ? 0 : -1; }
            return new TikTokApiPage(code, message, body);
        }
        catch { return new TikTokApiPage(-1, "JSON invalido", body); }
    }

    private async Task<TikTokApiPage> PostJsonBearerAsync(string url, string json, string accessToken, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        req.Headers.TryAddWithoutValidation("Authorization", "Bearer " + accessToken);
        req.Headers.TryAddWithoutValidation("Accept", "application/json");
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(Timeout);
        string body;
        try
        {
            using var resp = await _http.SendAsync(req, cts.Token);
            body = await resp.Content.ReadAsStringAsync(cts.Token);
        }
        catch (Exception ex)
        {
            return new TikTokApiPage(-1, ex.Message, "{}");
        }
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            // Content Posting API encapsula errores en root.error.{code,message}
            int code = -1;
            string? message = null;
            if (root.TryGetProperty("error", out var err) && err.ValueKind == JsonValueKind.Object)
            {
                var c = err.TryGetProperty("code", out var cp) && cp.ValueKind == JsonValueKind.String ? cp.GetString() : null;
                message = err.TryGetProperty("message", out var mp) && mp.ValueKind == JsonValueKind.String ? mp.GetString() : null;
                // "ok" significa exito. Cualquier otro string es error.
                code = string.Equals(c, "ok", StringComparison.OrdinalIgnoreCase) ? 0 : 1;
                if (code == 1 && string.IsNullOrEmpty(message)) { message = c; }
            }
            else
            {
                // Fallback: si no hay error envelope, asumimos exito si hay data
                code = root.TryGetProperty("data", out _) ? 0 : -1;
            }
            return new TikTokApiPage(code, message, body);
        }
        catch
        {
            return new TikTokApiPage(-1, "Respuesta no es JSON valido", body);
        }
    }

    private static string Trim(string s) => s.Length > 300 ? s[..300] : s;

    private async Task<TikTokApiPage> GetAsync(string url, string accessToken, CancellationToken cancellationToken)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.TryAddWithoutValidation("Access-Token", accessToken);
        req.Headers.TryAddWithoutValidation("Accept", "application/json");

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(Timeout);
        string body;
        try
        {
            using var resp = await _http.SendAsync(req, cts.Token);
            body = await resp.Content.ReadAsStringAsync(cts.Token);
        }
        catch (Exception ex)
        {
            return new TikTokApiPage(-1, ex.Message, "{}");
        }

        // TikTok responde con {code, message, data} incluso en errores HTTP 200.
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            var code = root.TryGetProperty("code", out var c) && c.ValueKind == JsonValueKind.Number ? c.GetInt32() : -1;
            var message = root.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String ? m.GetString() : null;
            return new TikTokApiPage(code, message, body);
        }
        catch
        {
            return new TikTokApiPage(-1, "Respuesta no es JSON valido", body);
        }
    }
}
