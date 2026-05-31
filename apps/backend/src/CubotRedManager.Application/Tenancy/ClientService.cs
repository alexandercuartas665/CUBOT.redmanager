using CubotRedManager.Application.Abstractions;
using CubotRedManager.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CubotRedManager.Application.Tenancy;

public sealed class ClientService : IClientService
{
    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly IAuditWriter _audit;

    public ClientService(IApplicationDbContext db, ITenantContext tenantContext, IAuditWriter audit)
    {
        _db = db;
        _tenantContext = tenantContext;
        _audit = audit;
    }

    public async Task<IReadOnlyList<ClientDto>> ListAsync(CancellationToken cancellationToken = default)
    {
        var clients = await _db.Clients.AsNoTracking()
            .OrderByDescending(c => c.IsActive).ThenBy(c => c.Name)
            .ToListAsync(cancellationToken);
        var counts = await _db.UserClientLinks.AsNoTracking()
            .GroupBy(l => l.ClientId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, cancellationToken);
        return clients.Select(c => Map(c, counts.TryGetValue(c.Id, out var n) ? n : 0)).ToList();
    }

    public async Task<ClientDto?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var c = await _db.Clients.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (c is null) { return null; }
        var count = await _db.UserClientLinks.CountAsync(l => l.ClientId == id, cancellationToken);
        return Map(c, count);
    }

    public async Task<ClientDto?> CreateAsync(CreateClientRequest request, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId) { return null; }

        var client = new Client
        {
            TenantId = tenantId,
            Name = request.Name.Trim(),
            ContactName = request.ContactName?.Trim(),
            ContactEmail = request.ContactEmail?.Trim(),
            ContactPhone = request.ContactPhone?.Trim(),
            Industry = request.Industry?.Trim(),
            TimeZone = request.TimeZone?.Trim(),
            BrandToneNotes = request.BrandToneNotes?.Trim(),
            Notes = request.Notes?.Trim(),
            IsActive = true
        };
        _db.Clients.Add(client);
        _audit.Write(actorUserId, "client.create", nameof(Client), client.Id,
            previousValue: null, newValue: new { client.Name, client.Industry }, tenantId: tenantId);
        await _db.SaveChangesAsync(cancellationToken);
        return Map(client, 0);
    }

    public async Task<ClientDto?> UpdateAsync(Guid id, UpdateClientRequest request, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var client = await _db.Clients.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (client is null) { return null; }

        client.Name = request.Name.Trim();
        client.ContactName = request.ContactName?.Trim();
        client.ContactEmail = request.ContactEmail?.Trim();
        client.ContactPhone = request.ContactPhone?.Trim();
        client.Industry = request.Industry?.Trim();
        client.TimeZone = request.TimeZone?.Trim();
        client.BrandLogoUrl = request.BrandLogoUrl?.Trim();
        client.BrandColorsJson = request.BrandColorsJson;
        client.BrandToneNotes = request.BrandToneNotes?.Trim();
        client.Notes = request.Notes?.Trim();

        _audit.Write(actorUserId, "client.update", nameof(Client), client.Id,
            previousValue: null, newValue: new { client.Name }, tenantId: client.TenantId);
        await _db.SaveChangesAsync(cancellationToken);
        var count = await _db.UserClientLinks.CountAsync(l => l.ClientId == id, cancellationToken);
        return Map(client, count);
    }

    public async Task<ClientDto?> SetActiveAsync(Guid id, bool active, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var client = await _db.Clients.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (client is null) { return null; }
        client.IsActive = active;
        _audit.Write(actorUserId, active ? "client.activate" : "client.deactivate", nameof(Client), client.Id,
            previousValue: null, newValue: new { active }, tenantId: client.TenantId);
        await _db.SaveChangesAsync(cancellationToken);
        var count = await _db.UserClientLinks.CountAsync(l => l.ClientId == id, cancellationToken);
        return Map(client, count);
    }

    private static ClientDto Map(Client c, int operatorCount) =>
        new(c.Id, c.Name, c.ContactName, c.ContactEmail, c.ContactPhone, c.Industry, c.BrandLogoUrl,
            c.BrandColorsJson, c.BrandToneNotes, c.TimeZone, c.Notes, c.IsActive, operatorCount);
}
