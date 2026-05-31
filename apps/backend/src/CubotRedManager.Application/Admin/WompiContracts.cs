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
