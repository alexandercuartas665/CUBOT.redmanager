using CubotRedManager.Application.Abstractions;
using CubotRedManager.Application.Common.Auth;
using CubotRedManager.Domain.Entities;
using CubotRedManager.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace CubotRedManager.Application.Auth;

public sealed class AuthService : IAuthService
{
    // Estados de tenant que permiten operar/iniciar sesion. Redmanager no tiene PendingPayment;
    // usa Trial, Active y PastDue como estados "operables".
    // TODO redmanager: cuando se agregue PendingPayment al enum, incluirlo aqui.
    private static readonly TenantStatus[] OperableStatuses =
    [
        TenantStatus.Trial,
        TenantStatus.Active,
        TenantStatus.PastDue
    ];

    private readonly IApplicationDbContext _db;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IJwtTokenService _tokenService;

    public AuthService(IApplicationDbContext db, IPasswordHasher passwordHasher, IJwtTokenService tokenService)
    {
        _db = db;
        _passwordHasher = passwordHasher;
        _tokenService = tokenService;
    }

    public async Task<ChangePasswordResult> ChangePasswordAsync(Guid userId, string currentPassword, string newPassword, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 6)
        {
            return new ChangePasswordResult(false, "La nueva clave debe tener al menos 6 caracteres.");
        }

        var user = await _db.PlatformUsers.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (user is null)
        {
            return new ChangePasswordResult(false, "Usuario no encontrado.");
        }
        if (string.IsNullOrEmpty(user.PasswordHash) || !_passwordHasher.Verify(user.PasswordHash, currentPassword))
        {
            return new ChangePasswordResult(false, "La clave actual no es correcta.");
        }

        user.PasswordHash = _passwordHasher.Hash(newPassword);
        await _db.SaveChangesAsync(cancellationToken);
        return new ChangePasswordResult(true, null);
    }

    public async Task<TokenResponse?> AuthenticateAsync(LoginRequest request, CancellationToken cancellationToken = default)
    {
        var email = Normalize(request.Email);

        var user = await _db.PlatformUsers
            .FirstOrDefaultAsync(u => u.Email == email, cancellationToken);

        // TODO redmanager: PlatformUser no tiene Status (vs travels). Si se agrega, validar Active aqui.
        if (user is null
            || string.IsNullOrEmpty(user.PasswordHash)
            || !_passwordHasher.Verify(user.PasswordHash, request.Password))
        {
            return null;
        }

        var memberships = await GetMembershipsAsync(user.Id, cancellationToken);

        // Si pidio un tenant explicito, debe pertenecer a el.
        if (request.TenantId is Guid requestedTenant)
        {
            var match = memberships.FirstOrDefault(m => m.TenantId == requestedTenant);
            return match is null ? null : BuildToken(user, match);
        }

        return memberships.Count switch
        {
            1 => BuildToken(user, memberships[0]),
            > 1 => BuildToken(user, membership: null, tenantSelectionRequired: true),
            _ => BuildToken(user, membership: null)
        };
    }

    public async Task<TokenResponse?> SwitchTenantAsync(Guid userId, Guid tenantId, CancellationToken cancellationToken = default)
    {
        var user = await _db.PlatformUsers.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (user is null)
        {
            return null;
        }

        var memberships = await GetMembershipsAsync(userId, cancellationToken);
        var match = memberships.FirstOrDefault(m => m.TenantId == tenantId);
        return match is null ? null : BuildToken(user, match);
    }

    public async Task<MeResponse?> GetMeAsync(Guid userId, Guid? currentTenantId, CancellationToken cancellationToken = default)
    {
        var user = await _db.PlatformUsers.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (user is null)
        {
            return null;
        }

        var memberships = await GetMembershipsAsync(userId, cancellationToken);
        var tenants = memberships
            .Select(m => new TenantSummary(m.TenantId, m.Name, m.TenantRole.ToString()))
            .ToList();

        return new MeResponse(
            user.Id,
            user.Email,
            user.DisplayName,
            user.PlatformRole?.ToString(),
            currentTenantId,
            tenants);
    }

    private async Task<List<Membership>> GetMembershipsAsync(Guid userId, CancellationToken cancellationToken)
    {
        // IgnoreQueryFilters: resolver las agencias del usuario es una operacion cross-tenant legitima
        // que ocurre antes de fijar el tenant activo.
        return await _db.TenantUsers
            .IgnoreQueryFilters()
            .Where(tu => tu.PlatformUserId == userId && tu.Status == PlatformUserStatus.Active)
            .Join(
                _db.Tenants.Where(t => OperableStatuses.Contains(t.Status)),
                tu => tu.TenantId,
                t => t.Id,
                (tu, t) => new Membership(t.Id, t.Name, tu.TenantRole))
            .ToListAsync(cancellationToken);
    }

    private TokenResponse BuildToken(PlatformUser user, Membership? membership, bool tenantSelectionRequired = false)
    {
        var claims = new TokenClaims(
            UserId: user.Id,
            Email: user.Email,
            DisplayName: user.DisplayName,
            TenantId: membership?.TenantId,
            PlatformRole: user.PlatformRole?.ToString(),
            TenantRole: membership?.TenantRole.ToString(),
            Permissions: []);

        var issued = _tokenService.Create(claims);
        return new TokenResponse(issued.AccessToken, issued.ExpiresAt, membership?.TenantId, tenantSelectionRequired);
    }

    private static string Normalize(string email) => email.Trim().ToLowerInvariant();

    private sealed record Membership(Guid TenantId, string Name, TenantRole TenantRole);
}
