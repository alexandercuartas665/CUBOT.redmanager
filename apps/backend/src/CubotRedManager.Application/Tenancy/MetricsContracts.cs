namespace CubotRedManager.Application.Tenancy;

public sealed record AccountActivityDto(string Network, string? Handle, string ClientName, int Videos, int Comments, int RepliedComments, int PendingComments);
public sealed record PublicationStatusDto(string Status, int Count);
public sealed record TopVideoDto(string ExternalId, string? Caption, int CommentCount, int LikeCount, int ViewCount);
public sealed record MonthlyCountDto(int Year, int Month, int Count);

public sealed record TenantMetricsDto(
    int TotalAccounts,
    int ActiveAccounts,
    int TotalPublications,
    int PublishedPublications,
    int TotalComments,
    int RepliedComments,
    int PendingComments,
    IReadOnlyList<AccountActivityDto> AccountActivity,
    IReadOnlyList<PublicationStatusDto> PublicationsByStatus,
    IReadOnlyList<TopVideoDto> TopVideos,
    IReadOnlyList<MonthlyCountDto> CommentsLast6Months);

public interface ITenantMetricsService
{
    Task<TenantMetricsDto> GetMetricsAsync(CancellationToken ct = default);
}
