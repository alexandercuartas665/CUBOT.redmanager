using CubotRedManager.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace CubotRedManager.Application.Tenancy;

public sealed class TokenRefreshLogService : ITokenRefreshLogService
{
    private readonly IApplicationDbContext _db;

    public TokenRefreshLogService(IApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<TokenRefreshLogDto>> ListForAccountAsync(
        Guid socialAccountId, int take = 30, CancellationToken cancellationToken = default)
    {
        // Tenant-scoped implicito via HasQueryFilter en TokenRefreshLog (hereda de TenantEntity).
        var rows = await _db.TokenRefreshLogs.AsNoTracking()
            .Where(x => x.SocialAccountId == socialAccountId)
            .OrderByDescending(x => x.AttemptedAt)
            .Take(take)
            .Select(x => new TokenRefreshLogDto(x.Id, x.AttemptedAt, x.Operation, x.Endpoint, x.Flavor,
                x.Success, x.HttpStatus, x.ResponseCode, x.ErrorMessage, x.DurationMs, x.FailureCountAfter))
            .ToListAsync(cancellationToken);
        return rows;
    }
}
