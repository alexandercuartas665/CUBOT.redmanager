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

    /// <summary>
    /// Etapa del embudo comercial (TikTok Manager Pipeline). Por defecto New al ingresar.
    /// Solo se usa cuando Type=Comment; para DMs/Menciones se ignora en UI.
    /// </summary>
    public CommentPipelineStage PipelineStage { get; set; } = CommentPipelineStage.New;

    /// <summary>Titulo del video al que pertenece el comentario (se llena cuando aplica).</summary>
    public string? RelatedVideoTitle { get; set; }
    /// <summary>Thumbnail del video, si esta disponible.</summary>
    public string? RelatedVideoThumbUrl { get; set; }
    /// <summary>External id del video en la red (TikTok video_id, IG media_id, etc.).</summary>
    public string? RelatedVideoExternalId { get; set; }

    public ICollection<InboxReply> Replies { get; set; } = new List<InboxReply>();
}
