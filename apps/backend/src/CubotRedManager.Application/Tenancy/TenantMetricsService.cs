using CubotRedManager.Application.Abstractions;
using CubotRedManager.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace CubotRedManager.Application.Tenancy;

/// <summary>
/// Calcula metricas agregadas del tenant (cuentas, publicaciones, comentarios). Tenant-scoped via
/// HasQueryFilter automatico del DbContext. Sin parametros: siempre devuelve el snapshot del tenant
/// activo.
/// </summary>
public sealed class TenantMetricsService : ITenantMetricsService
{
    private readonly IApplicationDbContext _db;

    public TenantMetricsService(IApplicationDbContext db) { _db = db; }

    public async Task<TenantMetricsDto> GetMetricsAsync(CancellationToken ct = default)
    {
        var accounts = await _db.SocialAccounts.AsNoTracking().ToListAsync(ct);
        var totalAccounts = accounts.Count;
        var activeAccounts = accounts.Count(a => a.Status == SocialAccountStatus.Connected);

        var publications = await _db.Publications.AsNoTracking().ToListAsync(ct);
        var totalPublications = publications.Count;
        var publishedPublications = publications.Count(p => p.Status == PublicationStatus.Published);

        var publicationsByStatus = Enum.GetValues<PublicationStatus>()
            .Select(s => new PublicationStatusDto(s.ToString(), publications.Count(p => p.Status == s)))
            .ToList();

        var inbox = await _db.InboxMessages.AsNoTracking().ToListAsync(ct);
        var comments = inbox.Where(m => m.Type == InboxMessageType.Comment).ToList();
        var totalComments = comments.Count;
        var repliedComments = comments.Count(c => c.Status == InboxStatus.Replied);
        var pendingComments = comments.Count(c => c.Status == InboxStatus.Unread || c.Status == InboxStatus.Read);

        var clients = await _db.Clients.AsNoTracking().ToDictionaryAsync(c => c.Id, c => c.Name, ct);
        var videos = await _db.TikTokVideos.AsNoTracking().ToListAsync(ct);

        var accountActivity = new List<AccountActivityDto>(accounts.Count);
        foreach (var a in accounts)
        {
            var clientName = clients.TryGetValue(a.ClientId, out var cn) ? cn : "(cliente)";
            var videosOfAccount = videos.Count(v => v.SocialAccountId == a.Id);
            var commentsOfAccount = comments.Where(c => c.SocialAccountId == a.Id).ToList();
            var replied = commentsOfAccount.Count(c => c.Status == InboxStatus.Replied);
            var pending = commentsOfAccount.Count(c => c.Status == InboxStatus.Unread || c.Status == InboxStatus.Read);
            accountActivity.Add(new AccountActivityDto(
                Network: a.NetworkCode,
                Handle: a.Handle,
                ClientName: clientName,
                Videos: videosOfAccount,
                Comments: commentsOfAccount.Count,
                RepliedComments: replied,
                PendingComments: pending));
        }

        var topVideos = videos
            .OrderByDescending(v => v.ViewCount)
            .Take(10)
            .Select(v => new TopVideoDto(v.ExternalId, v.Caption, v.CommentCount, v.LikeCount, v.ViewCount))
            .ToList();

        // Ultimos 6 meses (incluye el actual). UTC.
        var now = DateTimeOffset.UtcNow;
        var months = new List<MonthlyCountDto>(6);
        for (var i = 5; i >= 0; i--)
        {
            var monthAnchor = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero).AddMonths(-i);
            var year = monthAnchor.Year;
            var month = monthAnchor.Month;
            var count = comments.Count(c =>
            {
                var d = c.ReceivedAt.UtcDateTime;
                return d.Year == year && d.Month == month;
            });
            months.Add(new MonthlyCountDto(year, month, count));
        }

        return new TenantMetricsDto(
            TotalAccounts: totalAccounts,
            ActiveAccounts: activeAccounts,
            TotalPublications: totalPublications,
            PublishedPublications: publishedPublications,
            TotalComments: totalComments,
            RepliedComments: repliedComments,
            PendingComments: pendingComments,
            AccountActivity: accountActivity,
            PublicationsByStatus: publicationsByStatus,
            TopVideos: topVideos,
            CommentsLast6Months: months);
    }
}
