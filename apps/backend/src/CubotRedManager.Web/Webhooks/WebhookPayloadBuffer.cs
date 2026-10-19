using System.Collections.Concurrent;

namespace CubotRedManager.Web.Webhooks;

/// <summary>
/// Buffer en memoria de los ultimos N payloads de webhook recibidos (YCloud, Evolution, etc.) para
/// diagnosticar sin pelearse con los logs de Railway. Cada entrada guarda metadatos + un preview
/// recortado del body (primeros 4000 chars). No persiste en BD — se pierde al reiniciar el proceso.
///
/// Scope: singleton. Thread-safe por ConcurrentQueue + un lock corto cuando se recorta la cola.
/// Capacidad: 50 por proveedor (configurable). Suficiente para rastrear una ventana de minutos
/// de trafico moderado sin presionar memoria.
///
/// Que NO guarda: credenciales, API keys, tokens — ninguna; solo lo que ya viene en el body del
/// webhook (que es publico para el remitente y no incluye secretos nuestros). Headers: solo los
/// relevantes para diagnostico (content-type, user-agent, x-forwarded-for), sin cookies ni auth.
/// </summary>
public sealed class WebhookPayloadBuffer
{
    public sealed record Entry(
        DateTimeOffset ReceivedAt,
        string Provider,
        string? TenantId,
        int Status,
        string? ContentType,
        string? UserAgent,
        string? RemoteIp,
        int BodyBytes,
        string BodyPreview,
        int ParsedMessages,
        int ParsedReactions,
        string? Notes);

    private const int DefaultCapacity = 50;
    private const int PreviewMaxChars = 4000;
    private readonly ConcurrentDictionary<string, ConcurrentQueue<Entry>> _byProvider = new(StringComparer.OrdinalIgnoreCase);

    public void Record(
        string provider,
        string? tenantId,
        int status,
        string? contentType,
        string? userAgent,
        string? remoteIp,
        string body,
        int parsedMessages,
        int parsedReactions,
        string? notes = null)
    {
        var q = _byProvider.GetOrAdd(provider, _ => new ConcurrentQueue<Entry>());
        q.Enqueue(new Entry(
            ReceivedAt: DateTimeOffset.UtcNow,
            Provider: provider,
            TenantId: tenantId,
            Status: status,
            ContentType: contentType,
            UserAgent: userAgent,
            RemoteIp: remoteIp,
            BodyBytes: body?.Length ?? 0,
            BodyPreview: Preview(body),
            ParsedMessages: parsedMessages,
            ParsedReactions: parsedReactions,
            Notes: notes));
        // Trim: mantener solo las ultimas N entradas. Loop porque pueden entrar varios Enqueue
        // concurrentes entre que leemos Count y hacemos TryDequeue.
        while (q.Count > DefaultCapacity && q.TryDequeue(out _)) { }
    }

    public IReadOnlyList<Entry> Snapshot(string provider, int take)
    {
        if (!_byProvider.TryGetValue(provider, out var q)) { return Array.Empty<Entry>(); }
        var arr = q.ToArray();
        if (take <= 0 || take >= arr.Length) { return arr.Reverse().ToArray(); }
        return arr.Skip(Math.Max(0, arr.Length - take)).Reverse().ToArray();
    }

    private static string Preview(string? body)
    {
        if (string.IsNullOrEmpty(body)) { return string.Empty; }
        return body.Length <= PreviewMaxChars ? body : body[..PreviewMaxChars] + "...[truncated]";
    }
}
