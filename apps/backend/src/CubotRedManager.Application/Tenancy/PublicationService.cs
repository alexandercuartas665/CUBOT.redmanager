using CubotRedManager.Application.Abstractions;
using CubotRedManager.Domain.Entities;
using CubotRedManager.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace CubotRedManager.Application.Tenancy;

public sealed class PublicationService : IPublicationService
{
    private const int MaxMediaPerPublication = 10;
    private const long MaxTotalMediaBytes = 60L * 1024 * 1024 * 10; // guardia dura por publicacion

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

        // Cargamos metadata de Media (sin bytes) por publicacion en un solo query.
        var pubIds = rows.Select(r => r.p.Id).ToList();
        var mediaMeta = await _db.PublicationMedias.AsNoTracking()
            .Where(m => pubIds.Contains(m.PublicationId))
            .OrderBy(m => m.PublicationId).ThenBy(m => m.SortOrder)
            .Select(m => new { m.PublicationId, m.Id, m.FileName, m.MimeType, m.FileSize })
            .ToListAsync(cancellationToken);
        var mediaByPub = mediaMeta
            .GroupBy(m => m.PublicationId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<PublicationMediaDto>)g
                .Select(m => new PublicationMediaDto(m.Id, m.FileName, m.MimeType, m.FileSize)).ToList());

        return rows.Select(x => new PublicationDto(
            x.p.Id, x.p.ClientId, x.ClientName, x.p.Caption,
            x.p.ScheduledAt, x.p.Status,
            counts.TryGetValue(x.p.Id, out var n) ? n : 0,
            mediaByPub.TryGetValue(x.p.Id, out var m) ? m : Array.Empty<PublicationMediaDto>())).ToList();
    }

    public async Task<PublicationDto?> CreateAsync(CreatePublicationRequest request, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId) { return null; }
        var client = await _db.Clients.FirstOrDefaultAsync(c => c.Id == request.ClientId, cancellationToken);
        if (client is null) { return null; }

        // Guardias defensivas: recortamos a MaxMediaPerPublication y respetamos el tope de bytes.
        var mediaBlobs = (request.Media ?? Array.Empty<PublicationMediaBlob>())
            .Where(b => b.Content.Length > 0 && !string.IsNullOrWhiteSpace(b.FileName))
            .Take(MaxMediaPerPublication)
            .ToList();
        var totalBytes = mediaBlobs.Sum(b => (long)b.Content.Length);
        if (totalBytes > MaxTotalMediaBytes)
        {
            // El uploader ya valida 60MB por archivo; este es el failsafe agregado.
            throw new InvalidOperationException($"Media total excede {MaxTotalMediaBytes / 1024 / 1024}MB.");
        }

        var pub = new Publication
        {
            TenantId = tenantId,
            ClientId = request.ClientId,
            Caption = request.Caption.Trim(),
            ScheduledAt = request.ScheduledAt,
            Status = request.ScheduledAt is null ? PublicationStatus.Draft : PublicationStatus.Scheduled,
            AuthorTenantUserId = actorUserId
        };
        _db.Publications.Add(pub);

        var mediaDtos = new List<PublicationMediaDto>(mediaBlobs.Count);
        for (var i = 0; i < mediaBlobs.Count; i++)
        {
            var b = mediaBlobs[i];
            var media = new PublicationMedia
            {
                TenantId = tenantId,
                Publication = pub, // EF resuelve la FK con la Publication en el ChangeTracker
                FileName = b.FileName.Trim(),
                MimeType = string.IsNullOrWhiteSpace(b.MimeType) ? "application/octet-stream" : b.MimeType.Trim(),
                FileSize = b.Content.Length,
                Content = b.Content,
                SortOrder = i
            };
            _db.PublicationMedias.Add(media);
            mediaDtos.Add(new PublicationMediaDto(media.Id, media.FileName, media.MimeType, media.FileSize));
        }

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
            previousValue: null, newValue: new { pub.ClientId, pub.Status, MediaCount = mediaBlobs.Count, TotalBytes = totalBytes }, tenantId: tenantId);
        await _db.SaveChangesAsync(cancellationToken);
        return new PublicationDto(pub.Id, pub.ClientId, client.Name, pub.Caption, pub.ScheduledAt, pub.Status,
            request.SocialAccountIds.Distinct().Count(), mediaDtos);
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
        var media = await _db.PublicationMedias.AsNoTracking()
            .Where(m => m.PublicationId == id)
            .OrderBy(m => m.SortOrder)
            .Select(m => new PublicationMediaDto(m.Id, m.FileName, m.MimeType, m.FileSize))
            .ToListAsync(cancellationToken);
        return new PublicationDto(pub.Id, pub.ClientId, name, pub.Caption, pub.ScheduledAt, pub.Status, count, media);
    }

    public async Task<bool> DeleteAsync(Guid id, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var pub = await _db.Publications.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (pub is null) { return false; }
        // El cascade delete de EF borra PublicationTarget + PublicationMedia (bytes incluidos).
        _db.Publications.Remove(pub);
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }
}
