using CubotRedManager.Domain.Enums;

namespace CubotRedManager.Application.Tenancy;

public sealed record InboxMessageDto(
    Guid Id,
    Guid ClientId,
    string ClientName,
    string NetworkCode,
    InboxMessageType Type,
    string? AuthorName,
    string? AuthorAvatarUrl,
    string Body,
    DateTimeOffset ReceivedAt,
    InboxStatus Status,
    CommentPipelineStage PipelineStage,
    string? RelatedVideoTitle,
    string? RelatedVideoThumbUrl,
    Guid? SocialAccountId,
    int ReplyCount,
    string ExternalId,
    Guid? ParentMessageId,
    string? RelatedVideoExternalId);

/// <summary>Resultado detallado de un reply (publicado o fallido).</summary>
public sealed record InboxReplyResult(bool Success, string? Error, string? RemoteReplyId, InboxMessageDto? Message);

public sealed record SimulateInboxRequest(
    Guid ClientId,
    string NetworkCode,
    InboxMessageType Type,
    string AuthorName,
    string Body,
    string? AuthorAvatarUrl = null,
    string? RelatedVideoTitle = null,
    string? RelatedVideoThumbUrl = null,
    Guid? SocialAccountId = null);

/// <summary>Bandeja unificada de DMs/comentarios/menciones (Modulo 2.6). Tenant-scoped.</summary>
public interface IInboxService
{
    Task<int> UnreadCountAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<InboxMessageDto>> ListAsync(InboxStatus? status = null, Guid? clientId = null, CancellationToken cancellationToken = default);

    /// <summary>Lista comentarios para el Pipeline TikTok (Type=Comment, opcionalmente filtrado por red/cuenta/video).</summary>
    Task<IReadOnlyList<InboxMessageDto>> ListCommentsAsync(string? networkCode = null, Guid? socialAccountId = null, string? videoExternalId = null, CancellationToken cancellationToken = default);

    Task<InboxMessageDto?> ReplyAsync(Guid messageId, string body, Guid actorUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Como ReplyAsync, pero retorna ademas el error si la publicacion remota fallo. Para
    /// comentarios TikTok publica primero en la red via API; solo si la API responde code=0
    /// se persiste el reply local. Si TikTok falla, no se persiste nada.
    /// </summary>
    Task<InboxReplyResult> ReplyWithDetailsAsync(Guid messageId, string body, Guid actorUserId, CancellationToken cancellationToken = default);
    Task<InboxMessageDto?> SetStatusAsync(Guid messageId, InboxStatus status, Guid actorUserId, CancellationToken cancellationToken = default);

    /// <summary>Cambia la etapa del embudo de un comentario (Pipeline TikTok Manager).</summary>
    Task<InboxMessageDto?> SetPipelineStageAsync(Guid messageId, CommentPipelineStage stage, Guid actorUserId, CancellationToken cancellationToken = default);

    /// <summary>Inserta un mensaje entrante de prueba (demo; en produccion entra por webhook idempotente).</summary>
    Task<InboxMessageDto?> SimulateIncomingAsync(SimulateInboxRequest request, CancellationToken cancellationToken = default);
}
