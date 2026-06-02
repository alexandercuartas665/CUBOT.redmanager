using CubotRedManager.Application.Abstractions;
using CubotRedManager.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CubotRedManager.Application.Tenancy;

public sealed class AutomationService : IAutomationService
{
    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly IAuditWriter _audit;

    public AutomationService(IApplicationDbContext db, ITenantContext tenantContext, IAuditWriter audit)
    { _db = db; _tenantContext = tenantContext; _audit = audit; }

    public async Task<IReadOnlyList<AutomationRuleDto>> ListAsync(CancellationToken ct = default)
    {
        var rows = await _db.AutomationRules.AsNoTracking().OrderByDescending(r => r.CreatedAt).ToListAsync(ct);
        return rows.Select(r => new AutomationRuleDto(r.Id, r.Name, r.Trigger, r.Action, r.IsActive, r.NoReplyMinutes, r.ExecutionCount, r.LastExecutedAt)).ToList();
    }

    public async Task<AutomationRuleDto?> SaveAsync(SaveAutomationRuleRequest req, Guid actorUserId, CancellationToken ct = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId) return null;
        AutomationRule? rule = null;
        if (req.Id is { } id) rule = await _db.AutomationRules.FirstOrDefaultAsync(r => r.Id == id, ct);
        var isNew = rule is null;
        rule ??= new AutomationRule { TenantId = tenantId };
        rule.Name = req.Name.Trim();
        rule.Trigger = req.Trigger;
        rule.Action = req.Action;
        rule.IsActive = req.IsActive;
        rule.NoReplyMinutes = req.NoReplyMinutes;
        if (isNew) _db.AutomationRules.Add(rule);
        _audit.Write(actorUserId, isNew ? "automation.create" : "automation.update", nameof(AutomationRule), rule.Id,
            previousValue: null, newValue: new { rule.Name, Trigger = rule.Trigger.ToString(), Action = rule.Action.ToString(), rule.IsActive }, tenantId: tenantId);
        await _db.SaveChangesAsync(ct);
        return new AutomationRuleDto(rule.Id, rule.Name, rule.Trigger, rule.Action, rule.IsActive, rule.NoReplyMinutes, rule.ExecutionCount, rule.LastExecutedAt);
    }

    public async Task<bool> DeleteAsync(Guid id, Guid actorUserId, CancellationToken ct = default)
    {
        var rule = await _db.AutomationRules.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (rule is null) return false;
        _db.AutomationRules.Remove(rule);
        _audit.Write(actorUserId, "automation.delete", nameof(AutomationRule), id, previousValue: new { rule.Name }, newValue: null, tenantId: rule.TenantId);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> ToggleActiveAsync(Guid id, bool isActive, Guid actorUserId, CancellationToken ct = default)
    {
        var rule = await _db.AutomationRules.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (rule is null) return false;
        rule.IsActive = isActive;
        _audit.Write(actorUserId, "automation.toggle", nameof(AutomationRule), id, previousValue: null, newValue: new { isActive }, tenantId: rule.TenantId);
        await _db.SaveChangesAsync(ct);
        return true;
    }
}
