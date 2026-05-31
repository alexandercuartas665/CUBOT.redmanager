using CubotRedManager.Domain.Common;
using CubotRedManager.Domain.Enums;

namespace CubotRedManager.Domain.Entities;

/// <summary>DM, comentario o mencion entrante de una red (Modulo 2.6). Tenant-scoped. Idempotente por (network, external_id).</summary>
public class InboxMessage : TenantEntity
{
    public Guid ClientId { get; set; }
    public Guid? SocialAccountId { get; set; }
    public string NetworkCode { get; set; } = null!;
    public InboxMessageType Type { get; set; } = InboxMessageType.Comment;
    public string ExternalId { get; set; } = null!;
    public string? AuthorExternalId { get; set; }
    public string? AuthorName { get; set; }
    public string? AuthorAvatarUrl { get; set; }
    public string Body { get; set; } = "";
    public string? MediaUrlsJson { get; set; }
    public DateTimeOffset ReceivedAt { get; set; }
    public InboxStatus Status { get; set; } = InboxStatus.Unread;
    public Guid? AssignedTenantUserId { get; set; }
    public Guid? RelatedPublicationId { get; set; }
    public Guid? ParentMessageId { get; set; }

    public ICollection<InboxReply> Replies { get; set; } = new List<InboxReply>();
}
