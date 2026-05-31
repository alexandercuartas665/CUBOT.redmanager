using CubotRedManager.Application.Abstractions;
using CubotRedManager.Domain.Entities;
using CubotRedManager.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace CubotRedManager.Application.Admin;

public sealed class TenantAdminService : ITenantAdminService
{
    private readonly IApplicationDbContext _db;
    private readonly IAuditWriter _audit;

    public TenantAdminService(IApplicationDbContext db, IAuditWriter audit)
    {
        _db = db;
        _audit = audit;
    }

    public async Task<TenantDetail> CreateAsync(CreateTenantRequest request, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var tenant = new Tenant
        {
            Name = request.Name.Trim(),
            LegalName = request.LegalName?.Trim(),
            TaxId = request.TaxId?.Trim(),
            Country = request.Country?.Trim(),
            Currency = request.Currency?.Trim(),
            Status = TenantStatus.Trial,
            Kind = request.Kind
        };

        _db.Tenants.Add(tenant);
        _audit.Write(actorUserId, "tenant.create", nameof(Tenant), tenant.Id,
            previousValue: null,
            newValue: new { tenant.Name, tenant.Status, tenant.Kind },
            tenantId: tenant.Id);

        await _db.SaveChangesAsync(cancellationToken);
        return Map(tenant);
    }

    public async Task<IReadOnlyList<TenantListItem>> ListAsync(TenantStatus? status = null, string? search = null, CancellationToken cancellationToken = default)
    {
        var query = _db.Tenants.AsNoTracking();

        if (status is TenantStatus s)
        {
            query = query.Where(t => t.Status == s);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(t => t.Name.ToLower().Contains(term));
        }

        return await query
            .OrderByDescending(t => t.CreatedAt)
            .Select(t => new TenantListItem(t.Id, t.Name, t.Status, t.Kind, t.Country, t.Currency, t.CreatedAt))
            .ToListAsync(cancellationToken);
    }

    public async Task<TenantDetail?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var tenant = await _db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
        return tenant is null ? null : Map(tenant);
    }

    public async Task<TenantDetail?> ChangeStatusAsync(Guid id, ChangeTenantStatusRequest request, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var tenant = await _db.Tenants.FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
        if (tenant is null)
        {
            return null;
        }

        var previousStatus = tenant.Status;
        if (previousStatus == request.Status)
        {
            return Map(tenant);
        }

        tenant.Status = request.Status;
        _audit.Write(actorUserId, "tenant.change-status", nameof(Tenant), tenant.Id,
            previousValue: new { Status = previousStatus },
            newValue: new { Status = request.Status },
            tenantId: tenant.Id,
            reason: request.Reason);

        await _db.SaveChangesAsync(cancellationToken);
        return Map(tenant);
    }

    public async Task<TenantDetail?> UpdateProfileAsync(Guid id, UpdateTenantProfileRequest request, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var tenant = await _db.Tenants.FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
        if (tenant is null)
        {
            return null;
        }

        tenant.Name = request.Name.Trim();
        tenant.LegalName = request.LegalName?.Trim();
        tenant.TaxId = request.TaxId?.Trim();
        tenant.Country = request.Country?.Trim();
        tenant.Currency = request.Currency?.Trim();
        tenant.LogoUrl = string.IsNullOrWhiteSpace(request.LogoUrl) ? tenant.LogoUrl : request.LogoUrl.Trim();

        _audit.Write(actorUserId, "tenant.profile.update", nameof(Tenant), tenant.Id,
            previousValue: null,
            newValue: new { tenant.Name, tenant.LegalName, tenant.TaxId, tenant.Country, tenant.Currency, HasLogo = tenant.LogoUrl is not null },
            tenantId: tenant.Id);

        await _db.SaveChangesAsync(cancellationToken);
        return Map(tenant);
    }

    private static TenantDetail Map(Tenant t) =>
        new(t.Id, t.Name, t.LegalName, t.TaxId, t.Country, t.Currency, t.Status, t.Kind, t.CreatedAt, t.LogoUrl);
}
