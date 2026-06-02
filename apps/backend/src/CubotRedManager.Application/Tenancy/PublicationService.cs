using System.Text.Json;
using CubotRedManager.Application.Abstractions;
using CubotRedManager.Domain.Entities;
using CubotRedManager.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace CubotRedManager.Application.Tenancy;

public sealed class PublicationService : IPublicationService
{
    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly IAuditWriter _audit;

    public PublicationService(IApplicationDbContext db, ITenantContext tenantContext, IAuditWriter audit)
    {
        _db = db;
        _tenantContext = tenantContext;
        _audit = audit;
    }

    public async Task<IReadOnlyList<PublicationDto>> ListAsync(DateTimeOffset? from = null, DateTimeOffset? to = null, CancellationToken cancellationToken = default)
    {
        var q = from p in _db.Publications.AsNoTracking()
                join c in _db.Clients.AsNoTracking() on p.ClientId equals c.Id
                select new { p, ClientName = c.Name };
        if (from is { } f) { q = q.Where(x => x.p.ScheduledAt >= f); }
        if (to is { } t) { q = q.Where(x => x.p.ScheduledAt <= t); }

        var rows = await q.OrderBy(x => x.p.ScheduledAt ?? x.p.CreatedAt).ToListAsync(cancellationToken);
        var counts = await _db.PublicationTargets.AsNoTracking()
            .GroupBy(t => t.PublicationId).Select(g => new { g.Key, C = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.C, cancellationToken);

        return rows.Select(x => new PublicationDto(x.p.Id, x.p.ClientId, x.ClientName, x.p.Caption,
            x.p.ScheduledAt, x.p.Status, counts.TryGetValue(x.p.Id, out var n) ? n : 0,
            DeserializeUrls(x.p.MediaUrlsJson))).ToList();
    }

    private static IReadOnlyList<string> DeserializeUrls(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) { return Array.Empty<string>(); }
        try { return JsonSerializer.Deserialize<List<string>>(json) ?? new(); }
        catch { return Array.Empty<string>(); }
    }

    public async Task<PublicationDto?> CreateAsync(CreatePublicationRequest request, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId) { return null; }
        var client = await _db.Clients.FirstOrDefaultAsync(c => c.Id == request.ClientId, cancellationToken);
        if (client is null) { return null; }

        // Conservar solo URLs relativas que comienzan con "/uploads/" para evitar inyectar URLs externas
        // arbitrarias por parte del cliente (defense in depth — el upload server-side ya valida).
        var safeUrls = request.MediaUrls
            .Where(u => !string.IsNullOrWhiteSpace(u) && u.StartsWith("/uploads/", StringComparison.OrdinalIgnoreCase))
            .Distinct()
            .Take(10)
            .ToList();

        var pub = new Publication
        {
            TenantId = tenantId,
            ClientId = request.ClientId,
            Caption = request.Caption.Trim(),
            MediaUrlsJson = safeUrls.Count == 0 ? null : JsonSerializer.Serialize(safeUrls),
            ScheduledAt = request.ScheduledAt,
            Status = request.ScheduledAt is null ? PublicationStatus.Draft : PublicationStatus.Scheduled,
            AuthorTenantUserId = actorUserId
        };
        _db.Publications.Add(pub);

        foreach (var accId in request.SocialAccountIds.Distinct())
        {
            _db.PublicationTargets.Add(new PublicationTarget
            {
                TenantId = tenantId,
                PublicationId = pub.Id,
                SocialAccountId = accId,
                Status = PublicationTargetStatus.Pending
            });
        }
        _audit.Write(actorUserId, "publication.create", nameof(Publication), pub.Id,
            previousValue: null, newValue: new { pub.ClientId, pub.Status, MediaCount = safeUrls.Count }, tenantId: tenantId);
        await _db.SaveChangesAsync(cancellationToken);
        return new PublicationDto(pub.Id, pub.ClientId, client.Name, pub.Caption, pub.ScheduledAt, pub.Status,
            request.SocialAccountIds.Distinct().Count(), safeUrls);
    }

    public async Task<PublicationDto?> SetStatusAsync(Guid id, PublicationStatus status, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var pub = await _db.Publications.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (pub is null) { return null; }
        pub.Status = status;
        if (status == PublicationStatus.Approved) { pub.ApprovedByTenantUserId = actorUserId; }
        if (status == PublicationStatus.Published) { pub.PublishedAt = DateTimeOffset.UtcNow; }
        _audit.Write(actorUserId, "publication.set-status", nameof(Publication), pub.Id,
            previousValue: null, newValue: new { status }, tenantId: pub.TenantId);
        await _db.SaveChangesAsync(cancellationToken);
        var name = await _db.Clients.AsNoTracking().Where(c => c.Id == pub.ClientId).Select(c => c.Name).FirstOrDefaultAsync(cancellationToken) ?? "";
        var count = await _db.PublicationTargets.CountAsync(t => t.PublicationId == id, cancellationToken);
        return new PublicationDto(pub.Id, pub.ClientId, name, pub.Caption, pub.ScheduledAt, pub.Status, count, DeserializeUrls(pub.MediaUrlsJson));
    }

    public async Task<bool> DeleteAsync(Guid id, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var pub = await _db.Publications.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (pub is null) { return false; }
        _db.Publications.Remove(pub);
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }
}
