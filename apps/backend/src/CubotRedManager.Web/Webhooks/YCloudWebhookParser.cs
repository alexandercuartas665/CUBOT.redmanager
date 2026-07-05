using System.Text.Json;

namespace CubotRedManager.Web.Webhooks;

/// <summary>
/// Parser tolerante del payload de webhooks entrantes de YCloud. YCloud puede entregar el JSON
/// como un arreglo de eventos o como un objeto con {"messages":[...]} segun la version del canal.
/// Se ignoran los eventos que no sean mensajes de WhatsApp entrantes.
/// </summary>
public static class YCloudWebhookParser
{
    public sealed record InboundMessage(string Wamid, string FromPhone, string ToPhone, string? Text, string? MessageType, DateTimeOffset ReceivedAt);

    /// <summary>Extrae los mensajes entrantes del payload. Devuelve lista vacia si no aplica.</summary>
    public static List<InboundMessage> Parse(JsonDocument doc)
    {
        var list = new List<InboundMessage>();
        var root = doc.RootElement;

        if (root.ValueKind == JsonValueKind.Array)
        {
            foreach (var el in root.EnumerateArray()) { TryExtract(el, list); }
        }
        else if (root.ValueKind == JsonValueKind.Object)
        {
            // Formato {"messages":[...]} o {"whatsappInboundMessage":{...}} o evento suelto.
            if (root.TryGetProperty("messages", out var arr) && arr.ValueKind == JsonValueKind.Array)
            {
                foreach (var el in arr.EnumerateArray()) { TryExtract(el, list); }
            }
            else
            {
                TryExtract(root, list);
            }
        }
        return list;
    }

    private static void TryExtract(JsonElement el, List<InboundMessage> list)
    {
        // YCloud v2: cada evento trae "type": "whatsapp.inbound_message.received" +
        // "whatsappInboundMessage": { wamid, from, to, type, text: { body }, timestamp }
        JsonElement msg;
        if (el.TryGetProperty("whatsappInboundMessage", out var em)) { msg = em; }
        else if (el.TryGetProperty("message", out var em2)) { msg = em2; }
        else { msg = el; }

        var wamid = TryStr(msg, "wamid") ?? TryStr(msg, "id");
        var from = TryStr(msg, "from");
        var to = TryStr(msg, "to");
        if (string.IsNullOrWhiteSpace(wamid) || string.IsNullOrWhiteSpace(from) || string.IsNullOrWhiteSpace(to))
        {
            return;
        }
        var type = TryStr(msg, "type") ?? "text";
        string? text = null;
        if (msg.TryGetProperty("text", out var txt) && txt.ValueKind == JsonValueKind.Object)
        {
            text = TryStr(txt, "body");
        }
        // El timestamp de Meta viene como epoch en segundos; YCloud lo entrega como iso-8601
        // en la mayoria de canales. Intento ambos formatos.
        DateTimeOffset received = DateTimeOffset.UtcNow;
        if (msg.TryGetProperty("timestamp", out var tsEl))
        {
            if (tsEl.ValueKind == JsonValueKind.String && DateTimeOffset.TryParse(tsEl.GetString(), out var dto))
            {
                received = dto;
            }
            else if (tsEl.ValueKind == JsonValueKind.Number && tsEl.TryGetInt64(out var epoch))
            {
                received = DateTimeOffset.FromUnixTimeSeconds(epoch);
            }
            else if (tsEl.ValueKind == JsonValueKind.String && long.TryParse(tsEl.GetString(), out var epochStr))
            {
                received = DateTimeOffset.FromUnixTimeSeconds(epochStr);
            }
        }

        list.Add(new InboundMessage(wamid!, from!, to!, text, type, received));
    }

    private static string? TryStr(JsonElement el, string prop)
        => el.ValueKind == JsonValueKind.Object && el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;
}
