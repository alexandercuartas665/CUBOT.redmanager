using CubotRedManager.Application.Abstractions;
using CubotRedManager.Domain.Entities;
using CubotRedManager.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace CubotRedManager.Application.Admin;

public sealed class SubscriptionAdminService : ISubscriptionAdminService
{
    private readonly IApplicationDbContext _db;
    private readonly IAuditWriter _audit;
    private readonly IRecurringBillingService _recurring;

    public SubscriptionAdminService(IApplicationDbContext db, IAuditWriter audit, IRecurringBillingService recurring)
    {
        _db = db;
        _audit = audit;
        _recurring = recurring;
    }

    public async Task<SubscriptionDetail?> AssignAsync(AssignSubscriptionRequest request, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var tenantExists = await _db.Tenants.AnyAsync(t => t.Id == request.TenantId, cancellationToken);
        var planExists = await _db.SaasPlans.AnyAsync(p => p.Id == request.PlanId, cancellationToken);
        if (!tenantExists || !planExists)
        {
            return null;
        }

        var startsAt = request.StartsAt ?? DateTimeOffset.UtcNow;
        var periodEnd = request.BillingFrequency == BillingFrequency.Yearly
            ? startsAt.AddYears(1)
            : startsAt.AddMonths(1);

        var subscription = new TenantSubscription
        {
            TenantId = request.TenantId,
            PlanId = request.PlanId,
            Status = SubscriptionStatus.Active,
            BillingFrequency = request.BillingFrequency,
            StartsAt = startsAt,
            CurrentPeriodEndsAt = periodEnd
        };

        _db.TenantSubscriptions.Add(subscription);
        _audit.Write(actorUserId, "subscription.assign", nameof(TenantSubscription), subscription.Id,
            previousValue: null,
            newValue: new { subscription.PlanId, subscription.BillingFrequency, subscription.CurrentPeriodEndsAt },
            tenantId: request.TenantId);

        await _db.SaveChangesAsync(cancellationToken);
        return Map(subscription);
    }

    public async Task<ChangePlanResult?> ChangePlanAsync(Guid tenantId, Guid planId, BillingFrequency frequency, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var tenantExists = await _db.Tenants.AnyAsync(t => t.Id == tenantId, cancellationToken);
        var newPlan = await _db.SaasPlans.AsNoTracking().FirstOrDefaultAsync(p => p.Id == planId, cancellationToken);
        if (!tenantExists || newPlan is null)
        {
            return null;
        }

        var current = await _db.TenantSubscriptions
            .Where(s => s.TenantId == tenantId)
            .OrderByDescending(s => s.StartsAt)
            .FirstOrDefaultAsync(cancellationToken);

        var newAmount = AmountOf(newPlan, frequency);
        var currentAmount = 0m;
        if (current is not null)
        {
            var currentPlan = await _db.SaasPlans.AsNoTracking().FirstOrDefaultAsync(p => p.Id == current.PlanId, cancellationToken);
            if (currentPlan is not null)
            {
                currentAmount = AmountOf(currentPlan, current.BillingFrequency);
            }
        }

        var isUpgrade = newAmount > currentAmount;
        var now = DateTimeOffset.UtcNow;
        var resetEnd = frequency == BillingFrequency.Yearly ? now.AddYears(1) : now.AddMonths(1);

        // Upgrade: la fecha de corte se reinicia (se cobra el plan nuevo completo de inmediato; queda
        //          "vencida" en 'now' para que el cobro la renueve a now+periodo).
        // Downgrade: se conserva la fecha de corte vigente (el plan menor entra en la proxima renovacion).
        var periodEnd = isUpgrade ? now : (current?.CurrentPeriodEndsAt ?? resetEnd);

        var subscription = new TenantSubscription
        {
            TenantId = tenantId,
            PlanId = planId,
            Status = SubscriptionStatus.Active,
            BillingFrequency = frequency,
            StartsAt = now,
            CurrentPeriodEndsAt = periodEnd,
            // Hereda el metodo de pago para que el debito automatico sobreviva al cambio de plan.
            WompiPaymentSourceId = current?.WompiPaymentSourceId,
            PaymentMethodLabel = current?.PaymentMethodLabel,
            AutoRenew = current?.AutoRenew ?? false
        };
        _db.TenantSubscriptions.Add(subscription);

        _audit.Write(actorUserId, "subscription.change", nameof(TenantSubscription), subscription.Id,
            previousValue: current is null ? null : new { current.PlanId, current.BillingFrequency, current.CurrentPeriodEndsAt },
            newValue: new { subscription.PlanId, subscription.BillingFrequency, subscription.CurrentPeriodEndsAt, isUpgrade },
            tenantId: tenantId);

        await _db.SaveChangesAsync(cancellationToken);

        if (!isUpgrade)
        {
            return new ChangePlanResult(Map(subscription), IsUpgrade: false, ChargedNow: false, RequiresPayment: false,
                "Plan actualizado. Aplica de inmediato y el nuevo valor se cobrara en la proxima fecha de corte.");
        }

        // Upgrade: se cobra el plan nuevo completo de inmediato.
        if (subscription.AutoRenew && subscription.WompiPaymentSourceId is not null)
        {
            var charge = await _recurring.ChargeNowAsync(tenantId, actorUserId, cancellationToken);
            if (charge.Ok)
            {
                return new ChangePlanResult(Map(subscription), IsUpgrade: true, ChargedNow: true, RequiresPayment: false,
                    "Plan actualizado y cobrado con tu metodo de pago.");
            }
            return new ChangePlanResult(Map(subscription), IsUpgrade: true, ChargedNow: false, RequiresPayment: true,
                $"Plan actualizado, pero el cobro automatico no se completo ({charge.Error}). Usa 'Pagar ahora' para finalizar.");
        }

        return new ChangePlanResult(Map(subscription), IsUpgrade: true, ChargedNow: false, RequiresPayment: true,
            "Plan actualizado. Usa 'Pagar ahora' para completar el pago del plan nuevo.");
    }

    private static decimal AmountOf(SaasPlan plan, BillingFrequency frequency) =>
        (frequency == BillingFrequency.Yearly ? plan.YearlyPrice : plan.MonthlyPrice) ?? 0m;

    public async Task<IReadOnlyList<SubscriptionDetail>> ListByTenantAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        return await _db.TenantSubscriptions
            .AsNoTracking()
            .Where(s => s.TenantId == tenantId)
            .OrderByDescending(s => s.StartsAt)
            .Select(s => new SubscriptionDetail(s.Id, s.TenantId, s.PlanId, s.Status, s.BillingFrequency, s.StartsAt, s.CurrentPeriodEndsAt, s.GracePeriodEndsAt, s.AutoRenew, s.PaymentMethodLabel))
            .ToListAsync(cancellationToken);
    }

    private static SubscriptionDetail Map(TenantSubscription s) =>
        new(s.Id, s.TenantId, s.PlanId, s.Status, s.BillingFrequency, s.StartsAt, s.CurrentPeriodEndsAt, s.GracePeriodEndsAt, s.AutoRenew, s.PaymentMethodLabel);
}
