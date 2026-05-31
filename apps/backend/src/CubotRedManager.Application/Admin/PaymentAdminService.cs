using CubotRedManager.Application.Abstractions;
using CubotRedManager.Domain.Entities;
using CubotRedManager.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace CubotRedManager.Application.Admin;

public sealed class PaymentAdminService : IPaymentAdminService
{
    private readonly IApplicationDbContext _db;
    private readonly IAuditWriter _audit;

    public PaymentAdminService(IApplicationDbContext db, IAuditWriter audit)
    {
        _db = db;
        _audit = audit;
    }

    public async Task<PaymentDetail?> RegisterAsync(RegisterPaymentRequest request, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var subscription = await _db.TenantSubscriptions
            .FirstOrDefaultAsync(s => s.Id == request.SubscriptionId && s.TenantId == request.TenantId, cancellationToken);
        if (subscription is null)
        {
            return null;
        }

        var payment = new TenantPayment
        {
            TenantId = request.TenantId,
            SubscriptionId = request.SubscriptionId,
            Provider = "manual",
            ProviderReference = request.ProviderReference?.Trim(),
            Amount = request.Amount,
            Currency = request.Currency.Trim(),
            Status = request.Status,
            BillingPeriodStart = request.BillingPeriodStart,
            BillingPeriodEnd = request.BillingPeriodEnd,
            ConfirmedAt = request.Status == PaymentStatus.Approved ? DateTimeOffset.UtcNow : null
        };

        _db.TenantPayments.Add(payment);
        _audit.Write(actorUserId, "payment.register", nameof(TenantPayment), payment.Id,
            previousValue: null,
            newValue: new { payment.Amount, payment.Currency, payment.Status },
            tenantId: request.TenantId);

        await _db.SaveChangesAsync(cancellationToken);
        return Map(payment);
    }

    public async Task<IReadOnlyList<PaymentDetail>> ListAsync(Guid? tenantId = null, PaymentStatus? status = null, CancellationToken cancellationToken = default)
    {
        var query = _db.TenantPayments.AsNoTracking();

        if (tenantId is Guid t)
        {
            query = query.Where(p => p.TenantId == t);
        }

        if (status is PaymentStatus s)
        {
            query = query.Where(p => p.Status == s);
        }

        return await query
            .OrderByDescending(p => p.CreatedAt)
            .Select(p => Map(p))
            .ToListAsync(cancellationToken);
    }

    private static PaymentDetail Map(TenantPayment p) =>
        new(p.Id, p.TenantId, p.SubscriptionId, p.Provider, p.ProviderReference, p.Amount, p.Currency, p.Status,
            p.BillingPeriodStart, p.BillingPeriodEnd, p.ConfirmedAt, p.CreatedAt);
}
