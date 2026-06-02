using System.Text;
using System.Text.Json;
using CubotRedManager.Application.Abstractions;
using CubotRedManager.Domain.Entities;
using CubotRedManager.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace CubotRedManager.Application.Tenancy;

/// <summary>
/// Orquesta la sincronizacion de videos y comentarios de TikTok Business. Porta la logica
/// del control VB.NET (ctrlVideos / ctrlBandeja):
///  - Paginacion cursor-based para videos, page-based para comentarios, cursor para replies.
///  - Mapeo tolerante a renombres de campos entre versiones de la API.
///  - Auto-refresh del token UNA sola vez al recibir code=40105.
///  - Upsert idempotente por (SocialAccountId, ExternalId).
///  - Replies se guardan como InboxMessage con ParentMessageId; si la API devuelve replies,
///    el comentario raiz se marca como Replied (el VB.NET hace lo mismo).
/// </summary>
public sealed class TikTokSyncService : ITikTokSyncService
{
    private const string Network = "tiktok";
    private const int MaxVideosPerPage = 20;
    private const int MaxCommentsPerPage = 20;
    private const int MaxReplyPagesPerComment = 3;
    private const int MaxVideosCap = 500;

    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly ITikTokApiClient _api;
    private readonly ITikTokConnectionService _connection;
    private readonly ISecretProtector _protector;
    private readonly IAuditWriter _audit;
    private readonly TimeProvider _time;

    public TikTokSyncService(
        IApplicationDbContext db,
        ITenantContext tenantContext,
        ITikTokApiClient api,
        ITikTokConnectionService connection,
        ISecretProtector protector,
        IAuditWriter audit,
        TimeProvider time)
    {
        _db = db;
        _tenantContext = tenantContext;
        _api = api;
        _connection = connection;
        _protector = protector;
        _audit = audit;
        _time = time;
    }

    public async Task<IReadOnlyList<TikTokVideoDto>> ListVideosAsync(Guid socialAccountId, bool onlyWithPending = false, string? textFilter = null, CancellationToken cancellationToken = default)
    {
        var q = from v in _db.TikTokVideos.AsNoTracking()
                join c in _db.Clients.AsNoTracking() on v.ClientId equals c.Id
                where v.SocialAccountId == socialAccountId
                select new { v, c.Name };
        if (!string.IsNullOrWhiteSpace(textFilter))
        {
            var t = textFilter.Trim();
            q = q.Where(x => (x.v.Caption ?? "").Contains(t) || x.v.ExternalId.Contains(t));
        }
        var rows = await q.OrderByDescending(x => x.v.PublishedAt).Take(500).ToListAsync(cancellationToken);
        var ids = rows.Select(r => r.v.ExternalId).ToList();

        // Total local de comentarios por video (raices + replies).
        var localCounts = await _db.InboxMessages.AsNoTracking()
            .Where(m => m.SocialAccountId == socialAccountId && m.Type == InboxMessageType.Comment && ids.Contains(m.RelatedVideoExternalId!))
            .GroupBy(m => m.RelatedVideoExternalId!)
            .Select(g => new { g.Key, C = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.C, cancellationToken);

        // Pendientes = comentarios RAIZ (sin parent) cuyo Status no es Replied.
        var pendingCounts = await _db.InboxMessages.AsNoTracking()
            .Where(m => m.SocialAccountId == socialAccountId
                        && m.Type == InboxMessageType.Comment
                        && m.ParentMessageId == null
                        && m.Status != InboxStatus.Replied
                        && ids.Contains(m.RelatedVideoExternalId!))
            .GroupBy(m => m.RelatedVideoExternalId!)
            .Select(g => new { g.Key, C = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.C, cancellationToken);

        var result = rows.Select(r => new TikTokVideoDto(
            r.v.Id, r.v.SocialAccountId, r.v.ClientId, r.Name,
            r.v.ExternalId, r.v.Caption, r.v.Description, r.v.ShareUrl, r.v.ThumbnailUrl, r.v.PublishedAt,
            r.v.CommentCount, r.v.LikeCount, r.v.ViewCount, r.v.LastSyncAt,
            localCounts.TryGetValue(r.v.ExternalId, out var n) ? n : 0,
            pendingCounts.TryGetValue(r.v.ExternalId, out var p) ? p : 0));
        if (onlyWithPending) { result = result.Where(d => d.PendingCount > 0); }
        return result.ToList();
    }

    public async Task<TikTokAccountStats> GetStatsAsync(Guid socialAccountId, CancellationToken cancellationToken = default)
    {
        var totalVideos = await _db.TikTokVideos.AsNoTracking()
            .Where(v => v.SocialAccountId == socialAccountId).CountAsync(cancellationToken);
        var rootComments = _db.InboxMessages.AsNoTracking()
            .Where(m => m.SocialAccountId == socialAccountId
                        && m.Type == InboxMessageType.Comment
                        && m.ParentMessageId == null);
        var totalComments = await rootComments.CountAsync(cancellationToken);
        var replied = await rootComments.Where(m => m.Status == InboxStatus.Replied).CountAsync(cancellationToken);
        return new TikTokAccountStats(totalVideos, totalComments, totalComments - replied, replied);
    }

    public async Task<TikTokSyncResult> SyncCommentsForVideoAsync(Guid socialAccountId, string videoExternalId, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var trace = new StringBuilder();
        var (account, token, businessId, errInit) = await LoadAccountAsync(socialAccountId, trace, cancellationToken);
        if (account is null || token is null || businessId is null)
        {
            return new TikTokSyncResult(false, 0, 0, 0, 0, 0, 1, trace.ToString(), errInit);
        }

        var video = await _db.TikTokVideos.AsNoTracking()
            .FirstOrDefaultAsync(v => v.SocialAccountId == socialAccountId && v.ExternalId == videoExternalId, cancellationToken);
        if (video is null)
        {
            trace.AppendLine("[ERROR] Video no encontrado en BD.");
            return new TikTokSyncResult(false, 0, 0, 0, 0, 0, 1, trace.ToString(), "Video no encontrado.");
        }

        trace.AppendLine($"[VIDEO] {Truncate(video.Caption ?? video.ExternalId, 40)} ({video.ExternalId})");
        var totalIns = 0; var totalUpd = 0; var totalReplies = 0; var errors = 0;
        var refreshed = false;
        var page = 1;
        var hasMore = true;

        while (hasMore && page <= 10)
        {
            var resp = await _api.ListCommentsAsync(token, businessId, video.ExternalId, page, MaxCommentsPerPage, cancellationToken);
            if (resp.Code == 40105 && !refreshed)
            {
                trace.AppendLine("  [INFO] Token expirado. Refrescando...");
                var nt = await TryRefreshAsync(socialAccountId, actorUserId, trace, cancellationToken);
                if (nt is null) { errors++; break; }
                token = nt; refreshed = true;
                continue;
            }
            if (resp.Code != 0)
            {
                trace.AppendLine($"  [ERROR] code={resp.Code}: {resp.Message}");
                errors++; break;
            }
            using var doc = JsonDocument.Parse(resp.RawJson);
            if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object) { break; }
            var arr = GetArrayProp(data, "comments", "comment_list", "list", "items");
            if (arr is null || arr.Value.GetArrayLength() == 0) { break; }

            var procesados = 0;
            foreach (var c in arr.Value.EnumerateArray())
            {
                if (c.ValueKind != JsonValueKind.Object) { continue; }
                var commentId = GetStr(c, "comment_id", "id", "cid");
                if (string.IsNullOrEmpty(commentId)) { continue; }
                var (inserted, parentMsg) = await UpsertCommentAsync(account, video, commentId, c, null, null, cancellationToken);
                if (inserted) { totalIns++; } else { totalUpd++; }
                procesados++;
                var rc = GetInt(c, "reply_count", "comment_reply_count", "replies");
                if (rc > 0 && parentMsg is not null)
                {
                    totalReplies += await SyncRepliesAsync(account, video, commentId, parentMsg, token, businessId, trace, cancellationToken);
                }
            }
            trace.AppendLine($"  [OK] Pag {page}: {procesados} comentarios.");
            hasMore = ParseHasMore(data);
            if (!hasMore && procesados >= MaxCommentsPerPage) { hasMore = true; }
            page++;
        }

        _audit.Write(actorUserId, "tiktok.sync-comments-video", nameof(TikTokVideo), video.Id,
            previousValue: null, newValue: new { totalIns, totalUpd, totalReplies, video.ExternalId }, tenantId: account.TenantId);
        await _db.SaveChangesAsync(cancellationToken);
        trace.AppendLine($"[FIN] {totalIns} nuevos, {totalUpd} actualizados, {totalReplies} replies, {errors} errores.");
        return new TikTokSyncResult(errors == 0, 0, 0, totalIns, totalUpd, totalReplies, errors, trace.ToString(), null);
    }

    public async Task<TikTokSyncResult> SyncVideosAsync(Guid socialAccountId, int maxVideos, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var trace = new StringBuilder();
        var (account, token, businessId, errInit) = await LoadAccountAsync(socialAccountId, trace, cancellationToken);
        if (account is null || token is null || businessId is null)
        {
            return new TikTokSyncResult(false, 0, 0, 0, 0, 0, 1, trace.ToString(), errInit);
        }

        var totalInserted = 0;
        var totalUpdated = 0;
        var capped = Math.Clamp(maxVideos, 1, MaxVideosCap);
        var cursor = 0L;
        var hasMore = true;
        var pageNum = 1;
        var totalProcessed = 0;
        var refreshed = false;

        trace.AppendLine($"[INFO] Sync videos: cuenta={businessId}, max={capped}");

        while (hasMore && totalProcessed < capped && pageNum <= 50)
        {
            var batchSize = Math.Min(MaxVideosPerPage, capped - totalProcessed);
            trace.AppendLine($"[INFO] Pagina {pageNum} (cursor={cursor}): pidiendo {batchSize} videos...");

            var page = await _api.ListVideosAsync(token, businessId, cursor, batchSize, cancellationToken);
            if (page.Code == 40105 && !refreshed)
            {
                trace.AppendLine("[INFO] Token expirado (40105). Refrescando...");
                var newToken = await TryRefreshAsync(socialAccountId, actorUserId, trace, cancellationToken);
                if (newToken is null) { return new TikTokSyncResult(false, totalInserted, totalUpdated, 0, 0, 0, 1, trace.ToString(), "Refresh fallido."); }
                token = newToken; refreshed = true;
                continue; // reintenta misma pagina
            }
            if (page.Code != 0)
            {
                trace.AppendLine($"[ERROR] TikTok respondio code={page.Code}: {page.Message}");
                break;
            }

            // Parsear data.list (tolerante a 4 nombres)
            using var doc = JsonDocument.Parse(page.RawJson);
            if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
            {
                trace.AppendLine("[WARN] Sin campo 'data'. Fin de paginacion.");
                break;
            }
            var videosArr = GetArrayProp(data, "list", "videos", "items", "video_list");
            if (videosArr is null || videosArr.Value.GetArrayLength() == 0)
            {
                trace.AppendLine($"[INFO] Pagina {pageNum}: sin videos devueltos. Fin.");
                break;
            }

            var processedPage = 0;
            foreach (var v in videosArr.Value.EnumerateArray())
            {
                if (v.ValueKind != JsonValueKind.Object) { continue; }
                var externalId = GetStr(v, "item_id", "id");
                if (string.IsNullOrEmpty(externalId)) { continue; }

                var existing = await _db.TikTokVideos.FirstOrDefaultAsync(
                    x => x.SocialAccountId == socialAccountId && x.ExternalId == externalId, cancellationToken);

                // TikTok devuelve la descripcion en "caption" (video_description NO esta permitido).
                var caption = GetStr(v, "caption", "title");
                var description = caption;
                var shareUrl = GetStr(v, "share_url");
                var embedUrl = GetStr(v, "embed_url", "embed_link");
                var thumb = GetStr(v, "thumbnail_url", "cover_image_url");
                var comments = GetInt(v, "comments", "comment_count");
                var likes = GetInt(v, "likes", "like_count");
                var views = GetInt(v, "video_views", "view_count");
                var shares = GetInt(v, "shares", "share_count");
                var publishedAt = GetUnixTimestamp(v, "create_time");

                if (existing is null)
                {
                    _db.TikTokVideos.Add(new TikTokVideo
                    {
                        TenantId = account.TenantId,
                        SocialAccountId = socialAccountId,
                        ClientId = account.ClientId,
                        ExternalId = externalId,
                        Caption = caption,
                        Description = description,
                        ShareUrl = shareUrl,
                        EmbedUrl = embedUrl,
                        ThumbnailUrl = thumb,
                        PublishedAt = publishedAt,
                        CommentCount = comments,
                        LikeCount = likes,
                        ViewCount = views,
                        ShareCount = shares,
                        LastSyncAt = _time.GetUtcNow()
                    });
                    totalInserted++;
                }
                else
                {
                    existing.Caption = caption; existing.Description = description;
                    existing.ShareUrl = shareUrl; existing.EmbedUrl = embedUrl; existing.ThumbnailUrl = thumb;
                    existing.PublishedAt = publishedAt;
                    existing.CommentCount = comments; existing.LikeCount = likes;
                    existing.ViewCount = views; existing.ShareCount = shares;
                    existing.LastSyncAt = _time.GetUtcNow();
                    totalUpdated++;
                }
                processedPage++; totalProcessed++;
                if (totalProcessed >= capped) { break; }
            }

            await _db.SaveChangesAsync(cancellationToken);
            trace.AppendLine($"[OK] Pagina {pageNum}: procesados={processedPage} (acum: +{totalInserted}/~{totalUpdated})");

            // Leer cursor + has_more (tolerante)
            if (data.TryGetProperty("cursor", out var cElem))
            {
                if (cElem.ValueKind == JsonValueKind.Number && cElem.TryGetInt64(out var nc)) { cursor = nc; }
                else if (cElem.ValueKind == JsonValueKind.String && long.TryParse(cElem.GetString(), out var ncs)) { cursor = ncs; }
            }
            hasMore = ParseHasMore(data);
            pageNum++;
        }

        // Actualizar LastSyncAt en la cuenta
        var acc = await _db.SocialAccounts.FirstOrDefaultAsync(a => a.Id == socialAccountId, cancellationToken);
        if (acc is not null) { acc.LastSyncAt = _time.GetUtcNow(); }
        await _db.SaveChangesAsync(cancellationToken);

        trace.AppendLine($"[FIN] Videos: {totalInserted} nuevos, {totalUpdated} actualizados.");
        _audit.Write(actorUserId, "tiktok.sync-videos", nameof(SocialAccount), socialAccountId,
            previousValue: null, newValue: new { totalInserted, totalUpdated }, tenantId: account.TenantId);
        // Flush del audit (Write solo agrega al ChangeTracker; sin SaveChanges aqui no se persiste
        // si nadie despues hace SaveChanges en el mismo scope).
        await _db.SaveChangesAsync(cancellationToken);

        return new TikTokSyncResult(true, totalInserted, totalUpdated, 0, 0, 0, 0, trace.ToString(), null);
    }

    public async Task<TikTokSyncResult> SyncCommentsAsync(Guid socialAccountId, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var trace = new StringBuilder();
        var (account, token, businessId, errInit) = await LoadAccountAsync(socialAccountId, trace, cancellationToken);
        if (account is null || token is null || businessId is null)
        {
            return new TikTokSyncResult(false, 0, 0, 0, 0, 0, 1, trace.ToString(), errInit);
        }

        var videos = await _db.TikTokVideos.AsNoTracking()
            .Where(v => v.SocialAccountId == socialAccountId)
            .OrderByDescending(v => v.PublishedAt)
            .Take(200)
            .ToListAsync(cancellationToken);

        if (videos.Count == 0)
        {
            trace.AppendLine("[WARN] Sin videos locales. Sincroniza videos primero.");
            return new TikTokSyncResult(false, 0, 0, 0, 0, 0, 0, trace.ToString(), "Sin videos locales.");
        }

        trace.AppendLine($"[INFO] Sync comentarios: {videos.Count} videos a procesar.");
        var totalCommentsInserted = 0;
        var totalCommentsUpdated = 0;
        var totalRepliesInserted = 0;
        var errors = 0;
        var refreshed = false;

        foreach (var video in videos)
        {
            if (cancellationToken.IsCancellationRequested) { break; }
            trace.AppendLine($"[VIDEO] {Truncate(video.Caption ?? video.ExternalId, 40)} ({video.ExternalId})");

            var page = 1;
            var hasMore = true;
            while (hasMore && page <= 5)
            {
                var resp = await _api.ListCommentsAsync(token, businessId, video.ExternalId, page, MaxCommentsPerPage, cancellationToken);
                if (resp.Code == 40105 && !refreshed)
                {
                    trace.AppendLine("  [INFO] Token expirado. Refrescando...");
                    var nt = await TryRefreshAsync(socialAccountId, actorUserId, trace, cancellationToken);
                    if (nt is null) { errors++; break; }
                    token = nt; refreshed = true;
                    continue;
                }
                if (resp.Code != 0)
                {
                    trace.AppendLine($"  [ERROR] code={resp.Code}: {resp.Message}");
                    errors++;
                    break;
                }

                using var doc = JsonDocument.Parse(resp.RawJson);
                if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object) { break; }
                var commentsArr = GetArrayProp(data, "comments", "comment_list", "list", "items");
                if (commentsArr is null || commentsArr.Value.GetArrayLength() == 0)
                {
                    trace.AppendLine($"  [INFO] Pag {page}: sin comentarios.");
                    break;
                }

                var processedInPage = 0;
                foreach (var c in commentsArr.Value.EnumerateArray())
                {
                    if (c.ValueKind != JsonValueKind.Object) { continue; }
                    var commentId = GetStr(c, "comment_id", "id", "cid");
                    if (string.IsNullOrEmpty(commentId)) { continue; }

                    var (inserted, parentMsg) = await UpsertCommentAsync(
                        account, video, commentId, c, parentExternalId: null, parentMessageId: null, cancellationToken);
                    if (inserted) { totalCommentsInserted++; } else { totalCommentsUpdated++; }
                    processedInPage++;

                    // Sincronizar replies si reply_count > 0
                    var replyCount = GetInt(c, "reply_count", "comment_reply_count", "replies");
                    if (replyCount > 0 && parentMsg is not null)
                    {
                        totalRepliesInserted += await SyncRepliesAsync(account, video, commentId, parentMsg, token, businessId, trace, cancellationToken);
                    }
                }

                trace.AppendLine($"  [OK] Pag {page}: {processedInPage} comentarios.");
                hasMore = ParseHasMore(data);
                if (!hasMore && processedInPage >= MaxCommentsPerPage) { hasMore = true; }
                page++;
            }
        }

        // Actualizar LastSyncAt
        var acc = await _db.SocialAccounts.FirstOrDefaultAsync(a => a.Id == socialAccountId, cancellationToken);
        if (acc is not null) { acc.LastSyncAt = _time.GetUtcNow(); await _db.SaveChangesAsync(cancellationToken); }

        trace.AppendLine($"[FIN] Comentarios: {totalCommentsInserted} nuevos, {totalCommentsUpdated} actualizados, {totalRepliesInserted} replies, {errors} errores.");
        _audit.Write(actorUserId, "tiktok.sync-comments", nameof(SocialAccount), socialAccountId,
            previousValue: null, newValue: new { totalCommentsInserted, totalCommentsUpdated, totalRepliesInserted }, tenantId: account.TenantId);
        // Flush del audit (sin esto el row queda solo en el ChangeTracker).
        await _db.SaveChangesAsync(cancellationToken);

        return new TikTokSyncResult(errors == 0, 0, 0, totalCommentsInserted, totalCommentsUpdated, totalRepliesInserted, errors, trace.ToString(), null);
    }

    public async Task<TikTokSyncResult> SyncAllAsync(Guid socialAccountId, int maxVideos, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var r1 = await SyncVideosAsync(socialAccountId, maxVideos, actorUserId, cancellationToken);
        if (!r1.Success) { return r1; }
        var r2 = await SyncCommentsAsync(socialAccountId, actorUserId, cancellationToken);
        return new TikTokSyncResult(
            r1.Success && r2.Success,
            r1.VideosInserted, r1.VideosUpdated,
            r2.CommentsInserted, r2.CommentsUpdated, r2.RepliesInserted,
            r1.Errors + r2.Errors,
            r1.Trace + Environment.NewLine + r2.Trace,
            r2.ErrorMessage ?? r1.ErrorMessage);
    }

    // ----- Helpers internos -----

    private async Task<(SocialAccount? account, string? token, string? businessId, string? error)> LoadAccountAsync(
        Guid socialAccountId, StringBuilder trace, CancellationToken ct)
    {
        if (_tenantContext.TenantId is null) { return (null, null, null, "Sin tenant activo."); }

        var account = await _db.SocialAccounts.FirstOrDefaultAsync(a => a.Id == socialAccountId && a.NetworkCode == Network, ct);
        if (account is null) { trace.AppendLine("[ERROR] Cuenta no encontrada."); return (null, null, null, "Cuenta no encontrada."); }
        if (string.IsNullOrEmpty(account.AccessTokenEncrypted)) { trace.AppendLine("[ERROR] Sin Access Token. Reconecta la cuenta."); return (null, null, null, "Sin Access Token."); }
        if (string.IsNullOrEmpty(account.ExternalId)) { trace.AppendLine("[ERROR] Sin Business ID (open_id). Reconecta la cuenta."); return (null, null, null, "Sin Business ID."); }

        var token = _protector.Unprotect(account.AccessTokenEncrypted);
        trace.AppendLine($"[OK] Credenciales cargadas. Business ID: {account.ExternalId}");
        return (account, token, account.ExternalId, null);
    }

    private async Task<string?> TryRefreshAsync(Guid socialAccountId, Guid actorUserId, StringBuilder trace, CancellationToken ct)
    {
        var r = await _connection.RefreshAccountAsync(socialAccountId, actorUserId, ct);
        trace.AppendLine(r.Trace.TrimEnd());
        if (!r.Success)
        {
            trace.AppendLine($"[ERROR] Refresh fallido: {r.Error}");
            return null;
        }
        // Releer token recien guardado
        var acc = await _db.SocialAccounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == socialAccountId, ct);
        if (acc is null || string.IsNullOrEmpty(acc.AccessTokenEncrypted)) { return null; }
        return _protector.Unprotect(acc.AccessTokenEncrypted);
    }

    private async Task<(bool inserted, InboxMessage? msg)> UpsertCommentAsync(
        SocialAccount account, TikTokVideo video, string commentId, JsonElement c,
        string? parentExternalId, Guid? parentMessageId, CancellationToken ct)
    {
        var text = GetStr(c, "text", "comment_text", "content");
        var authorName = GetStr(c, "author_name", "username");
        var authorId = GetStr(c, "author_id", "open_id");
        string? authorAvatar = null;

        // Si viene un sub-objeto "user", probar campos adicionales
        if (c.TryGetProperty("user", out var u) && u.ValueKind == JsonValueKind.Object)
        {
            if (string.IsNullOrEmpty(authorName)) { authorName = GetStr(u, "username", "display_name", "nickname"); }
            if (string.IsNullOrEmpty(authorId)) { authorId = GetStr(u, "id", "uid"); }
            authorAvatar = GetStr(u, "avatar_url", "avatar");
        }
        var createdAt = GetUnixTimestamp(c, "create_time") ?? _time.GetUtcNow();

        var existing = await _db.InboxMessages.FirstOrDefaultAsync(
            m => m.NetworkCode == Network && m.ExternalId == commentId, ct);
        if (existing is not null)
        {
            existing.Body = text ?? existing.Body;
            existing.AuthorName = authorName ?? existing.AuthorName;
            existing.AuthorAvatarUrl = authorAvatar ?? existing.AuthorAvatarUrl;
            return (false, existing);
        }

        var msg = new InboxMessage
        {
            TenantId = account.TenantId,
            ClientId = account.ClientId,
            SocialAccountId = account.Id,
            NetworkCode = Network,
            Type = InboxMessageType.Comment,
            ExternalId = commentId,
            AuthorExternalId = authorId,
            AuthorName = authorName,
            AuthorAvatarUrl = authorAvatar,
            Body = text ?? "",
            ReceivedAt = createdAt,
            Status = InboxStatus.Unread,
            PipelineStage = CommentPipelineStage.New,
            RelatedVideoExternalId = video.ExternalId,
            RelatedVideoTitle = video.Caption,
            RelatedVideoThumbUrl = video.ThumbnailUrl,
            ParentMessageId = parentMessageId
        };
        _db.InboxMessages.Add(msg);
        await _db.SaveChangesAsync(ct);
        return (true, msg);
    }

    private async Task<int> SyncRepliesAsync(SocialAccount account, TikTokVideo video, string parentCommentId,
        InboxMessage parentMsg, string token, string businessId, StringBuilder trace, CancellationToken ct)
    {
        var inserted = 0;
        var cursor = 0L;
        var hasMore = true;
        var pages = 0;
        var hadAny = false;

        while (hasMore && pages < MaxReplyPagesPerComment)
        {
            var resp = await _api.ListRepliesAsync(token, businessId, video.ExternalId, parentCommentId, cursor, 20, ct);
            if (resp.Code != 0) { break; }
            using var doc = JsonDocument.Parse(resp.RawJson);
            if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object) { break; }
            var arr = GetArrayProp(data, "replies", "reply_list", "list", "items", "comments");
            if (arr is null || arr.Value.GetArrayLength() == 0) { break; }
            hadAny = true;

            foreach (var r in arr.Value.EnumerateArray())
            {
                if (r.ValueKind != JsonValueKind.Object) { continue; }
                var replyId = GetStr(r, "reply_id", "comment_id", "id");
                if (string.IsNullOrEmpty(replyId)) { continue; }
                var (ins, _) = await UpsertCommentAsync(account, video, replyId, r, parentCommentId, parentMsg.Id, ct);
                if (ins) { inserted++; }
            }

            hasMore = ParseHasMore(data);
            if (data.TryGetProperty("cursor", out var ce))
            {
                if (ce.ValueKind == JsonValueKind.Number && ce.TryGetInt64(out var nc)) { cursor = nc; }
                else if (ce.ValueKind == JsonValueKind.String && long.TryParse(ce.GetString(), out var ncs)) { cursor = ncs; }
            }
            pages++;
        }

        // Si la API devolvio al menos una reply, el comentario raiz se considera respondido.
        if (hadAny && parentMsg.Status != InboxStatus.Replied)
        {
            parentMsg.Status = InboxStatus.Replied;
            if (parentMsg.PipelineStage == CommentPipelineStage.New)
            {
                parentMsg.PipelineStage = CommentPipelineStage.Contacted;
            }
            await _db.SaveChangesAsync(ct);
        }
        return inserted;
    }

    // ----- Parsing tolerante (TikTok renombra campos entre versiones) -----

    private static JsonElement? GetArrayProp(JsonElement obj, params string[] names)
    {
        foreach (var n in names)
        {
            if (obj.TryGetProperty(n, out var p) && p.ValueKind == JsonValueKind.Array)
            {
                return p;
            }
        }
        return null;
    }

    private static string? GetStr(JsonElement obj, params string[] names)
    {
        foreach (var n in names)
        {
            if (obj.TryGetProperty(n, out var p))
            {
                var s = p.ValueKind switch
                {
                    JsonValueKind.String => p.GetString(),
                    JsonValueKind.Number => p.ToString(),
                    _ => null
                };
                if (!string.IsNullOrWhiteSpace(s)) { return s.Trim(); }
            }
        }
        return null;
    }

    private static int GetInt(JsonElement obj, params string[] names)
    {
        foreach (var n in names)
        {
            if (obj.TryGetProperty(n, out var p))
            {
                if (p.ValueKind == JsonValueKind.Number && p.TryGetInt32(out var v)) { return v; }
                if (p.ValueKind == JsonValueKind.String && int.TryParse(p.GetString(), out var s)) { return s; }
            }
        }
        return 0;
    }

    private static DateTimeOffset? GetUnixTimestamp(JsonElement obj, string name)
    {
        if (!obj.TryGetProperty(name, out var p)) { return null; }
        long ts = 0;
        if (p.ValueKind == JsonValueKind.Number && p.TryGetInt64(out var n)) { ts = n; }
        else if (p.ValueKind == JsonValueKind.String && long.TryParse(p.GetString(), out var s)) { ts = s; }
        if (ts <= 0) { return null; }
        return DateTimeOffset.FromUnixTimeSeconds(ts);
    }

    private static bool ParseHasMore(JsonElement data)
    {
        if (!data.TryGetProperty("has_more", out var hm)) { return false; }
        return hm.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String => hm.GetString()?.Trim().ToLowerInvariant() is "true" or "1",
            JsonValueKind.Number => hm.GetInt32() != 0,
            _ => false
        };
    }

    private static string Truncate(string s, int n) => s.Length <= n ? s : s[..n] + "...";
}
