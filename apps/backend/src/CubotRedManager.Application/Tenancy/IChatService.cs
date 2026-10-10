namespace CubotRedManager.Application.Tenancy;

/// <summary>
/// Servicio de chat WhatsApp para la ficha del lead en Pipelines.
/// Resuelve la conversacion por LeadId (si el lead ya tiene la columna ContactPhone)
/// o directamente por telefono (contactos manuales). Portado reducido desde CUBOT.travels:
/// esta primera version solo cubre texto; media/voz/location queda para una iteracion
/// posterior.
/// </summary>
public interface IChatService
{
    /// <summary>Devuelve o crea la Conversation asociada al Lead. Tenant-scoped.</summary>
    Task<ChatConversationDto?> GetOrCreateForLeadAsync(Guid leadId, CancellationToken cancellationToken = default);

    /// <summary>Devuelve o crea la Conversation asociada a un telefono (contactos sin lead).</summary>
    Task<ChatConversationDto?> GetOrCreateForPhoneAsync(string contactPhone, string? contactName, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MessageDto>> ListMessagesAsync(Guid conversationId, int take = 100, CancellationToken cancellationToken = default);

    /// <summary>Envia un mensaje outbound de texto por una linea WhatsApp. Persiste el Message en
    /// BD, llama al IWhatsAppConnectorService (si la linea es Emulator es no-op, igual que el
    /// agente IA) y broadcastea por SignalR.</summary>
    Task<ChatSendResult> SendViaLineAsync(Guid conversationId, Guid whatsAppLineId, string body, Guid actorUserId, string? actorDisplayName = null, CancellationToken cancellationToken = default);
}

public sealed record ChatConversationDto(
    Guid Id,
    string ContactPhone,
    string? ContactName,
    Guid? WhatsAppLineId,
    DateTimeOffset? LastMessageAt);
// NOTA: ChatSendResult vive en ChatDtos.cs (shape: Ok, Message, Error).
