using System.Net.Http.Json;
using System.Text.Json;
using CubotRedManager.Application.Tenancy;

namespace CubotRedManager.Infrastructure.YCloud;

/// <summary>
/// Implementacion de IYCloudApiClient contra la API v2 de YCloud (api.ycloud.com). Usa
/// HttpClient inyectado por DI (registrar via AddHttpClient). La API key se entrega por llamada
/// en el header X-API-Key — este cliente NO conoce la entidad WhatsAppLine ni el ISecretProtector.
/// Errores HTTP de YCloud se mapean a Error string en el result (sin throw).
/// </summary>
internal sealed class YCloudApiClient : IYCloudApiClient
{
    private const string ApiBase = "https://api.ycloud.com/v2";

    private readonly HttpClient _http;

    public YCloudApiClient(HttpClient http)
    {
        _http = http;
    }

    public async Task<YCloudCheckResult> CheckAsync(string apiKey, string? phoneNumber, CancellationToken cancellationToken = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, $"{ApiBase}/whatsapp/phoneNumbers?limit=10");
        req.Headers.Add("X-API-Key", apiKey);
        try
        {
            using var res = await _http.SendAsync(req, cancellationToken);
            var body = await res.Content.ReadAsStringAsync(cancellationToken);
            if (!res.IsSuccessStatusCode)
            {
                return new YCloudCheckResult(false, null, null, ExtractError(body) ?? $"HTTP {(int)res.StatusCode}");
            }
            using var doc = JsonDocument.Parse(body);
            string? verifiedPhone = null;
            string? wabaId = null;
            if (doc.RootElement.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array && items.GetArrayLength() > 0)
            {
                var first = items[0];
                verifiedPhone = TryStr(first, "phoneNumber") ?? TryStr(first, "displayPhoneNumber");
                wabaId = TryStr(first, "wabaId") ?? TryStr(first, "whatsappBusinessAccountId");
                if (!string.IsNullOrWhiteSpace(phoneNumber))
                {
                    foreach (var it in items.EnumerateArray())
                    {
                        var pn = TryStr(it, "phoneNumber") ?? TryStr(it, "displayPhoneNumber");
                        if (pn is not null && pn.Replace("+", "") == phoneNumber.Replace("+", ""))
                        {
                            verifiedPhone = pn;
                            wabaId = TryStr(it, "wabaId") ?? TryStr(it, "whatsappBusinessAccountId") ?? wabaId;
                            break;
                        }
                    }
                }
            }
            return new YCloudCheckResult(true, verifiedPhone, wabaId, null);
        }
        catch (Exception ex)
        {
            return new YCloudCheckResult(false, null, null, ex.Message);
        }
    }

    public Task<YCloudSendResult> SendTextAsync(string apiKey, string fromPhone, string toPhone, string text, CancellationToken cancellationToken = default)
    {
        var payload = new Dictionary<string, object?>
        {
            ["from"] = fromPhone,
            ["to"] = toPhone,
            ["type"] = "text",
            ["text"] = new { body = text }
        };
        return SendMessageAsync(apiKey, payload, cancellationToken);
    }

    public Task<YCloudSendResult> SendMediaAsync(string apiKey, string fromPhone, string toPhone, YCloudMediaKind kind, string mediaUrl, string? caption, string? fileName, CancellationToken cancellationToken = default)
    {
        var typeKey = kind switch
        {
            YCloudMediaKind.Image => "image",
            YCloudMediaKind.Video => "video",
            YCloudMediaKind.Audio => "audio",
            YCloudMediaKind.Document => "document",
            _ => "document"
        };
        var mediaBody = new Dictionary<string, object?> { ["link"] = mediaUrl };
        if (!string.IsNullOrWhiteSpace(caption) && kind != YCloudMediaKind.Audio) { mediaBody["caption"] = caption; }
        if (kind == YCloudMediaKind.Document && !string.IsNullOrWhiteSpace(fileName)) { mediaBody["filename"] = fileName; }
        var payload = new Dictionary<string, object?>
        {
            ["from"] = fromPhone,
            ["to"] = toPhone,
            ["type"] = typeKey,
            [typeKey] = mediaBody
        };
        return SendMessageAsync(apiKey, payload, cancellationToken);
    }

    public Task<YCloudSendResult> SendTemplateAsync(string apiKey, string fromPhone, string toPhone, string templateName, string language, IReadOnlyList<string> bodyParams, string? headerMediaType = null, string? headerMediaUrl = null, string? headerMediaFileName = null, IReadOnlyList<WhatsAppUrlButtonParam>? urlButtons = null, CancellationToken cancellationToken = default)
    {
        // Payload de plantilla (WhatsApp/YCloud v2): componentes con los parametros en orden.
        // Header de media (imagen/documento/video): { type:"header", parameters:[{ type:"image", image:{ link } }] }.
        // Body: { type:"body", parameters:[{ type:"text", text }] }. Si no hay variables ni header, se omiten.
        var components = new List<object>();
        if (!string.IsNullOrWhiteSpace(headerMediaType) && !string.IsNullOrWhiteSpace(headerMediaUrl))
        {
            var kind = headerMediaType!.Trim().ToLowerInvariant();   // "image" | "document" | "video"
            var mediaObj = new Dictionary<string, object?> { ["link"] = headerMediaUrl!.Trim() };
            // Solo el DOCUMENTO admite nombre visible: sin el, WhatsApp lo muestra como "Sin titulo". Se usa el
            // nombre dado o, en su defecto, el ultimo segmento de la URL (decodificado).
            if (kind == "document")
            {
                var fileName = !string.IsNullOrWhiteSpace(headerMediaFileName)
                    ? headerMediaFileName!.Trim()
                    : FileNameFromUrl(headerMediaUrl!);
                if (!string.IsNullOrWhiteSpace(fileName)) { mediaObj["filename"] = fileName; }
            }
            components.Add(new Dictionary<string, object?>
            {
                ["type"] = "header",
                ["parameters"] = new[] { new Dictionary<string, object?> { ["type"] = kind, [kind] = mediaObj } }
            });
        }
        if (bodyParams.Count > 0)
        {
            components.Add(new
            {
                type = "body",
                parameters = bodyParams.Select(p => new { type = "text", text = p }).ToArray()
            });
        }
        // Botones URL DINAMICOS: un componente por boton con su indice y el sufijo que reemplaza {{1}}.
        // { type:"button", sub_type:"url", index:N, parameters:[{ type:"text", text }] }.
        if (urlButtons is { Count: > 0 })
        {
            foreach (var b in urlButtons)
            {
                components.Add(new Dictionary<string, object?>
                {
                    ["type"] = "button",
                    ["sub_type"] = "url",
                    ["index"] = b.Index,
                    ["parameters"] = new[] { new Dictionary<string, object?> { ["type"] = "text", ["text"] = b.Text } }
                });
            }
        }
        var template = new Dictionary<string, object?>
        {
            ["name"] = templateName,
            ["language"] = new { code = language }
        };
        if (components.Count > 0) { template["components"] = components; }
        var payload = new Dictionary<string, object?>
        {
            ["from"] = fromPhone,
            ["to"] = toPhone,
            ["type"] = "template",
            ["template"] = template
        };
        return SendMessageAsync(apiKey, payload, cancellationToken);
    }

    public Task<YCloudSendResult> SendReactionAsync(string apiKey, string fromPhone, string toPhone, string messageId, string emoji, CancellationToken cancellationToken = default)
    {
        // WhatsApp/YCloud v2: mensaje type=reaction con el wamid del mensaje a reaccionar y el emoji.
        // Un emoji vacio quita la reaccion (comportamiento nativo de WhatsApp).
        var payload = new Dictionary<string, object?>
        {
            ["from"] = fromPhone,
            ["to"] = toPhone,
            ["type"] = "reaction",
            ["reaction"] = new { message_id = messageId, emoji }
        };
        return SendMessageAsync(apiKey, payload, cancellationToken);
    }

    // Ultimo segmento de una URL como nombre de archivo (decodificado), o null. Da titulo al documento del header.
    private static string? FileNameFromUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) { return null; }
        var noQuery = url.Split('?', '#')[0].TrimEnd('/');
        var last = noQuery.Split('/').LastOrDefault();
        if (string.IsNullOrWhiteSpace(last)) { return null; }
        try { return Uri.UnescapeDataString(last); } catch { return last; }
    }

    private async Task<YCloudSendResult> SendMessageAsync(string apiKey, object payload, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, $"{ApiBase}/whatsapp/messages");
        req.Headers.Add("X-API-Key", apiKey);
        req.Content = JsonContent.Create(payload);
        try
        {
            using var res = await _http.SendAsync(req, ct);
            var body = await res.Content.ReadAsStringAsync(ct);
            if (!res.IsSuccessStatusCode)
            {
                return new YCloudSendResult(false, null, ExtractError(body) ?? $"HTTP {(int)res.StatusCode}");
            }
            using var doc = JsonDocument.Parse(body);
            var id = TryStr(doc.RootElement, "wamid") ?? TryStr(doc.RootElement, "id");
            return new YCloudSendResult(true, id, null);
        }
        catch (Exception ex)
        {
            return new YCloudSendResult(false, null, ex.Message);
        }
    }

    public async Task<YCloudTemplateResult> CreateTemplateAsync(string apiKey, string wabaId, string name, string language, string category, object components, int? ttlSeconds, CancellationToken cancellationToken = default)
    {
        var payload = new Dictionary<string, object?>
        {
            ["wabaId"] = wabaId,
            ["name"] = name,
            ["language"] = language,
            ["category"] = category,
            ["components"] = components
        };
        if (ttlSeconds is int ttl) { payload["messageSendTtlSeconds"] = ttl; }

        using var req = new HttpRequestMessage(HttpMethod.Post, $"{ApiBase}/whatsapp/templates");
        req.Headers.Add("X-API-Key", apiKey);
        req.Content = JsonContent.Create(payload);
        try
        {
            using var res = await _http.SendAsync(req, cancellationToken);
            var body = await res.Content.ReadAsStringAsync(cancellationToken);
            if (!res.IsSuccessStatusCode)
            {
                return new YCloudTemplateResult(false, null, null, ExtractError(body) ?? $"HTTP {(int)res.StatusCode}");
            }
            using var doc = JsonDocument.Parse(body);
            var id = TryStr(doc.RootElement, "id");
            var status = TryStr(doc.RootElement, "status");
            return new YCloudTemplateResult(true, id, status, null);
        }
        catch (Exception ex)
        {
            return new YCloudTemplateResult(false, null, null, ex.Message);
        }
    }

    public async Task<YCloudTemplateListResult> ListTemplatesAsync(string apiKey, string wabaId, CancellationToken cancellationToken = default)
    {
        const int pageSize = 100;
        const int maxPages = 50;
        var list = new List<YCloudTemplateStatus>();
        var offset = 0;
        for (var page = 0; page < maxPages; page++)
        {
            using var req = new HttpRequestMessage(HttpMethod.Get,
                $"{ApiBase}/whatsapp/templates?wabaId={Uri.EscapeDataString(wabaId)}&limit={pageSize}&offset={offset}");
            req.Headers.Add("X-API-Key", apiKey);
            int pageCount;
            try
            {
                using var res = await _http.SendAsync(req, cancellationToken);
                var body = await res.Content.ReadAsStringAsync(cancellationToken);
                if (!res.IsSuccessStatusCode)
                {
                    return new YCloudTemplateListResult(false, list, ExtractError(body) ?? $"HTTP {(int)res.StatusCode}");
                }
                using var doc = JsonDocument.Parse(body);
                pageCount = 0;
                if (doc.RootElement.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
                {
                    pageCount = items.GetArrayLength();
                    foreach (var it in items.EnumerateArray())
                    {
                        // YCloudTemplateParser extrae components HEADER/BODY/FOOTER/BUTTONS en
                        // campos utiles (headerText, bodyText, varExamples, buttons). ImportFromYCloudAsync
                        // ya puede hidratar el BodyText real sin placeholder.
                        var parsed = YCloudTemplateParser.ParseItem(it);
                        if (parsed is not null) { list.Add(parsed); }
                    }
                }
            }
            catch (Exception ex)
            {
                return new YCloudTemplateListResult(false, list, ex.Message);
            }
            if (pageCount < pageSize) { break; }
            offset += pageSize;
        }
        return new YCloudTemplateListResult(true, list, null);
    }

    private static string? TryStr(JsonElement el, string prop)
        => el.ValueKind == JsonValueKind.Object && el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;

    /// <summary>Extrae un mensaje de error del body de YCloud. Soporta {"error":{"message":...}}
    /// y {"message":...}. Si no aplica, devuelve null (el caller usa el HTTP code).</summary>
    private static string? ExtractError(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) { return null; }
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (root.TryGetProperty("error", out var err))
            {
                if (err.ValueKind == JsonValueKind.String) { return err.GetString(); }
                if (err.ValueKind == JsonValueKind.Object && err.TryGetProperty("message", out var em)) { return em.GetString(); }
            }
            if (root.TryGetProperty("message", out var msg)) { return msg.GetString(); }
        }
        catch { /* swallow */ }
        return null;
    }
}
