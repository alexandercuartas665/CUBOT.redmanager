using CubotRedManager.Application.Abstractions;
using CubotRedManager.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CubotRedManager.Application.Admin;

public sealed class PlanAdminService : IPlanAdminService
{
    private readonly IApplicationDbContext _db;
    private readonly IAuditWriter _audit;

    public PlanAdminService(IApplicationDbContext db, IAuditWriter audit)
    {
        _db = db;
        _audit = audit;
    }

    public async Task<PlanDetail> CreateAsync(CreatePlanRequest request, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var plan = new SaasPlan
        {
            Name = request.Name.Trim(),
            Description = request.Description?.Trim(),
            MonthlyPrice = request.MonthlyPrice,
            YearlyPrice = request.YearlyPrice,
            Currency = request.Currency?.Trim(),
            IsActive = true,
            Limits = request.Limits
                .Select(l => new SaasPlanLimit
                {
                    LimitKey = l.LimitKey.Trim(),
                    LimitValue = l.LimitValue,
                    LimitUnit = l.LimitUnit?.Trim(),
                    EnforcementMode = l.EnforcementMode
                })
                .ToList()
        };

        _db.SaasPlans.Add(plan);
        _audit.Write(actorUserId, "plan.create", nameof(SaasPlan), plan.Id,
            previousValue: null,
            newValue: new { plan.Name, plan.MonthlyPrice, plan.YearlyPrice, LimitCount = plan.Limits.Count });

        await _db.SaveChangesAsync(cancellationToken);
        return Map(plan);
    }

    public async Task<PlanDetail?> UpdateAsync(Guid id, CreatePlanRequest request, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var plan = await _db.SaasPlans.Include(p => p.Limits).FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (plan is null)
        {
            return null;
        }

        plan.Name = request.Name.Trim();
        plan.Description = request.Description?.Trim();
        plan.MonthlyPrice = request.MonthlyPrice;
        plan.YearlyPrice = request.YearlyPrice;
        plan.Currency = request.Currency?.Trim();

        _db.SaasPlanLimits.RemoveRange(plan.Limits);
        var newLimits = request.Limits
            .Select(l => new SaasPlanLimit
            {
                PlanId = plan.Id,
                LimitKey = l.LimitKey.Trim(),
                LimitValue = l.LimitValue,
                LimitUnit = l.LimitUnit?.Trim(),
                EnforcementMode = l.EnforcementMode
            })
            .ToList();
        _db.SaasPlanLimits.AddRange(newLimits);

        _audit.Write(actorUserId, "plan.update", nameof(SaasPlan), plan.Id,
            previousValue: null,
            newValue: new { plan.Name, plan.MonthlyPrice, plan.YearlyPrice, LimitCount = newLimits.Count });

        await _db.SaveChangesAsync(cancellationToken);
        plan.Limits = newLimits;
        return Map(plan);
    }

    public async Task<IReadOnlyList<PlanDetail>> ListAsync(CancellationToken cancellationToken = default)
    {
        var plans = await _db.SaasPlans
            .AsNoTracking()
            .Include(p => p.Limits)
            .OrderByDescending(p => p.CreatedAt)
            .ToListAsync(cancellationToken);

        return plans.Select(Map).ToList();
    }

    public async Task<PlanDetail?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var plan = await _db.SaasPlans
            .AsNoTracking()
            .Include(p => p.Limits)
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

        return plan is null ? null : Map(plan);
    }

    public async Task<PlanDetail?> SetActiveAsync(Guid id, bool isActive, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var plan = await _db.SaasPlans.Include(p => p.Limits).FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (plan is null)
        {
            return null;
        }

        var previous = plan.IsActive;
        if (previous != isActive)
        {
            plan.IsActive = isActive;
            _audit.Write(actorUserId, "plan.set-active", nameof(SaasPlan), plan.Id,
                previousValue: new { IsActive = previous },
                newValue: new { IsActive = isActive });
            await _db.SaveChangesAsync(cancellationToken);
        }

        return Map(plan);
    }

    private static PlanDetail Map(SaasPlan p) =>
        new(p.Id, p.Name, p.Description, p.MonthlyPrice, p.YearlyPrice, p.Currency, p.IsActive,
            p.Limits.Select(l => new PlanLimitDto(l.LimitKey, l.LimitValue, l.LimitUnit, l.EnforcementMode)).ToList());
}
