using System.Text.Json;
using CubotRedManager.Domain.Enums;

namespace CubotRedManager.Application.Tenancy;

/// <summary>
/// Mensaje entrante normalizado a partir del webhook crudo de Evolution. Si el cliente envio un
/// adjunto y Evolution incluyo su contenido en base64 en el webhook, MediaBase64/MediaFileName lo
/// traen para que el handler lo guarde en /uploads/chat y se vea inline en el chat.
/// </summary>
public sealed record ParsedInbound(Guid TenantId, IngestMessageRequest Payload, string? MediaBase64 = null, string? MediaFileName = null,
    bool IsTakeControlCommand = false);

/// <summary>
/// Traduce el payload crudo del webhook de Evolution (evento messages.upsert) a nuestro
/// formato de ingesta. El tenant se deduce del nombre de instancia cubot_{tenant}_{linea}.
/// Devuelve null si el evento no es un mensaje entrante procesable.
/// Portado 1:1 desde CUBOT.travels.
/// </summary>
public static class EvolutionWebhookParser
{
    public static ParsedInbound? Parse(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object) { return null; }

        // Evento: solo messages.upsert (entrante).
        if (root.TryGetProperty("event", out var ev) && ev.ValueKind == JsonValueKind.String)
        {
            var name = ev.GetString()!.Replace("_", ".").ToLowerInvariant();
            if (name != "messages.upsert") { return null; }
        }

        if (!root.TryGetProperty("instance", out var instEl) || instEl.ValueKind != JsonValueKind.String) { return null; }
        var (tenantId, lineId) = TenantAndLineFromInstance(instEl.GetString()!);
        if (tenantId is null) { return null; }

        if (!root.TryGetProperty("data", out var data)) { return null; }
        if (data.ValueKind == JsonValueKind.Array)
        {
            data = data.EnumerateArray().FirstOrDefault();
        }
        if (data.ValueKind != JsonValueKind.Object) { return null; }

        // Reacciones (emoji sobre un mensaje): WhatsApp las envia como messages.upsert con
        // reactionMessage. NO son un mensaje de texto del cliente: no deben persistirse como mensaje
        // (saldria "(mensaje no soportado)") ni disparar al agente. Las ignoramos por completo.
        if (data.TryGetProperty("message", out var reactProbe) && reactProbe.ValueKind == JsonValueKind.Object
            && reactProbe.TryGetProperty("reactionMessage", out _))
        {
            return null;
        }

        if (!data.TryGetProperty("key", out var key) || key.ValueKind != JsonValueKind.Object) { return null; }

        var isFromMe = key.TryGetProperty("fromMe", out var fromMe) && fromMe.ValueKind == JsonValueKind.True;

        if (!key.TryGetProperty("remoteJid", out var jidEl) || jidEl.ValueKind != JsonValueKind.String) { return null; }
        var jid = jidEl.GetString()!;
        if (jid.Contains("@g.us")) { return null; } // grupos no soportados
        var phone = new string(jid.TakeWhile(c => c != '@').Where(char.IsDigit).ToArray());
        if (string.IsNullOrEmpty(phone)) { return null; }

        var externalId = key.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.String
            ? idEl.GetString()!
            : Guid.NewGuid().ToString("N");

        var name2 = data.TryGetProperty("pushName", out var pn) && pn.ValueKind == JsonValueKind.String ? pn.GetString() : null;
        var content = ExtractContent(data);

        DateTimeOffset? sentAt = null;
        if (data.TryGetProperty("messageTimestamp", out var ts) && ts.ValueKind == JsonValueKind.Number && ts.TryGetInt64(out var secs))
        {
            sentAt = DateTimeOffset.FromUnixTimeSeconds(secs);
        }

        // Mensajes propios (fromMe): normalmente son eco de lo que enviamos y se ignoran. EXCEPCION: si el
        // asesor escribio el comando de toma de control "Manejo_asesor" desde el WhatsApp del negocio, lo
        // dejamos pasar como comando para bloquear el numero del chat (remoteJid = el cliente).
        if (isFromMe)
        {
            if (AgentControlCommands.IsTakeControl(content.Body))
            {
                var cmdPayload = new IngestMessageRequest(phone, name2, externalId, content.Body!.Trim(), "text", sentAt, lineId);
                return new ParsedInbound(tenantId.Value, cmdPayload, IsTakeControlCommand: true);
            }
            return null;
        }

        var body = string.IsNullOrWhiteSpace(content.Body) ? "(mensaje no soportado)" : content.Body!;
        var msgType = content.MediaType == MessageMediaType.None ? "text" : content.MediaType.ToString().ToLowerInvariant();
        var payload = new IngestMessageRequest(
            phone, name2, externalId, body, msgType, sentAt, lineId,
            content.MediaType, content.LocationUrl, content.Mime);
        return new ParsedInbound(tenantId.Value, payload, content.Base64, content.FileName);
    }

    private sealed record InboundContent(string? Body, MessageMediaType MediaType, string? Mime, string? FileName, string? Base64, string? LocationUrl);

    /// <summary>
    /// Extrae el contenido del mensaje entrante: texto, o el tipo de adjunto (imagen/audio/video/
    /// documento/ubicacion) con su MIME, nombre de archivo y -si Evolution lo incluyo en el
    /// webhook- su contenido base64 para guardarlo y mostrarlo inline.
    /// </summary>
    private static InboundContent ExtractContent(JsonElement data)
    {
        if (!data.TryGetProperty("message", out var msg) || msg.ValueKind != JsonValueKind.Object)
        {
            return new InboundContent(null, MessageMediaType.None, null, null, null, null);
        }

        if (msg.TryGetProperty("conversation", out var c) && c.ValueKind == JsonValueKind.String)
        {
            return new InboundContent(c.GetString(), MessageMediaType.None, null, null, null, null);
        }
        if (msg.TryGetProperty("extendedTextMessage", out var ext) && ext.ValueKind == JsonValueKind.Object
            && ext.TryGetProperty("text", out var t) && t.ValueKind == JsonValueKind.String)
        {
            return new InboundContent(t.GetString(), MessageMediaType.None, null, null, null, null);
        }

        // base64 puede venir a nivel data o message (si la instancia Evolution lo manda en el webhook).
        var topB64 = ReadString(data, "base64") ?? ReadString(msg, "base64");

        if (msg.TryGetProperty("imageMessage", out var im) && im.ValueKind == JsonValueKind.Object)
        {
            return new InboundContent(Caption(im, "Imagen"), MessageMediaType.Image, ReadString(im, "mimetype"), "imagen.jpg", topB64 ?? ReadString(im, "base64"), null);
        }
        if (msg.TryGetProperty("audioMessage", out var au) && au.ValueKind == JsonValueKind.Object)
        {
            return new InboundContent("Nota de voz", MessageMediaType.Audio, ReadString(au, "mimetype") ?? "audio/ogg", "audio.ogg", topB64 ?? ReadString(au, "base64"), null);
        }
        if (msg.TryGetProperty("videoMessage", out var vi) && vi.ValueKind == JsonValueKind.Object)
        {
            return new InboundContent(Caption(vi, "Video"), MessageMediaType.Video, ReadString(vi, "mimetype"), "video.mp4", topB64 ?? ReadString(vi, "base64"), null);
        }
        if (msg.TryGetProperty("documentMessage", out var doc) && doc.ValueKind == JsonValueKind.Object)
        {
            var fn = ReadString(doc, "fileName") ?? ReadString(doc, "title") ?? "documento";
            return new InboundContent("Documento: " + fn, MessageMediaType.Document, ReadString(doc, "mimetype"), fn, topB64 ?? ReadString(doc, "base64"), null);
        }
        if (msg.TryGetProperty("stickerMessage", out var st) && st.ValueKind == JsonValueKind.Object)
        {
            return new InboundContent("Sticker", MessageMediaType.Image, "image/webp", "sticker.webp", topB64 ?? ReadString(st, "base64"), null);
        }
        if (msg.TryGetProperty("locationMessage", out var loc) && loc.ValueKind == JsonValueKind.Object)
        {
            var lat = ReadDouble(loc, "degreesLatitude");
            var lng = ReadDouble(loc, "degreesLongitude");
            if (lat is not null && lng is not null)
            {
                var ci = System.Globalization.CultureInfo.InvariantCulture;
                return new InboundContent("Ubicacion", MessageMediaType.Location, null, null, null,
                    $"{lat.Value.ToString(ci)},{lng.Value.ToString(ci)}");
            }
        }
        return new InboundContent(null, MessageMediaType.None, null, null, null, null);
    }

    private static string Caption(JsonElement media, string fallback)
        => media.TryGetProperty("caption", out var cap) && cap.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(cap.GetString())
            ? cap.GetString()! : fallback;

    private static string? ReadString(JsonElement obj, string prop)
        => obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static double? ReadDouble(JsonElement obj, string prop)
        => obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var d) ? d : null;

    // Instancia cubot_{tenant:N}_{linea:N} -> (tenant, linea opcional).
    private static (Guid? Tenant, Guid? Line) TenantAndLineFromInstance(string instance)
    {
        const string prefix = "cubot_";
        if (!instance.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) { return (null, null); }
        var rest = instance[prefix.Length..];
        var sep = rest.IndexOf('_');
        var tenantPart = sep > 0 ? rest[..sep] : rest;
        if (!Guid.TryParseExact(tenantPart, "N", out var tenant)) { return (null, null); }

        Guid? line = null;
        if (sep > 0 && sep + 1 < rest.Length)
        {
            var linePart = rest[(sep + 1)..];
            if (Guid.TryParseExact(linePart, "N", out var l)) { line = l; }
        }
        return (tenant, line);
    }
}
