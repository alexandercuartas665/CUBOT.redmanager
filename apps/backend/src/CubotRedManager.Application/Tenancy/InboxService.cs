using CubotRedManager.Application.Abstractions;
using CubotRedManager.Domain.Entities;
using CubotRedManager.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace CubotRedManager.Application.Tenancy;

public sealed class InboxService : IInboxService
{
    private const string TikTok = "tiktok";

    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly ITikTokApiClient _tiktokApi;
    private readonly ITikTokConnectionService _tiktokConnection;
    private readonly ISecretProtector _protector;

    public InboxService(
        IApplicationDbContext db,
        ITenantContext tenantContext,
        ITikTokApiClient tiktokApi,
        ITikTokConnectionService tiktokConnection,
        ISecretProtector protector)
    {
        _db = db;
        _tenantContext = tenantContext;
        _tiktokApi = tiktokApi;
        _tiktokConnection = tiktokConnection;
        _protector = protector;
    }

    public async Task<int> UnreadCountAsync(CancellationToken cancellationToken = default) =>
        await _db.InboxMessages.AsNoTracking().CountAsync(m => m.Status == InboxStatus.Unread, cancellationToken);

    public async Task<int> PendingCountAsync(CancellationToken cancellationToken = default) =>
        // Pendiente = comentario top-level con Status != Replied y sin InboxReply.
        // Misma definicion que usa /tiktok/videos para consistencia entre modulos.
        await _db.InboxMessages.AsNoTracking()
            .CountAsync(m => m.ParentMessageId == null
                          && m.Status != InboxStatus.Replied
                          && m.Status != InboxStatus.Archived
                          && !_db.InboxReplies.Any(r => r.InboxMessageId == m.Id), cancellationToken);

    public async Task<int> DeleteAllAsync(Guid actorUserId, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId) { return 0; }
        // FK ON DELETE CASCADE en inbox_replies se encarga de borrar las respuestas asociadas.
        var count = await _db.InboxMessages
            .Where(m => m.TenantId == tenantId)
            .ExecuteDeleteAsync(cancellationToken);
        return count;
    }

    public async Task<IReadOnlyList<InboxMessageDto>> ListAsync(InboxStatus? status = null, Guid? clientId = null, CancellationToken cancellationToken = default)
    {
        var q = from m in _db.InboxMessages.AsNoTracking()
                join c in _db.Clients.AsNoTracking() on m.ClientId equals c.Id
                select new { m, ClientName = c.Name };
        if (status is { } s) { q = q.Where(x => x.m.Status == s); }
        if (clientId is { } cid) { q = q.Where(x => x.m.ClientId == cid); }

        var rows = await q.OrderByDescending(x => x.m.ReceivedAt).Take(200).ToListAsync(cancellationToken);
        var counts = await ReplyCountsAsync(rows.Select(x => x.m.Id).ToList(), cancellationToken);
        return rows.Select(x => Map(x.m, x.ClientName, counts)).ToList();
    }

    public async Task<IReadOnlyList<InboxMessageDto>> ListCommentsAsync(string? networkCode = null, Guid? socialAccountId = null, string? videoExternalId = null, CancellationToken cancellationToken = default)
    {
        var q = from m in _db.InboxMessages.AsNoTracking()
                join c in _db.Clients.AsNoTracking() on m.ClientId equals c.Id
                where m.Type == InboxMessageType.Comment
                select new { m, ClientName = c.Name };
        if (!string.IsNullOrWhiteSpace(networkCode)) { q = q.Where(x => x.m.NetworkCode == networkCode); }
        if (socialAccountId is { } sid) { q = q.Where(x => x.m.SocialAccountId == sid); }
        if (!string.IsNullOrWhiteSpace(videoExternalId)) { q = q.Where(x => x.m.RelatedVideoExternalId == videoExternalId); }

        var rows = await q.OrderByDescending(x => x.m.ReceivedAt).Take(500).ToListAsync(cancellationToken);
        var counts = await ReplyCountsAsync(rows.Select(x => x.m.Id).ToList(), cancellationToken);
        return rows.Select(x => Map(x.m, x.ClientName, counts)).ToList();
    }

    public async Task<InboxMessageDto?> ReplyAsync(Guid messageId, string body, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var r = await ReplyWithDetailsAsync(messageId, body, actorUserId, cancellationToken);
        return r.Message;
    }

    public async Task<InboxReplyResult> ReplyWithDetailsAsync(Guid messageId, string body, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId) { return new InboxReplyResult(false, "Sin tenant activo.", null, null); }
        if (string.IsNullOrWhiteSpace(body)) { return new InboxReplyResult(false, "Texto vacio.", null, null); }

        var msg = await _db.InboxMessages.FirstOrDefaultAsync(m => m.Id == messageId, cancellationToken);
        if (msg is null) { return new InboxReplyResult(false, "Mensaje no encontrado.", null, null); }

        var trimmed = body.Trim();
        string? remoteId = null;

        // Si es comentario TikTok, publicamos primero en la red. Solo si TikTok acepta, persistimos.
        if (msg.NetworkCode == TikTok && msg.Type == InboxMessageType.Comment && msg.SocialAccountId is Guid accId)
        {
            if (string.IsNullOrEmpty(msg.RelatedVideoExternalId))
            {
                return new InboxReplyResult(false, "Comentario sin video asociado; no se puede responder en TikTok.", null, null);
            }
            var publish = await PublishTikTokReplyAsync(accId, msg.RelatedVideoExternalId!, msg.ExternalId, trimmed, actorUserId, cancellationToken);
            if (!publish.ok)
            {
                return new InboxReplyResult(false, publish.error ?? "TikTok rechazo la respuesta.", null, null);
            }
            remoteId = publish.replyId;
        }

        _db.InboxReplies.Add(new InboxReply
        {
            TenantId = tenantId,
            InboxMessageId = messageId,
            Body = trimmed,
            SentByTenantUserId = actorUserId,
            SentAt = DateTimeOffset.UtcNow,
            Status = "Sent",
            ExternalRefId = remoteId
        });
        msg.Status = InboxStatus.Replied;
        // En el Pipeline TikTok, responder mueve automaticamente a "Contacted".
        if (msg.Type == InboxMessageType.Comment && msg.PipelineStage == CommentPipelineStage.New)
        {
            msg.PipelineStage = CommentPipelineStage.Contacted;
        }
        await _db.SaveChangesAsync(cancellationToken);
        var dto = await OneAsync(messageId, cancellationToken);
        return new InboxReplyResult(true, null, remoteId, dto);
    }

    /// <summary>
    /// Publica un reply en TikTok. Maneja 40105 (token expirado) con refresh + retry una vez.
    /// El reply_id que devuelve TikTok se guarda en InboxReply.ExternalRefId para correlacionar.
    /// </summary>
    private async Task<(bool ok, string? replyId, string? error)> PublishTikTokReplyAsync(
        Guid socialAccountId, string videoExternalId, string commentExternalId, string text, Guid actorUserId, CancellationToken ct)
    {
        var account = await _db.SocialAccounts.FirstOrDefaultAsync(a => a.Id == socialAccountId, ct);
        if (account is null) { return (false, null, "Cuenta social no encontrada."); }
        if (string.IsNullOrEmpty(account.AccessTokenEncrypted)) { return (false, null, "Cuenta sin Access Token. Reconecta."); }
        if (string.IsNullOrEmpty(account.ExternalId)) { return (false, null, "Cuenta sin Business ID (open_id)."); }

        var token = _protector.Unprotect(account.AccessTokenEncrypted);
        var businessId = account.ExternalId;

        async Task<TikTokApiPage> Call(string accessToken) =>
            await _tiktokApi.PostCommentReplyAsync(accessToken, businessId, videoExternalId, commentExternalId, text, ct);

        var resp = await Call(token);
        if (resp.Code == 40105)
        {
            // Token expirado: refresh y retry una sola vez
            var refresh = await _tiktokConnection.RefreshAccountAsync(socialAccountId, actorUserId, ct);
            if (!refresh.Success) { return (false, null, $"Token expirado y no fue posible refrescar: {refresh.Error}"); }
            var refreshed = await _db.SocialAccounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == socialAccountId, ct);
            if (refreshed is null || string.IsNullOrEmpty(refreshed.AccessTokenEncrypted)) { return (false, null, "No se pudo releer el token tras refresh."); }
            token = _protector.Unprotect(refreshed.AccessTokenEncrypted);
            resp = await Call(token);
        }

        if (resp.Code != 0)
        {
            return (false, null, $"TikTok respondio code={resp.Code}: {resp.Message}");
        }

        // Intentar extraer reply_id si TikTok lo devuelve
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(resp.RawJson);
            if (doc.RootElement.TryGetProperty("data", out var data) && data.ValueKind == System.Text.Json.JsonValueKind.Object)
            {
                foreach (var name in new[] { "comment_id", "reply_id", "id" })
                {
                    if (data.TryGetProperty(name, out var p) && p.ValueKind == System.Text.Json.JsonValueKind.String)
                    {
                        return (true, p.GetString(), null);
                    }
                }
            }
        }
        catch { /* opcional */ }
        return (true, null, null);
    }

    public async Task<InboxMessageDto?> SetStatusAsync(Guid messageId, InboxStatus status, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var msg = await _db.InboxMessages.FirstOrDefaultAsync(m => m.Id == messageId, cancellationToken);
        if (msg is null) { return null; }
        msg.Status = status;
        await _db.SaveChangesAsync(cancellationToken);
        return await OneAsync(messageId, cancellationToken);
    }

    public async Task<InboxMessageDto?> SetPipelineStageAsync(Guid messageId, CommentPipelineStage stage, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var msg = await _db.InboxMessages.FirstOrDefaultAsync(m => m.Id == messageId, cancellationToken);
        if (msg is null) { return null; }
        msg.PipelineStage = stage;
        await _db.SaveChangesAsync(cancellationToken);
        return await OneAsync(messageId, cancellationToken);
    }

    public async Task<InboxMessageDto?> SimulateIncomingAsync(SimulateInboxRequest request, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId) { return null; }
        var client = await _db.Clients.FirstOrDefaultAsync(c => c.Id == request.ClientId, cancellationToken);
        if (client is null) { return null; }

        var msg = new InboxMessage
        {
            TenantId = tenantId,
            ClientId = request.ClientId,
            SocialAccountId = request.SocialAccountId,
            NetworkCode = request.NetworkCode,
            Type = request.Type,
            ExternalId = Guid.CreateVersion7().ToString("N"),
            AuthorName = request.AuthorName.Trim(),
            AuthorAvatarUrl = request.AuthorAvatarUrl,
            Body = request.Body.Trim(),
            ReceivedAt = DateTimeOffset.UtcNow,
            Status = InboxStatus.Unread,
            PipelineStage = CommentPipelineStage.New,
            RelatedVideoTitle = request.RelatedVideoTitle,
            RelatedVideoThumbUrl = request.RelatedVideoThumbUrl
        };
        _db.InboxMessages.Add(msg);
        await _db.SaveChangesAsync(cancellationToken);
        return await OneAsync(msg.Id, cancellationToken);
    }

    private async Task<InboxMessageDto?> OneAsync(Guid id, CancellationToken ct)
    {
        var row = await (from m in _db.InboxMessages.AsNoTracking()
                         join c in _db.Clients.AsNoTracking() on m.ClientId equals c.Id
                         where m.Id == id
                         select new { m, c.Name }).FirstOrDefaultAsync(ct);
        if (row is null) { return null; }
        var n = await _db.InboxReplies.CountAsync(r => r.InboxMessageId == id, ct);
        return Map(row.m, row.Name, new Dictionary<Guid, int> { [id] = n });
    }

    private async Task<Dictionary<Guid, int>> ReplyCountsAsync(List<Guid> ids, CancellationToken ct)
    {
        if (ids.Count == 0) { return new(); }
        return await _db.InboxReplies.AsNoTracking()
            .Where(r => ids.Contains(r.InboxMessageId))
            .GroupBy(r => r.InboxMessageId)
            .Select(g => new { g.Key, C = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.C, ct);
    }

    private static InboxMessageDto Map(InboxMessage m, string clientName, IReadOnlyDictionary<Guid, int> counts) =>
        new(m.Id, m.ClientId, clientName, m.NetworkCode, m.Type, m.AuthorName, m.AuthorAvatarUrl, m.Body,
            m.ReceivedAt, m.Status, m.PipelineStage, m.RelatedVideoTitle, m.RelatedVideoThumbUrl,
            m.SocialAccountId, counts.TryGetValue(m.Id, out var n) ? n : 0,
            m.ExternalId, m.ParentMessageId, m.RelatedVideoExternalId);
}
