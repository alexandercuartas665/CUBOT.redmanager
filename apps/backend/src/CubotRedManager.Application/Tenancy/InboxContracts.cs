using CubotRedManager.Domain.Enums;

namespace CubotRedManager.Application.Tenancy;

public sealed record InboxMessageDto(
    Guid Id,
    Guid ClientId,
    string ClientName,
    string NetworkCode,
    InboxMessageType Type,
    string? AuthorName,
    string Body,
    DateTimeOffset ReceivedAt,
    InboxStatus Status,
    int ReplyCount);

public sealed record SimulateInboxRequest(Guid ClientId, string NetworkCode, InboxMessageType Type, string AuthorName, string Body);

/// <summary>Bandeja unificada de DMs/comentarios/menciones (Modulo 2.6). Tenant-scoped.</summary>
public interface IInboxService
{
    Task<int> UnreadCountAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<InboxMessageDto>> ListAsync(InboxStatus? status = null, Guid? clientId = null, CancellationToken cancellationToken = default);
    Task<InboxMessageDto?> ReplyAsync(Guid messageId, string body, Guid actorUserId, CancellationToken cancellationToken = default);
    Task<InboxMessageDto?> SetStatusAsync(Guid messageId, InboxStatus status, Guid actorUserId, CancellationToken cancellationToken = default);
    /// <summary>Inserta un mensaje entrante de prueba (demo; en produccion entra por webhook idempotente).</summary>
    Task<InboxMessageDto?> SimulateIncomingAsync(SimulateInboxRequest request, CancellationToken cancellationToken = default);
}
