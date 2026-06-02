using CubotRedManager.Domain.Enums;

namespace CubotRedManager.Application.Admin;

public sealed record AssignSubscriptionRequest(
    Guid TenantId,
    Guid PlanId,
    BillingFrequency BillingFrequency,
    DateTimeOffset? StartsAt = null);

public sealed record SubscriptionDetail(
    Guid Id,
    Guid TenantId,
    Guid PlanId,
    SubscriptionStatus Status,
    BillingFrequency BillingFrequency,
    DateTimeOffset StartsAt,
    DateTimeOffset CurrentPeriodEndsAt,
    DateTimeOffset? GracePeriodEndsAt,
    bool AutoRenew = false,
    string? PaymentMethodLabel = null);

/// <summary>Resultado de un cambio de plan en autoservicio.</summary>
public sealed record ChangePlanResult(
    SubscriptionDetail Subscription,
    bool IsUpgrade,
    bool ChargedNow,
    bool RequiresPayment,
    string? Message);

public interface ISubscriptionAdminService
{
    /// <summary>Devuelve null si el tenant o el plan no existen.</summary>
    Task<SubscriptionDetail?> AssignAsync(AssignSubscriptionRequest request, Guid actorUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cambio de plan en autoservicio (lo solicita el cliente). Aplica de inmediato.
    /// Modelo de cobro (sin prorrateo): si es un plan MAYOR (upgrade) se cobra el plan nuevo
    /// completo de inmediato y se reinicia la fecha de corte a hoy; si es un plan MENOR o igual
    /// (downgrade) no se cobra nada ahora y se conserva la fecha de corte actual.
    /// </summary>
    Task<ChangePlanResult?> ChangePlanAsync(Guid tenantId, Guid planId, BillingFrequency frequency, Guid actorUserId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SubscriptionDetail>> ListByTenantAsync(Guid tenantId, CancellationToken cancellationToken = default);
}
