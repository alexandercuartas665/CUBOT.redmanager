using CubotRedManager.Domain.Common;
using CubotRedManager.Domain.Enums;

namespace CubotRedManager.Domain.Entities;

/// <summary>
/// Suscripcion de una agencia a un plan. Tiene columna TenantId pero es ENTIDAD GLOBAL: la
/// administra el Super Admin sobre todas las agencias, por eso NO es ITenantScoped.
/// </summary>
public class TenantSubscription : BaseEntity
{
    public Guid TenantId { get; set; }
    public Tenant? Tenant { get; set; }

    public Guid PlanId { get; set; }
    public SaasPlan? Plan { get; set; }

    public SubscriptionStatus Status { get; set; } = SubscriptionStatus.Trialing;
    public BillingFrequency BillingFrequency { get; set; } = BillingFrequency.Monthly;
    public DateTimeOffset StartsAt { get; set; }
    public DateTimeOffset CurrentPeriodEndsAt { get; set; }
    public DateTimeOffset? GracePeriodEndsAt { get; set; }

    public bool AutoRenew { get; set; }
    public long? WompiPaymentSourceId { get; set; }
    public string? PaymentMethodLabel { get; set; }
    public int FailedAttempts { get; set; }
}
