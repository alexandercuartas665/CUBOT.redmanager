using CubotRedManager.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace CubotRedManager.Application.Tenancy;

public sealed class TenantUserService : ITenantUserService
{
    private readonly IApplicationDbContext _db;

    public TenantUserService(IApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<TenantUserDto>> ListAsync(CancellationToken cancellationToken = default)
    {
        // El filtro global del DbContext limita por el tenant del contexto.
        // En redmanager el email vive en PlatformUser (no en TenantUser), por eso hacemos join.
        return await _db.TenantUsers
            .AsNoTracking()
            .Include(u => u.PlatformUser)
            .OrderBy(u => u.PlatformUser!.Email)
            .Select(u => new TenantUserDto(
                u.Id,
                u.PlatformUserId,
                u.PlatformUser!.Email,
                u.TenantRole,
                u.Status))
            .ToListAsync(cancellationToken);
    }
}
