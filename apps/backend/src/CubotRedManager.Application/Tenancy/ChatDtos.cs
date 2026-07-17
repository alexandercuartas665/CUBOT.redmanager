using CubotRedManager.Domain.Enums;

namespace CubotRedManager.Application.Tenancy;

public sealed record ConversationDto(
    Guid Id,
    string ContactPhone,
    string? ContactName,
    Guid? LeadId,
    DateTimeOffset? LastMessageAt,
    Guid? WhatsAppLineId = null);

public sealed record MessageDto(
    Guid Id,
    Guid ConversationId,
    MessageDirection Direction,
    string Body,
    string MessageType,
    DateTimeOffset SentAt,
    MessageMediaType MediaType = MessageMediaType.None,
    string? MediaUrl = null,
    string? MediaMimeType = null,
    string? SentByName = null,
    /// <summary>Emoji con el que el agente reacciono a este mensaje entrante (null = sin reaccion).</summary>
    string? Reaction = null);

/// <summary>Payload normalizado del webhook entrante (lo produce el Evolution Connector).</summary>
public sealed record IngestMessageRequest(
    string ContactPhone,
    string? ContactName,
    string ExternalMessageId,
    string Body,
    string? MessageType = null,
    DateTimeOffset? SentAt = null,
    /// <summary>Linea por donde entro el mensaje. Si esta presente y tiene un binding de
    /// agente activo, el ChatIngestService dispara el AgentDispatcher tras persistir.</summary>
    Guid? WhatsAppLineId = null,
    /// <summary>Tipo de adjunto del mensaje entrante (imagen/audio/video/documento/ubicacion).</summary>
    MessageMediaType MediaType = MessageMediaType.None,
    /// <summary>URL local del adjunto ya guardado (/uploads/chat/...) o "lat,lng" para ubicacion.</summary>
    string? MediaUrl = null,
    /// <summary>MIME del adjunto entrante.</summary>
    string? MediaMimeType = null);

public sealed record SendMessageRequest(string Body);

/// <summary>Resultado de enviar un mensaje por una linea WhatsApp (Evolution real).</summary>
public sealed record ChatSendResult(bool Ok, MessageDto? Message, string? Error);
