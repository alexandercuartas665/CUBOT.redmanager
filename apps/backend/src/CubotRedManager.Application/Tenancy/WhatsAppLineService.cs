using CubotRedManager.Application.Abstractions;
using CubotRedManager.Domain.Entities;
using CubotRedManager.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace CubotRedManager.Application.Tenancy;

public sealed class WhatsAppLineService : IWhatsAppLineService
{
    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly IAuditWriter _audit;
    private readonly TimeProvider _timeProvider;

    public WhatsAppLineService(IApplicationDbContext db, ITenantContext tenantContext, IAuditWriter audit, TimeProvider timeProvider)
    {
        _db = db;
        _tenantContext = tenantContext;
        _audit = audit;
        _timeProvider = timeProvider;
    }

    public async Task<IReadOnlyList<WhatsAppLineDto>> ListAsync(CancellationToken cancellationToken = default)
    {
        return await _db.WhatsAppLines
            .AsNoTracking()
            .OrderBy(l => l.InstanceName)
            .Select(l => new WhatsAppLineDto(l.Id, l.InstanceName, l.PhoneNumber, l.Status, l.AssignedToTenantUserId, l.LastConnectedAt, l.LastStatusAt))
            .ToListAsync(cancellationToken);
    }

    public async Task<WhatsAppLineDto?> CreateAsync(CreateWhatsAppLineRequest request, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId)
        {
            return null;
        }

        var line = new WhatsAppLine
        {
            TenantId = tenantId,
            InstanceName = request.InstanceName.Trim(),
            PhoneNumber = request.PhoneNumber?.Trim(),
            Status = WhatsAppLineStatus.Created,
            LastStatusAt = _timeProvider.GetUtcNow()
        };
        _db.WhatsAppLines.Add(line);

        _audit.Write(actorUserId, "whatsapp-line.create", nameof(WhatsAppLine), line.Id,
            previousValue: null, newValue: new { line.InstanceName, line.PhoneNumber }, tenantId: tenantId);

        await _db.SaveChangesAsync(cancellationToken);
        return Map(line);
    }

    public async Task<WhatsAppLineDto?> ChangeStatusAsync(Guid lineId, WhatsAppLineStatus status, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var line = await _db.WhatsAppLines.FirstOrDefaultAsync(l => l.Id == lineId, cancellationToken);
        if (line is null)
        {
            return null;
        }

        var previous = line.Status;
        if (previous != status)
        {
            var now = _timeProvider.GetUtcNow();
            line.Status = status;
            line.LastStatusAt = now;
            if (status == WhatsAppLineStatus.Connected)
            {
                line.LastConnectedAt = now;
            }

            _audit.Write(actorUserId, "whatsapp-line.change-status", nameof(WhatsAppLine), line.Id,
                previousValue: new { Status = previous }, newValue: new { Status = status }, tenantId: line.TenantId);

            await _db.SaveChangesAsync(cancellationToken);
        }

        return Map(line);
    }

    public async Task<WhatsAppLineDto?> AssignAsync(Guid lineId, Guid? tenantUserId, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var line = await _db.WhatsAppLines.FirstOrDefaultAsync(l => l.Id == lineId, cancellationToken);
        if (line is null)
        {
            return null;
        }

        if (tenantUserId is Guid userId)
        {
            // El filtro global garantiza que solo se valida contra usuarios de la agencia activa.
            var belongs = await _db.TenantUsers.AnyAsync(tu => tu.Id == userId, cancellationToken);
            if (!belongs)
            {
                return null;
            }
        }

        line.AssignedToTenantUserId = tenantUserId;
        _audit.Write(actorUserId, "whatsapp-line.assign", nameof(WhatsAppLine), line.Id,
            previousValue: null, newValue: new { AssignedToTenantUserId = tenantUserId }, tenantId: line.TenantId);

        await _db.SaveChangesAsync(cancellationToken);
        return Map(line);
    }

    private static WhatsAppLineDto Map(WhatsAppLine l) =>
        new(l.Id, l.InstanceName, l.PhoneNumber, l.Status, l.AssignedToTenantUserId, l.LastConnectedAt, l.LastStatusAt);
}
