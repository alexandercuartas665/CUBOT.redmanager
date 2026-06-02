using CubotRedManager.Domain.Enums;

namespace CubotRedManager.Application.Admin;

/// <summary>Vista de la configuracion Wompi para la consola. Las llaves sensibles van enmascaradas.</summary>
public sealed record WompiConfigDto(
    WompiEnvironment Environment,
    string? PublicKey,
    string? PrivateKeyMasked,
    string? EventsSecretMasked,
    string? IntegritySecretMasked,
    string? WebhookEndpoint,
    string Currency,
    int MaxRetries,
    WompiIntegrationStatus Status,
    DateTimeOffset? LastValidatedAt,
    bool HasPrivateKey,
    bool HasEventsSecret,
    bool HasIntegritySecret);

public sealed record SaveWompiConfigRequest(
    WompiEnvironment Environment,
    string? PublicKey,
    string? PrivateKey,
    string? EventsSecret,
    string? IntegritySecret,
    string? WebhookEndpoint,
    string Currency,
    int MaxRetries);

public sealed record WompiValidationResult(bool Ok, string Message);

/// <summary>Resultado de generar un checkout: la URL de pago de Wompi y la referencia creada.</summary>
public sealed record WompiCheckoutResult(bool Ok, string? CheckoutUrl, string? Reference, string? Error);

// --- Wompi API client (debito automatico) ---
public sealed record WompiAcceptance(bool Ok, string? AcceptanceToken, string? Error);
public sealed record WompiPaymentSourceResult(bool Ok, long? Id, string? Label, string? Error);
public sealed record WompiChargeResult(bool Ok, string? TransactionId, string? Status, string? Error);

/// <summary>
/// Cliente HTTP de la API de Wompi (sandbox/produccion segun la config maestra). Lo usa el debito
/// automatico para tokenizar tarjetas y cobrar contra fuentes de pago guardadas.
/// </summary>
public interface IWompiApiClient
{
    Task<WompiAcceptance> GetAcceptanceTokenAsync(CancellationToken cancellationToken = default);
    Task<WompiPaymentSourceResult> CreateCardPaymentSourceAsync(string cardToken, string customerEmail, string acceptanceToken, CancellationToken cancellationToken = default);
    Task<WompiChargeResult> ChargePaymentSourceAsync(long paymentSourceId, long amountInCents, string currency, string reference, string customerEmail, CancellationToken cancellationToken = default);
}

public interface IWompiConfigService
{
    Task<WompiConfigDto?> GetAsync(CancellationToken cancellationToken = default);
    Task<WompiConfigDto> SaveAsync(SaveWompiConfigRequest request, Guid actorUserId, CancellationToken cancellationToken = default);
    Task<WompiValidationResult?> ValidateAsync(Guid actorUserId, CancellationToken cancellationToken = default);
}

// --- Pagos ---
public sealed record RegisterPaymentRequest(
    Guid TenantId,
    Guid SubscriptionId,
    decimal Amount,
    string Currency,
    PaymentStatus Status,
    DateTimeOffset BillingPeriodStart,
    DateTimeOffset BillingPeriodEnd,
    string? ProviderReference = null);

public sealed record PaymentDetail(
    Guid Id,
    Guid TenantId,
    Guid SubscriptionId,
    string Provider,
    string? ProviderReference,
    decimal Amount,
    string Currency,
    PaymentStatus Status,
    DateTimeOffset BillingPeriodStart,
    DateTimeOffset BillingPeriodEnd,
    DateTimeOffset? ConfirmedAt,
    DateTimeOffset CreatedAt);

public interface IPaymentAdminService
{
    Task<PaymentDetail?> RegisterAsync(RegisterPaymentRequest request, Guid actorUserId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PaymentDetail>> ListAsync(Guid? tenantId = null, PaymentStatus? status = null, CancellationToken cancellationToken = default);
}
