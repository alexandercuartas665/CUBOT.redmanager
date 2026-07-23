using System.Security.Cryptography;
using System.Text;
using CubotRedManager.Application.Abstractions;
using CubotRedManager.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CubotRedManager.Application.Tenancy;

public sealed class ApiTokenService : IApiTokenService
{
    private const string Prefix = "cubot_";
    private const int PlainByteLength = 32; // 32 bytes base64url ~ 43 chars, plenty of entropy

    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly IAuditWriter _audit;
    private readonly TimeProvider _time;

    public ApiTokenService(IApplicationDbContext db, ITenantContext tenantContext, IAuditWriter audit, TimeProvider time)
    {
        _db = db;
        _tenantContext = tenantContext;
        _audit = audit;
        _time = time;
    }

    public async Task<CreatedApiTokenDto?> CreateAsync(string label, TimeSpan ttl, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId) { return null; }
        var cleanLabel = (label ?? "").Trim();
        if (string.IsNullOrEmpty(cleanLabel)) { cleanLabel = "Token"; }
        if (cleanLabel.Length > 120) { cleanLabel = cleanLabel.Substring(0, 120); }

        // TTL de seguridad: entre 1h y 90d. Valores fuera de rango se recortan.
        if (ttl < TimeSpan.FromHours(1)) { ttl = TimeSpan.FromHours(24); }
        if (ttl > TimeSpan.FromDays(90)) { ttl = TimeSpan.FromDays(90); }

        // Generar token: prefijo humano + 32 bytes random base64url. Ejemplo: cubot_XYZ...
        var rand = RandomNumberGenerator.GetBytes(PlainByteLength);
        var body = Convert.ToBase64String(rand).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var plainToken = Prefix + body;

        var hash = Sha256Hex(plainToken);
        var now = _time.GetUtcNow();

        var entity = new ApiToken
        {
            TenantId = tenantId,
            UserId = actorUserId,
            Label = cleanLabel,
            TokenHash = hash,
            TokenPrefix = plainToken.Substring(0, Math.Min(12, plainToken.Length)),
            ExpiresAt = now.Add(ttl),
            LastUsedAt = null,
            RevokedAt = null
        };
        _db.ApiTokens.Add(entity);
        _audit.Write(actorUserId, "api-token.create", nameof(ApiToken), entity.Id,
            previousValue: null, newValue: new { entity.Label, entity.ExpiresAt }, tenantId: tenantId);
        await _db.SaveChangesAsync(cancellationToken);

        var dto = Map(entity, now);
        return new CreatedApiTokenDto(dto, plainToken);
    }

    public async Task<IReadOnlyList<ApiTokenDto>> ListForCurrentUserAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is not Guid) { return Array.Empty<ApiTokenDto>(); }
        var now = _time.GetUtcNow();
        var list = await _db.ApiTokens.AsNoTracking()
            .Where(t => t.UserId == userId)
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync(cancellationToken);
        return list.Select(t => Map(t, now)).ToList();
    }

    public async Task<bool> RevokeAsync(Guid tokenId, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var t = await _db.ApiTokens.FirstOrDefaultAsync(x => x.Id == tokenId, cancellationToken);
        if (t is null) { return false; }
        if (t.RevokedAt is not null) { return true; }
        t.RevokedAt = _time.GetUtcNow();
        _audit.Write(actorUserId, "api-token.revoke", nameof(ApiToken), t.Id,
            previousValue: null, newValue: new { t.Label }, tenantId: t.TenantId);
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<ApiTokenIdentity?> ValidateAsync(string plainToken, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(plainToken)) { return null; }
        var hash = Sha256Hex(plainToken.Trim());
        // IgnoreQueryFilters porque el token identifica al tenant (no lo sabemos antes de validar).
        var entity = await _db.ApiTokens.IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.TokenHash == hash, cancellationToken);
        if (entity is null) { return null; }
        var now = _time.GetUtcNow();
        if (entity.RevokedAt is not null) { return null; }
        if (entity.ExpiresAt <= now) { return null; }

        // Actualizacion perezosa de LastUsedAt: solo si cambio significativamente (evitar write en cada request).
        if (entity.LastUsedAt is null || (now - entity.LastUsedAt.Value) > TimeSpan.FromMinutes(1))
        {
            entity.LastUsedAt = now;
            try { await _db.SaveChangesAsync(cancellationToken); }
            catch { /* no fatal: la validacion sigue siendo valida */ }
        }
        return new ApiTokenIdentity(entity.Id, entity.TenantId, entity.UserId);
    }

    private static ApiTokenDto Map(ApiToken t, DateTimeOffset now)
        => new(t.Id, t.Label, t.TokenPrefix, t.CreatedAt, t.ExpiresAt, t.LastUsedAt,
            Revoked: t.RevokedAt is not null || t.ExpiresAt <= now);

    private static string Sha256Hex(string s)
    {
        var bytes = Encoding.UTF8.GetBytes(s);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
