using CubotRedManager.Application.Abstractions;
using CubotRedManager.Domain.Entities;
using CubotRedManager.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace CubotRedManager.Application.Tenancy;

public sealed class InboxService : IInboxService
{
    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenantContext;

    public InboxService(IApplicationDbContext db, ITenantContext tenantContext)
    {
        _db = db;
        _tenantContext = tenantContext;
    }

    public async Task<int> UnreadCountAsync(CancellationToken cancellationToken = default) =>
        await _db.InboxMessages.AsNoTracking().CountAsync(m => m.Status == InboxStatus.Unread, cancellationToken);

    public async Task<IReadOnlyList<InboxMessageDto>> ListAsync(InboxStatus? status = null, Guid? clientId = null, CancellationToken cancellationToken = default)
    {
        var q = from m in _db.InboxMessages.AsNoTracking()
                join c in _db.Clients.AsNoTracking() on m.ClientId equals c.Id
                select new { m, ClientName = c.Name };
        if (status is { } s) { q = q.Where(x => x.m.Status == s); }
        if (clientId is { } cid) { q = q.Where(x => x.m.ClientId == cid); }

        var rows = await q.OrderByDescending(x => x.m.ReceivedAt).Take(200).ToListAsync(cancellationToken);
        var counts = await _db.InboxReplies.AsNoTracking().GroupBy(r => r.InboxMessageId)
            .Select(g => new { g.Key, C = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.C, cancellationToken);

        return rows.Select(x => new InboxMessageDto(x.m.Id, x.m.ClientId, x.ClientName, x.m.NetworkCode, x.m.Type,
            x.m.AuthorName, x.m.Body, x.m.ReceivedAt, x.m.Status, counts.TryGetValue(x.m.Id, out var n) ? n : 0)).ToList();
    }

    public async Task<InboxMessageDto?> ReplyAsync(Guid messageId, string body, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId) { return null; }
        var msg = await _db.InboxMessages.FirstOrDefaultAsync(m => m.Id == messageId, cancellationToken);
        if (msg is null || string.IsNullOrWhiteSpace(body)) { return null; }

        _db.InboxReplies.Add(new InboxReply
        {
            TenantId = tenantId,
            InboxMessageId = messageId,
            Body = body.Trim(),
            SentByTenantUserId = actorUserId,
            SentAt = DateTimeOffset.UtcNow,
            Status = "Sent"
        });
        msg.Status = InboxStatus.Replied;
        await _db.SaveChangesAsync(cancellationToken);
        return await OneAsync(messageId, cancellationToken);
    }

    public async Task<InboxMessageDto?> SetStatusAsync(Guid messageId, InboxStatus status, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var msg = await _db.InboxMessages.FirstOrDefaultAsync(m => m.Id == messageId, cancellationToken);
        if (msg is null) { return null; }
        msg.Status = status;
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
            NetworkCode = request.NetworkCode,
            Type = request.Type,
            ExternalId = Guid.CreateVersion7().ToString("N"),
            AuthorName = request.AuthorName.Trim(),
            Body = request.Body.Trim(),
            ReceivedAt = DateTimeOffset.UtcNow,
            Status = InboxStatus.Unread
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
        return new InboxMessageDto(row.m.Id, row.m.ClientId, row.Name, row.m.NetworkCode, row.m.Type,
            row.m.AuthorName, row.m.Body, row.m.ReceivedAt, row.m.Status, n);
    }
}
