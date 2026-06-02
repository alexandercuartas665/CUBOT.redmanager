namespace CubotRedManager.Application.Admin;

public interface IWompiCheckoutService
{
    /// <summary>
    /// Genera un cobro de la suscripcion vigente del tenant: crea un pago Pendiente con una
    /// referencia unica y devuelve la URL de Web Checkout de Wompi firmada con el secret de
    /// integridad. Al pagar, el webhook confirma el pago por esa referencia.
    /// </summary>
    Task<WompiCheckoutResult> CreateSubscriptionCheckoutAsync(Guid tenantId, string returnUrl, Guid actorUserId, CancellationToken cancellationToken = default);
}
