namespace CubotRedManager.Application.Tenancy;

/// <summary>Resultado agregado de una operacion de sincronizacion (videos y/o comentarios).</summary>
public sealed record TikTokSyncResult(
    bool Success,
    int VideosInserted,
    int VideosUpdated,
    int CommentsInserted,
    int CommentsUpdated,
    int RepliesInserted,
    int Errors,
    string Trace,
    string? ErrorMessage)
{
    public int TotalChanges => VideosInserted + VideosUpdated + CommentsInserted + CommentsUpdated + RepliesInserted;
}

/// <summary>DTO de un video TikTok almacenado localmente.</summary>
public sealed record TikTokVideoDto(
    Guid Id,
    Guid SocialAccountId,
    Guid ClientId,
    string ClientName,
    string ExternalId,
    string? Caption,
    string? Description,
    string? ShareUrl,
    string? ThumbnailUrl,
    DateTimeOffset? PublishedAt,
    int CommentCount,
    int LikeCount,
    int ViewCount,
    DateTimeOffset LastSyncAt,
    int LocalCommentCount,
    /// <summary>Comentarios del video sin responder (Status != Replied y sin ParentMessageId).</summary>
    int PendingCount);

/// <summary>Stats agregados de una cuenta TikTok (header del modulo videos).</summary>
public sealed record TikTokAccountStats(
    int TotalVideos,
    int TotalComments,
    int PendingComments,
    int RepliedComments);

/// <summary>
/// Sincronizacion de TikTok Business API (videos + comentarios + replies). Reusa los servicios
/// ya construidos (TikTokConnectionService para refresh, ITikTokApiClient para HTTP).
/// Mapeo: videos -> TikTokVideo; comentarios -> InboxMessage(Type=Comment); replies -> InboxMessage(Type=Comment + ParentMessageId).
/// Manejo automatico del error 40105 (token expirado): refresh + retry una sola vez por sync.
/// </summary>
public interface ITikTokSyncService
{
    /// <summary>Sincroniza videos de una cuenta TikTok (cursor-based). Limita por maxVideos.</summary>
    Task<TikTokSyncResult> SyncVideosAsync(Guid socialAccountId, int maxVideos, Guid actorUserId, CancellationToken cancellationToken = default);

    /// <summary>Sincroniza comentarios + replies de TODOS los videos almacenados de una cuenta.</summary>
    Task<TikTokSyncResult> SyncCommentsAsync(Guid socialAccountId, Guid actorUserId, CancellationToken cancellationToken = default);

    /// <summary>Sincroniza videos y luego comentarios en orden.</summary>
    Task<TikTokSyncResult> SyncAllAsync(Guid socialAccountId, int maxVideos, Guid actorUserId, CancellationToken cancellationToken = default);

    /// <summary>Lista videos almacenados localmente para una cuenta TikTok.</summary>
    Task<IReadOnlyList<TikTokVideoDto>> ListVideosAsync(Guid socialAccountId, bool onlyWithPending = false, string? textFilter = null, CancellationToken cancellationToken = default);

    /// <summary>Stats agregados (KPIs del header).</summary>
    Task<TikTokAccountStats> GetStatsAsync(Guid socialAccountId, CancellationToken cancellationToken = default);

    /// <summary>Re-sincroniza comentarios + replies de UN solo video. Para refrescar bajo demanda desde el detalle.</summary>
    Task<TikTokSyncResult> SyncCommentsForVideoAsync(Guid socialAccountId, string videoExternalId, Guid actorUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Borra TODOS los videos sincronizados del tenant + sus comentarios (InboxMessage tipo
    /// Comment de la red TikTok). Las InboxReplies asociadas se borran en cascada por FK.
    /// NO toca: SocialAccount, AutoReplyConfig, AutoReplyJobLogs, TikTokAppConfig.
    /// Devuelve (cuantos videos, cuantos comentarios) borrados.
    /// </summary>
    Task<(int videos, int comments)> DeleteAllVideosAsync(Guid actorUserId, CancellationToken cancellationToken = default);
}
