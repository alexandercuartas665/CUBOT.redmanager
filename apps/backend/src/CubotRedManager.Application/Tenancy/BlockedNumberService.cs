using CubotRedManager.Application.Abstractions;
using CubotRedManager.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CubotRedManager.Application.Tenancy;

public sealed class BlockedNumberService : IBlockedNumberService
{
    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly IAuditWriter _audit;

    public BlockedNumberService(IApplicationDbContext db, ITenantContext tenantContext, IAuditWriter audit)
    {
        _db = db;
        _tenantContext = tenantContext;
        _audit = audit;
    }

    public async Task<IReadOnlyList<BlockedNumberDto>> ListAsync(CancellationToken cancellationToken = default)
    {
        var rows = await _db.TenantBlockedNumbers.AsNoTracking()
            .OrderByDescending(b => b.CreatedAt)
            .Select(b => new BlockedNumberDto(b.Id, b.Phone, b.Note, b.CreatedAt))
            .ToListAsync(cancellationToken);
        return rows;
    }

    public async Task<BlockedNumberDto?> AddAsync(string phone, string? note, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId) { return null; }
        var digits = new string((phone ?? string.Empty).Where(char.IsDigit).ToArray());
        if (digits.Length < 7) { return null; }

        // Dedupe por digitos: si ya hay un numero equivalente (tolera codigo de pais), devolverlo.
        var existing = await _db.TenantBlockedNumbers
            .FirstOrDefaultAsync(b => b.Phone == digits, cancellationToken);
        if (existing is not null)
        {
            return new BlockedNumberDto(existing.Id, existing.Phone, existing.Note, existing.CreatedAt);
        }

        var row = new TenantBlockedNumber
        {
            TenantId = tenantId,
            Phone = digits,
            Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim()
        };
        _db.TenantBlockedNumbers.Add(row);
        _audit.Write(actorUserId, "blocked-number.add", nameof(TenantBlockedNumber), row.Id,
            previousValue: null, newValue: new { row.Phone }, tenantId: tenantId);
        await _db.SaveChangesAsync(cancellationToken);
        return new BlockedNumberDto(row.Id, row.Phone, row.Note, row.CreatedAt);
    }

    public async Task<bool> RemoveAsync(Guid id, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var row = await _db.TenantBlockedNumbers.FirstOrDefaultAsync(b => b.Id == id, cancellationToken);
        if (row is null) { return false; }
        _db.TenantBlockedNumbers.Remove(row);
        _audit.Write(actorUserId, "blocked-number.remove", nameof(TenantBlockedNumber), row.Id,
            previousValue: new { row.Phone }, newValue: null, tenantId: row.TenantId);
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }
}
