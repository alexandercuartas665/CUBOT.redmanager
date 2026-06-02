using CubotRedManager.Application.Abstractions;
using CubotRedManager.Domain.Entities;
using CubotRedManager.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace CubotRedManager.Application.Admin;

/// <summary>
/// Debito automatico de suscripciones. Guarda la fuente de pago de Wompi en la suscripcion y
/// cobra contra ella en cada corte. La confirmacion la termina el webhook.
/// </summary>
public sealed class RecurringBillingService : IRecurringBillingService
{
    private const int MaxAttemptsBeforeGrace = 3;
    private const int GraceDays = 3;

    private readonly IApplicationDbContext _db;
    private readonly IWompiApiClient _wompi;
    private readonly IAuditWriter _audit;

    public RecurringBillingService(IApplicationDbContext db, IWompiApiClient wompi, IAuditWriter audit)
    {
        _db = db;
        _wompi = wompi;
        _audit = audit;
    }

    public async Task<RecurringResult> EnableAutoRenewAsync(Guid tenantId, string cardToken, string customerEmail, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var subscription = await LatestSubscriptionAsync(tenantId, cancellationToken);
        if (subscription is null)
        {
            return new RecurringResult(false, null, "La agencia no tiene una suscripcion activa.");
        }

        var acceptance = await _wompi.GetAcceptanceTokenAsync(cancellationToken);
        if (!acceptance.Ok || acceptance.AcceptanceToken is null)
        {
            return new RecurringResult(false, null, $"No se pudo obtener el token de aceptacion: {acceptance.Error}");
        }

        var source = await _wompi.CreateCardPaymentSourceAsync(cardToken, customerEmail, acceptance.AcceptanceToken, cancellationToken);
        if (!source.Ok || source.Id is null)
        {
            return new RecurringResult(false, null, $"No se pudo guardar el metodo de pago: {source.Error}");
        }

        subscription.WompiPaymentSourceId = source.Id;
        subscription.PaymentMethodLabel = source.Label;
        subscription.AutoRenew = true;
        subscription.FailedAttempts = 0;

        _audit.Write(actorUserId, "subscription.autorenew.enable", nameof(TenantSubscription), subscription.Id,
            previousValue: null,
            newValue: new { subscription.WompiPaymentSourceId, subscription.PaymentMethodLabel },
            tenantId: tenantId);

        await _db.SaveChangesAsync(cancellationToken);
        return new RecurringResult(true, source.Label, null);
    }

    public async Task DisableAutoRenewAsync(Guid tenantId, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var subscription = await LatestSubscriptionAsync(tenantId, cancellationToken);
        if (subscription is null || !subscription.AutoRenew)
        {
            return;
        }

        subscription.AutoRenew = false;
        _audit.Write(actorUserId, "subscription.autorenew.disable", nameof(TenantSubscription), subscription.Id,
            previousValue: null, newValue: new { AutoRenew = false }, tenantId: tenantId);
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<int> ChargeDueSubscriptionsAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var due = await _db.TenantSubscriptions
            .Where(s => s.AutoRenew
                        && s.WompiPaymentSourceId != null
                        && s.Status != SubscriptionStatus.Cancelled
                        && s.CurrentPeriodEndsAt <= now)
            .ToListAsync(cancellationToken);

        var processed = 0;
        foreach (var subscription in due)
        {
            await ChargeAsync(subscription, actorUserId: Guid.Empty, system: true, cancellationToken);
            processed++;
        }
        return processed;
    }

    public async Task<RecurringResult> ChargeNowAsync(Guid tenantId, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var subscription = await LatestSubscriptionAsync(tenantId, cancellationToken);
        if (subscription is null)
        {
            return new RecurringResult(false, null, "Sin suscripcion.");
        }
        if (!subscription.AutoRenew || subscription.WompiPaymentSourceId is null)
        {
            return new RecurringResult(false, null, "La suscripcion no tiene debito automatico activo.");
        }
        return await ChargeAsync(subscription, actorUserId, system: false, cancellationToken);
    }

    private async Task<RecurringResult> ChargeAsync(TenantSubscription subscription, Guid actorUserId, bool system, CancellationToken cancellationToken)
    {
        // Un solo cobro pendiente por periodo: no se generan facturas duplicadas.
        var hasPending = await _db.TenantPayments.AnyAsync(
            p => p.SubscriptionId == subscription.Id
                 && p.Status == PaymentStatus.Pending
                 && p.BillingPeriodEnd == subscription.CurrentPeriodEndsAt,
            cancellationToken);
        if (hasPending)
        {
            return new RecurringResult(false, null, "Ya existe un cobro pendiente para este periodo.");
        }

        var plan = await _db.SaasPlans.AsNoTracking().FirstOrDefaultAsync(p => p.Id == subscription.PlanId, cancellationToken);
        var amount = (subscription.BillingFrequency == BillingFrequency.Yearly ? plan?.YearlyPrice : plan?.MonthlyPrice) ?? 0m;
        if (amount <= 0m)
        {
            return new RecurringResult(false, null, "El plan no tiene precio para cobrar.");
        }
        var currency = string.IsNullOrWhiteSpace(plan?.Currency) ? "COP" : plan!.Currency!.Trim().ToUpperInvariant();
        var amountInCents = (long)decimal.Round(amount * 100m, 0);
        var reference = $"AUTO-{subscription.TenantId:N}-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}";

        // Email de notificacion: primer Owner/Admin activo de la agencia. Se obtiene via join con
        // PlatformUser (en redmanager el email no vive en TenantUser, sino en la identidad global).
        var email = await _db.TenantUsers.IgnoreQueryFilters()
            .Where(u => u.TenantId == subscription.TenantId && u.Status == PlatformUserStatus.Active)
            .OrderBy(u => u.CreatedAt)
            .Join(_db.PlatformUsers, tu => tu.PlatformUserId, pu => pu.Id, (tu, pu) => pu.Email)
            .FirstOrDefaultAsync(cancellationToken) ?? "billing@cubot.redmanager";

        var payment = new TenantPayment
        {
            TenantId = subscription.TenantId,
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
        // Se confirma el pago Pendiente ANTES de cobrar: Wompi puede enviar el webhook casi de
        // inmediato y debe encontrar el pago por su referencia.
        await _db.SaveChangesAsync(cancellationToken);

        var charge = await _wompi.ChargePaymentSourceAsync(subscription.WompiPaymentSourceId!.Value, amountInCents, currency, reference, email, cancellationToken);

        var action = system ? "subscription.autocharge" : "subscription.autocharge.manual";
        if (!charge.Ok)
        {
            payment.Status = PaymentStatus.Error;
            subscription.FailedAttempts++;
            ApplyDunning(subscription);
            _audit.Write(actorUserId, $"{action}.failed", nameof(TenantSubscription), subscription.Id,
                previousValue: null, newValue: new { reference, error = charge.Error, subscription.FailedAttempts },
                tenantId: subscription.TenantId);
            await _db.SaveChangesAsync(cancellationToken);
            return new RecurringResult(false, null, charge.Error);
        }

        // APPROVED -> renueva ya; PENDING -> el webhook lo confirma luego (idempotente).
        var approved = string.Equals(charge.Status, "APPROVED", StringComparison.OrdinalIgnoreCase);
        payment.Status = approved ? PaymentStatus.Approved : PaymentStatus.Pending;
        if (approved)
        {
            payment.ConfirmedAt = DateTimeOffset.UtcNow;
            Renew(subscription);
        }

        _audit.Write(actorUserId, action, nameof(TenantSubscription), subscription.Id,
            previousValue: null, newValue: new { reference, charge.TransactionId, charge.Status },
            tenantId: subscription.TenantId);

        await _db.SaveChangesAsync(cancellationToken);
        return new RecurringResult(true, charge.Status, null);
    }

    private void Renew(TenantSubscription s)
    {
        var basis = s.CurrentPeriodEndsAt > DateTimeOffset.UtcNow ? s.CurrentPeriodEndsAt : DateTimeOffset.UtcNow;
        s.CurrentPeriodEndsAt = s.BillingFrequency == BillingFrequency.Yearly ? basis.AddYears(1) : basis.AddMonths(1);
        s.Status = SubscriptionStatus.Active;
        s.GracePeriodEndsAt = null;
        s.FailedAttempts = 0;
    }

    private static void ApplyDunning(TenantSubscription s)
    {
        if (s.FailedAttempts < MaxAttemptsBeforeGrace)
        {
            s.Status = SubscriptionStatus.PastDue;
            return;
        }
        s.GracePeriodEndsAt ??= DateTimeOffset.UtcNow.AddDays(GraceDays);
        s.Status = s.GracePeriodEndsAt <= DateTimeOffset.UtcNow
            ? SubscriptionStatus.Suspended
            : SubscriptionStatus.GracePeriod;
    }

    private Task<TenantSubscription?> LatestSubscriptionAsync(Guid tenantId, CancellationToken ct) =>
        _db.TenantSubscriptions
            .Where(s => s.TenantId == tenantId)
            .OrderByDescending(s => s.StartsAt)
            .FirstOrDefaultAsync(ct);
}

/// <summary>
/// Stub no-funcional del cliente HTTP de Wompi. Devuelve "no configurado" en todas las operaciones.
/// TODO redmanager: portar el cliente HTTP real desde Infrastructure/Wompi (travels lo tiene en
/// CubotTravels.Infrastructure.Wompi.WompiApiClient). Mientras no exista, el panel de debito
/// automatico mostrara "La pasarela Wompi no esta configurada" y no podra activarse.
/// </summary>
public sealed class StubWompiApiClient : IWompiApiClient
{
    public Task<WompiAcceptance> GetAcceptanceTokenAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(new WompiAcceptance(false, null, "Cliente Wompi no configurado en esta instalacion."));

    public Task<WompiPaymentSourceResult> CreateCardPaymentSourceAsync(string cardToken, string customerEmail, string acceptanceToken, CancellationToken cancellationToken = default) =>
        Task.FromResult(new WompiPaymentSourceResult(false, null, null, "Cliente Wompi no configurado en esta instalacion."));

    public Task<WompiChargeResult> ChargePaymentSourceAsync(long paymentSourceId, long amountInCents, string currency, string reference, string customerEmail, CancellationToken cancellationToken = default) =>
        Task.FromResult(new WompiChargeResult(false, null, null, "Cliente Wompi no configurado en esta instalacion."));
}
