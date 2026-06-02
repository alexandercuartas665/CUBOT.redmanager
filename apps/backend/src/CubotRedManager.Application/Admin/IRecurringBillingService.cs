namespace CubotRedManager.Application.Admin;

public sealed record RecurringResult(bool Ok, string? Label, string? Error);

public interface IRecurringBillingService
{
    /// <summary>
    /// Activa el debito automatico: crea una fuente de pago en Wompi a partir del token de tarjeta
    /// (tokenizada en el navegador) y la guarda en la suscripcion vigente del tenant.
    /// </summary>
    Task<RecurringResult> EnableAutoRenewAsync(Guid tenantId, string cardToken, string customerEmail, Guid actorUserId, CancellationToken cancellationToken = default);

    Task DisableAutoRenewAsync(Guid tenantId, Guid actorUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cobra las suscripciones con debito automatico cuyo periodo ya vencio. Lo invoca el worker.
    /// Devuelve cuantas suscripciones se procesaron.
    /// </summary>
    Task<int> ChargeDueSubscriptionsAsync(CancellationToken cancellationToken = default);

    /// <summary>Fuerza el cobro de una suscripcion concreta (uso manual / pruebas), ignorando la fecha de corte.</summary>
    Task<RecurringResult> ChargeNowAsync(Guid tenantId, Guid actorUserId, CancellationToken cancellationToken = default);
}
