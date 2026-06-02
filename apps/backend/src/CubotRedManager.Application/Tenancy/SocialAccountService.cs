using CubotRedManager.Application.Abstractions;
using CubotRedManager.Domain.Entities;
using CubotRedManager.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace CubotRedManager.Application.Tenancy;

public sealed class SocialAccountService : ISocialAccountService
{
    // Catalogo base de redes soportadas (Fase 1).
    private static readonly (string Code, string Name, string Color)[] DefaultNetworks =
    {
        ("meta_facebook", "Facebook", "#1877F2"),
        ("meta_instagram", "Instagram", "#E1306C"),
        ("tiktok", "TikTok", "#010101"),
        ("youtube", "YouTube", "#FF0000")
    };

    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly IAuditWriter _audit;
    private readonly TimeProvider _time;

    public SocialAccountService(IApplicationDbContext db, ITenantContext tenantContext, IAuditWriter audit, TimeProvider time)
    {
        _db = db;
        _tenantContext = tenantContext;
        _audit = audit;
        _time = time;
    }

    public async Task<IReadOnlyList<SocialNetworkDto>> ListNetworksAsync(CancellationToken cancellationToken = default)
    {
        var existing = await _db.SocialNetworks.AsNoTracking().ToListAsync(cancellationToken);
        if (existing.Count == 0)
        {
            // Seed perezoso del catalogo global de redes.
            foreach (var (code, name, color) in DefaultNetworks)
            {
                _db.SocialNetworks.Add(new SocialNetwork { Code = code, DisplayName = name, ColorHex = color, IsEnabled = true });
            }
            await _db.SaveChangesAsync(cancellationToken);
            existing = await _db.SocialNetworks.AsNoTracking().ToListAsync(cancellationToken);
        }
        return existing
            .OrderBy(n => n.DisplayName)
            .Select(n => new SocialNetworkDto(n.Code, n.DisplayName, n.ColorHex, n.IsEnabled))
            .ToList();
    }

    public async Task<IReadOnlyList<SocialAccountDto>> ListAsync(Guid? clientId = null, CancellationToken cancellationToken = default)
    {
        var networks = (await ListNetworksAsync(cancellationToken)).ToDictionary(n => n.Code);

        var query = from a in _db.SocialAccounts.AsNoTracking()
                    join c in _db.Clients.AsNoTracking() on a.ClientId equals c.Id
                    select new { a, ClientName = c.Name };
        if (clientId is Guid cid) { query = query.Where(x => x.a.ClientId == cid); }

        var rows = await query.OrderBy(x => x.ClientName).ThenBy(x => x.a.NetworkCode).ToListAsync(cancellationToken);

        return rows.Select(x =>
        {
            networks.TryGetValue(x.a.NetworkCode, out var net);
            return new SocialAccountDto(x.a.Id, x.a.ClientId, x.ClientName, x.a.NetworkCode,
                net?.DisplayName ?? x.a.NetworkCode, net?.ColorHex ?? "#A03DC9",
                x.a.Handle, x.a.DisplayName, x.a.Status, x.a.ExpiresAt, x.a.LastSyncAt,
                x.a.FollowersCount, x.a.AvatarUrl, x.a.Bio);
        }).ToList();
    }

    public async Task<bool> DeleteAsync(Guid id, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var account = await _db.SocialAccounts.FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
        if (account is null) { return false; }
        var tenantId = account.TenantId;
        _db.SocialAccounts.Remove(account);
        _audit.Write(actorUserId, "social-account.delete", nameof(SocialAccount), id,
            previousValue: new { account.NetworkCode, account.ClientId, account.Handle }, newValue: null, tenantId: tenantId);
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<SocialAccountDto?> ConnectDemoAsync(ConnectSocialAccountRequest request, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId) { return null; }
        var client = await _db.Clients.FirstOrDefaultAsync(c => c.Id == request.ClientId, cancellationToken);
        if (client is null) { return null; }

        var handle = request.Handle?.Trim();
        var externalId = string.IsNullOrWhiteSpace(handle) ? Guid.CreateVersion7().ToString("N")[..12] : handle!;
        var existing = await _db.SocialAccounts.FirstOrDefaultAsync(
            a => a.ClientId == request.ClientId && a.NetworkCode == request.NetworkCode && a.ExternalId == externalId, cancellationToken);
        if (existing is not null) { return null; }

        var account = new SocialAccount
        {
            TenantId = tenantId,
            ClientId = request.ClientId,
            NetworkCode = request.NetworkCode,
            ExternalId = externalId,
            Handle = handle,
            DisplayName = handle,
            Status = SocialAccountStatus.Connected,
            LastSyncAt = null,
            ExpiresAt = _time.GetUtcNow().AddDays(60)
        };
        _db.SocialAccounts.Add(account);
        _audit.Write(actorUserId, "social-account.connect", nameof(SocialAccount), account.Id,
            previousValue: null, newValue: new { account.NetworkCode, account.ClientId }, tenantId: tenantId);
        await _db.SaveChangesAsync(cancellationToken);
        return (await ListAsync(request.ClientId, cancellationToken)).FirstOrDefault(a => a.Id == account.Id);
    }

    public async Task<SocialAccountDto?> ChangeStatusAsync(Guid id, SocialAccountStatus status, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var account = await _db.SocialAccounts.FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
        if (account is null) { return null; }
        account.Status = status;
        if (status == SocialAccountStatus.Disconnected || status == SocialAccountStatus.Revoked)
        {
            account.AccessTokenEncrypted = null;
            account.RefreshTokenEncrypted = null;
        }
        _audit.Write(actorUserId, "social-account.change-status", nameof(SocialAccount), account.Id,
            previousValue: null, newValue: new { status }, tenantId: account.TenantId);
        await _db.SaveChangesAsync(cancellationToken);
        return (await ListAsync(account.ClientId, cancellationToken)).FirstOrDefault(a => a.Id == id);
    }
}
