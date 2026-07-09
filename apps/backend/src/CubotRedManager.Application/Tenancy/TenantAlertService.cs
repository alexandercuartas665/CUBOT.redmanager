using CubotRedManager.Application.Abstractions;
using CubotRedManager.Domain.Entities;
using CubotRedManager.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace CubotRedManager.Application.Tenancy;

public sealed class TenantAlertService : ITenantAlertService
{
    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly IAuditWriter _audit;

    public TenantAlertService(IApplicationDbContext db, ITenantContext tenantContext, IAuditWriter audit)
    {
        _db = db;
        _tenantContext = tenantContext;
        _audit = audit;
    }

    public async Task<TenantAlertConfigDto> GetOrDefaultAsync(CancellationToken cancellationToken = default)
    {
        var cfg = await _db.TenantAlertConfigs.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
        return cfg is null
            ? new TenantAlertConfigDto(false, null, AutoReplySummaryTargetType.Phone, null)
            : new TenantAlertConfigDto(cfg.IsActive, cfg.WhatsAppLineId, cfg.TargetType, cfg.Target);
    }

    public async Task<TenantAlertConfigDto?> SaveAsync(SaveTenantAlertConfigRequest request, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId) { return null; }

        var cfg = await _db.TenantAlertConfigs.FirstOrDefaultAsync(cancellationToken);
        if (cfg is null)
        {
            cfg = new TenantAlertConfig { TenantId = tenantId };
            _db.TenantAlertConfigs.Add(cfg);
        }

        cfg.IsActive = request.IsActive;
        cfg.WhatsAppLineId = request.WhatsAppLineId;
        cfg.TargetType = request.TargetType;
        cfg.Target = string.IsNullOrWhiteSpace(request.Target) ? null : request.Target.Trim();

        _audit.Write(actorUserId, "tenant.alert.save", nameof(TenantAlertConfig), cfg.Id,
            previousValue: null,
            newValue: new { cfg.IsActive, cfg.WhatsAppLineId, cfg.TargetType, HasTarget = !string.IsNullOrEmpty(cfg.Target) },
            tenantId: tenantId);
        await _db.SaveChangesAsync(cancellationToken);

        return new TenantAlertConfigDto(cfg.IsActive, cfg.WhatsAppLineId, cfg.TargetType, cfg.Target);
    }
}
