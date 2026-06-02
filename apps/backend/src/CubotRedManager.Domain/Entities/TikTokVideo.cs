using CubotRedManager.Domain.Common;

namespace CubotRedManager.Domain.Entities;

/// <summary>
/// Video sincronizado desde TikTok Business API (Modulo 2.4 Sync). Tenant-scoped.
/// Idempotente por (SocialAccountId, ExternalId). Equivale a RC_VIDEOS del sistema VB.NET.
/// Los comentarios del video se guardan como InboxMessage con RelatedVideoExternalId = ExternalId.
/// </summary>
public class TikTokVideo : TenantEntity
{
    public Guid SocialAccountId { get; set; }
    public Guid ClientId { get; set; }

    /// <summary>item_id del video en TikTok.</summary>
    public string ExternalId { get; set; } = null!;

    /// <summary>Caption / titulo del video (puede venir de caption, title o video_description).</summary>
    public string? Caption { get; set; }
    public string? Description { get; set; }

    /// <summary>URL publica del video en tiktok.com.</summary>
    public string? ShareUrl { get; set; }
    public string? EmbedUrl { get; set; }
    public string? ThumbnailUrl { get; set; }

    /// <summary>Fecha de publicacion en TikTok (create_time, Unix timestamp).</summary>
    public DateTimeOffset? PublishedAt { get; set; }

    // Contadores (snapshot del ultimo sync)
    public int CommentCount { get; set; }
    public int LikeCount { get; set; }
    public int ViewCount { get; set; }
    public int ShareCount { get; set; }

    public DateTimeOffset LastSyncAt { get; set; } = DateTimeOffset.UtcNow;
}
