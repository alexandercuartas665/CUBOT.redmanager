using CubotRedManager.Domain.Common;
using CubotRedManager.Domain.Enums;

namespace CubotRedManager.Domain.Entities;

/// <summary>
/// Pago de suscripcion de la agencia hacia el dueno del SaaS. Entidad GLOBAL (administrada por
/// Super Admin / finanzas), no tenant-scoped, aunque referencia un tenant.
/// </summary>
public class TenantPayment : BaseEntity
{
    public Guid TenantId { get; set; }

    public Guid SubscriptionId { get; set; }
    public TenantSubscription? Subscription { get; set; }

    public string Provider { get; set; } = "wompi";
    public string? ProviderReference { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = null!;
    public PaymentStatus Status { get; set; } = PaymentStatus.Pending;
    public DateTimeOffset BillingPeriodStart { get; set; }
    public DateTimeOffset BillingPeriodEnd { get; set; }
    public DateTimeOffset? ConfirmedAt { get; set; }
}
