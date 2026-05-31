using System.Net.Http.Json;
using System.Text.Json;
using CubotRedManager.Application.Admin;

namespace CubotRedManager.Infrastructure.Evolution;

/// <summary>
/// Cliente HTTP del servidor Evolution API (v2): comprobar conexion, crear/conectar instancia,
/// estado, eliminar y enviar mensajes. La API key (global o del servidor propio) va en el header "apikey".
/// </summary>
public sealed class EvolutionApiClient : IEvolutionApiClient
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);
    private readonly HttpClient _http;

    public EvolutionApiClient(HttpClient http)
    {
        _http = http;
    }

    public async Task<EvolutionPingResult> CheckAsync(string baseUrl, string apiKey, CancellationToken cancellationToken = default)
    {
        try
        {
            using var resp = await SendAsync(HttpMethod.Get, baseUrl, "/instance/fetchInstances", apiKey, content: null, cancellationToken);
            var code = (int)resp.StatusCode;
            if (resp.IsSuccessStatusCode) { return new EvolutionPingResult(true, true, code, "OK"); }
            return new EvolutionPingResult(true, false, code, code is 401 or 403 ? "Unauthorized" : $"HTTP {code}");
        }
        catch (Exception ex)
        {
            return new EvolutionPingResult(false, false, null, ex.GetType().Name);
        }
    }

    public async Task<EvolutionInstanceResult> CreateInstanceAsync(string baseUrl, string apiKey, string instanceName, CancellationToken cancellationToken = default)
    {
        var body = new { instanceName, qrcode = true, integration = "WHATSAPP-BAILEYS" };
        try
        {
            using var resp = await SendAsync(HttpMethod.Post, baseUrl, "/instance/create", apiKey, JsonContent.Create(body), cancellationToken);
            var json = await resp.Content.ReadAsStringAsync(cancellationToken);
            if (!resp.IsSuccessStatusCode)
            {
                if ((int)resp.StatusCode is 403 or 409 || json.Contains("already in use", StringComparison.OrdinalIgnoreCase))
                {
                    return await ConnectAsync(baseUrl, apiKey, instanceName, cancellationToken);
                }
                return new EvolutionInstanceResult(false, null, null, null, $"HTTP {(int)resp.StatusCode}: {Trim(json)}");
            }
            using var doc = JsonDocument.Parse(json);
            return new EvolutionInstanceResult(true, ExtractQr(doc.RootElement), ExtractState(doc.RootElement), null, null);
        }
        catch (Exception ex)
        {
            return new EvolutionInstanceResult(false, null, null, null, ex.Message);
        }
    }

    public async Task<EvolutionInstanceResult> ConnectAsync(string baseUrl, string apiKey, string instanceName, CancellationToken cancellationToken = default)
    {
        try
        {
            using var resp = await SendAsync(HttpMethod.Get, baseUrl, $"/instance/connect/{Uri.EscapeDataString(instanceName)}", apiKey, content: null, cancellationToken);
            var json = await resp.Content.ReadAsStringAsync(cancellationToken);
            if (!resp.IsSuccessStatusCode)
            {
                return new EvolutionInstanceResult(false, null, null, null, $"HTTP {(int)resp.StatusCode}: {Trim(json)}");
            }
            using var doc = JsonDocument.Parse(json);
            return new EvolutionInstanceResult(true, ExtractQr(doc.RootElement), ExtractState(doc.RootElement), null, null);
        }
        catch (Exception ex)
        {
            return new EvolutionInstanceResult(false, null, null, null, ex.Message);
        }
    }

    public async Task<EvolutionInstanceResult> GetStateAsync(string baseUrl, string apiKey, string instanceName, CancellationToken cancellationToken = default)
    {
        try
        {
            using var resp = await SendAsync(HttpMethod.Get, baseUrl, $"/instance/connectionState/{Uri.EscapeDataString(instanceName)}", apiKey, content: null, cancellationToken);
            var json = await resp.Content.ReadAsStringAsync(cancellationToken);
            if (!resp.IsSuccessStatusCode)
            {
                return new EvolutionInstanceResult(false, null, null, null, $"HTTP {(int)resp.StatusCode}");
            }
            using var doc = JsonDocument.Parse(json);
            return new EvolutionInstanceResult(true, null, ExtractState(doc.RootElement), null, null);
        }
        catch (Exception ex)
        {
            return new EvolutionInstanceResult(false, null, null, null, ex.Message);
        }
    }

    public async Task<bool> DeleteInstanceAsync(string baseUrl, string apiKey, string instanceName, CancellationToken cancellationToken = default)
    {
        try
        {
            try { (await SendAsync(HttpMethod.Delete, baseUrl, $"/instance/logout/{Uri.EscapeDataString(instanceName)}", apiKey, null, cancellationToken)).Dispose(); }
            catch { /* puede no estar conectada */ }
            using var resp = await SendAsync(HttpMethod.Delete, baseUrl, $"/instance/delete/{Uri.EscapeDataString(instanceName)}", apiKey, null, cancellationToken);
            return resp.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<EvolutionSendResult> SendTextAsync(string baseUrl, string apiKey, string instanceName, string phone, string text, CancellationToken cancellationToken = default)
    {
        var body = new { number = phone, text };
        try
        {
            using var resp = await SendAsync(HttpMethod.Post, baseUrl, $"/message/sendText/{Uri.EscapeDataString(instanceName)}", apiKey, JsonContent.Create(body), cancellationToken);
            if (resp.IsSuccessStatusCode) { return new EvolutionSendResult(true, null); }
            var json = await resp.Content.ReadAsStringAsync(cancellationToken);
            return new EvolutionSendResult(false, $"HTTP {(int)resp.StatusCode}: {Trim(json)}");
        }
        catch (Exception ex)
        {
            return new EvolutionSendResult(false, ex.Message);
        }
    }

    public async Task<EvolutionSendResult> SendMediaAsync(string baseUrl, string apiKey, string instanceName, string phone, string mediatype, string base64, string? mimeType, string? fileName, string? caption, CancellationToken cancellationToken = default)
    {
        var body = new Dictionary<string, object>
        {
            ["number"] = phone,
            ["mediatype"] = mediatype,
            ["media"] = base64,
            ["fileName"] = fileName ?? "archivo"
        };
        if (!string.IsNullOrWhiteSpace(mimeType)) { body["mimetype"] = mimeType!; }
        if (!string.IsNullOrWhiteSpace(caption)) { body["caption"] = caption!; }
        return await PostSendAsync(baseUrl, apiKey, $"/message/sendMedia/{Uri.EscapeDataString(instanceName)}", body, cancellationToken);
    }

    public async Task<EvolutionSendResult> SendAudioAsync(string baseUrl, string apiKey, string instanceName, string phone, string base64, CancellationToken cancellationToken = default)
    {
        var body = new { number = phone, audio = base64 };
        return await PostSendAsync(baseUrl, apiKey, $"/message/sendWhatsAppAudio/{Uri.EscapeDataString(instanceName)}", body, cancellationToken);
    }

    public async Task<EvolutionSendResult> SendLocationAsync(string baseUrl, string apiKey, string instanceName, string phone, double latitude, double longitude, string? name, string? address, CancellationToken cancellationToken = default)
    {
        var body = new { number = phone, name = name ?? "", address = address ?? "", latitude, longitude };
        return await PostSendAsync(baseUrl, apiKey, $"/message/sendLocation/{Uri.EscapeDataString(instanceName)}", body, cancellationToken);
    }

    public async Task<EvolutionSendResult> SetWebhookAsync(string baseUrl, string apiKey, string instanceName, string webhookUrl, string token, CancellationToken cancellationToken = default)
    {
        var body = new
        {
            webhook = new
            {
                enabled = true,
                url = webhookUrl,
                headers = new Dictionary<string, string>
                {
                    ["x-webhook-token"] = token,
                    ["Content-Type"] = "application/json"
                },
                byEvents = false,
                base64 = false,
                events = new[] { "MESSAGES_UPSERT" }
            }
        };
        return await PostSendAsync(baseUrl, apiKey, $"/webhook/set/{Uri.EscapeDataString(instanceName)}", body, cancellationToken);
    }

    private async Task<EvolutionSendResult> PostSendAsync(string baseUrl, string apiKey, string path, object body, CancellationToken ct)
    {
        try
        {
            using var resp = await SendAsync(HttpMethod.Post, baseUrl, path, apiKey, JsonContent.Create(body), ct);
            if (resp.IsSuccessStatusCode) { return new EvolutionSendResult(true, null); }
            var json = await resp.Content.ReadAsStringAsync(ct);
            return new EvolutionSendResult(false, $"HTTP {(int)resp.StatusCode}: {Trim(json)}");
        }
        catch (Exception ex)
        {
            return new EvolutionSendResult(false, ex.Message);
        }
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string baseUrl, string path, string apiKey, HttpContent? content, CancellationToken ct)
    {
        var url = $"{baseUrl.TrimEnd('/')}{path}";
        using var request = new HttpRequestMessage(method, url) { Content = content };
        request.Headers.TryAddWithoutValidation("apikey", apiKey);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(Timeout);
        return await _http.SendAsync(request, cts.Token);
    }

    private static string? ExtractQr(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Object)
        {
            if (root.TryGetProperty("qrcode", out var qr) && qr.ValueKind == JsonValueKind.Object)
            {
                if (qr.TryGetProperty("base64", out var b) && b.ValueKind == JsonValueKind.String) { return b.GetString(); }
                if (qr.TryGetProperty("code", out var c) && c.ValueKind == JsonValueKind.String) { return c.GetString(); }
            }
            if (root.TryGetProperty("base64", out var b2) && b2.ValueKind == JsonValueKind.String) { return b2.GetString(); }
        }
        return null;
    }

    private static string? ExtractState(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Object)
        {
            if (root.TryGetProperty("instance", out var inst) && inst.ValueKind == JsonValueKind.Object
                && inst.TryGetProperty("state", out var s1) && s1.ValueKind == JsonValueKind.String)
            {
                return s1.GetString();
            }
            if (root.TryGetProperty("state", out var s2) && s2.ValueKind == JsonValueKind.String) { return s2.GetString(); }
        }
        return null;
    }

    private static string Trim(string s) => s.Length > 200 ? s[..200] : s;
}
