using System.Security.Cryptography;
using System.Text;
using CubotRedManager.Application.Abstractions;
using CubotRedManager.Domain.Entities;
using CubotRedManager.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace CubotRedManager.Application.Admin;

/// <summary>
/// Genera el Web Checkout de Wompi para cobrar la suscripcion de un tenant.
/// La firma de integridad es SHA256(reference + amount_in_cents + currency + integrity_secret).
/// El pago se crea Pendiente; el webhook lo confirma luego por su referencia.
/// </summary>
public sealed class WompiCheckoutService : IWompiCheckoutService
{
    private const string CheckoutBaseUrl = "https://checkout.wompi.co/p/";

    private readonly IApplicationDbContext _db;
    private readonly ISecretProtector _secretProtector;
    private readonly IAuditWriter _audit;

    public WompiCheckoutService(IApplicationDbContext db, ISecretProtector secretProtector, IAuditWriter audit)
    {
        _db = db;
        _secretProtector = secretProtector;
        _audit = audit;
    }

    public async Task<WompiCheckoutResult> CreateSubscriptionCheckoutAsync(Guid tenantId, string returnUrl, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var config = await _db.WompiMasterConfigs.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
        if (config is null || string.IsNullOrWhiteSpace(config.PublicKey) || string.IsNullOrWhiteSpace(config.IntegritySecretEncrypted))
        {
            return new WompiCheckoutResult(false, null, null, "La pasarela Wompi no esta configurada (faltan llave publica o secret de integridad).");
        }

        var subscription = await _db.TenantSubscriptions
            .Where(s => s.TenantId == tenantId)
            .OrderByDescending(s => s.StartsAt)
            .FirstOrDefaultAsync(cancellationToken);
        if (subscription is null)
        {
            return new WompiCheckoutResult(false, null, null, "La agencia no tiene una suscripcion activa.");
        }

        var plan = await _db.SaasPlans.AsNoTracking().FirstOrDefaultAsync(p => p.Id == subscription.PlanId, cancellationToken);
        var amount = (subscription.BillingFrequency == BillingFrequency.Yearly ? plan?.YearlyPrice : plan?.MonthlyPrice) ?? 0m;
        if (amount <= 0m)
        {
            return new WompiCheckoutResult(false, null, null, "El plan no tiene un precio configurado para cobrar.");
        }

        var currency = string.IsNullOrWhiteSpace(config.Currency) ? "COP" : config.Currency.Trim().ToUpperInvariant();

        string integritySecret;
        try
        {
            integritySecret = _secretProtector.Unprotect(config.IntegritySecretEncrypted);
        }
        catch
        {
            return new WompiCheckoutResult(false, null, null, "El secret de integridad esta cifrado con una llave anterior; vuelve a guardarlo.");
        }

        // Un solo cobro pendiente por periodo: si ya hay uno, se reutiliza (no se duplica la factura).
        var existing = await _db.TenantPayments
            .FirstOrDefaultAsync(p => p.SubscriptionId == subscription.Id
                                      && p.Status == PaymentStatus.Pending
                                      && p.BillingPeriodEnd == subscription.CurrentPeriodEndsAt,
                cancellationToken);

        string reference;
        long amountInCents;
        if (existing is not null)
        {
            reference = existing.ProviderReference!;
            amountInCents = (long)decimal.Round(existing.Amount * 100m, 0);
            currency = existing.Currency;
        }
        else
        {
            amountInCents = (long)decimal.Round(amount * 100m, 0);
            reference = $"SUB-{tenantId:N}-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}";
            var payment = new TenantPayment
            {
                TenantId = tenantId,
                SubscriptionId = subscription.Id,
                Provider = "Wompi",
                ProviderReference = reference,
                Amount = amount,
                Currency = currency,
                Status = PaymentStatus.Pending,
                BillingPeriodStart = DateTimeOffset.UtcNow,
                BillingPeriodEnd = subscription.CurrentPeriodEndsAt
            };
            _db.TenantPayments.Add(payment);
            _audit.Write(actorUserId, "payment.checkout", nameof(TenantPayment), payment.Id,
                previousValue: null,
                newValue: new { reference, amountInCents, currency },
                tenantId: tenantId);
            await _db.SaveChangesAsync(cancellationToken);
        }

        var signature = Sha256Hex($"{reference}{amountInCents}{currency}{integritySecret}");

        // El nombre del parametro lleva ':'; debe ir codificado (%3A) o el WAF de Wompi/CloudFront
        // bloquea la peticion (403). El navegador, al enviar el form GET, tambien lo codifica.
        var url = $"{CheckoutBaseUrl}?public-key={config.PublicKey}" +
                  $"&currency={currency}" +
                  $"&amount-in-cents={amountInCents}" +
                  $"&reference={Uri.EscapeDataString(reference)}" +
                  $"&signature%3Aintegrity={signature}";

        // Wompi rechaza redirect-url no publicas (p.ej. http://localhost). Solo se incluye si es
        // https y no apunta a localhost; en desarrollo se omite (Wompi muestra su pagina de
        // resultado) y la confirmacion la aplica igualmente el webhook.
        if (returnUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            && !returnUrl.Contains("localhost", StringComparison.OrdinalIgnoreCase))
        {
            url += $"&redirect-url={Uri.EscapeDataString(returnUrl)}";
        }

        return new WompiCheckoutResult(true, url, reference, null);
    }

    private static string Sha256Hex(string input) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input))).ToLowerInvariant();
}
