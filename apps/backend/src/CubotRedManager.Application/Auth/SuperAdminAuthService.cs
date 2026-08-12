using CubotRedManager.Application.Abstractions;
using CubotRedManager.Application.Common.Auth;
using CubotRedManager.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace CubotRedManager.Application.Auth;

/// <summary>
/// Login y emision de JWT para el super admin, disenado para consumir la Admin Agent API sin UI
/// (una instancia de Claude con el token). Se separa del login de la consola web (cookie) porque
/// necesita entregar un access_token portable que el cliente inyecta como Authorization: Bearer.
///
/// Failure modes explicitos:
///   InvalidCredentials -> email desconocido, sin PasswordHash, o Verify() falso.
///   NotSuperAdmin      -> el user existe y la clave es correcta pero PlatformRole != SuperAdmin.
///                         Distinguirlo evita fugar por-email si alguien es super admin o no
///                         (siempre respondemos "invalid credentials" al cliente en el endpoint).
/// </summary>
public interface ISuperAdminAuthService
{
    Task<SuperAdminLoginResult> LoginAsync(string email, string password, CancellationToken ct = default);
}

public sealed record SuperAdminLoginResponse(
    string Kind,
    string AccessToken,
    DateTimeOffset ExpiresAt,
    Guid UserId,
    string Email,
    string? DisplayName,
    string PlatformRole);

public abstract record SuperAdminLoginResult
{
    public sealed record Ok(SuperAdminLoginResponse Response) : SuperAdminLoginResult;
    public sealed record InvalidCredentials : SuperAdminLoginResult;
    public sealed record NotSuperAdmin : SuperAdminLoginResult;
}

public sealed class SuperAdminAuthService : ISuperAdminAuthService
{
    private readonly IApplicationDbContext _db;
    private readonly IPasswordHasher _hasher;
    private readonly IJwtTokenService _jwt;

    public SuperAdminAuthService(IApplicationDbContext db, IPasswordHasher hasher, IJwtTokenService jwt)
    {
        _db = db;
        _hasher = hasher;
        _jwt = jwt;
    }

    public async Task<SuperAdminLoginResult> LoginAsync(string email, string password, CancellationToken ct = default)
    {
        var normalized = (email ?? string.Empty).Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(normalized) || string.IsNullOrWhiteSpace(password))
        {
            return new SuperAdminLoginResult.InvalidCredentials();
        }

        var user = await _db.PlatformUsers.FirstOrDefaultAsync(u => u.Email == normalized, ct);
        if (user is null
            || string.IsNullOrEmpty(user.PasswordHash)
            || !_hasher.Verify(user.PasswordHash, password))
        {
            return new SuperAdminLoginResult.InvalidCredentials();
        }

        if (user.PlatformRole != PlatformRole.SuperAdmin)
        {
            return new SuperAdminLoginResult.NotSuperAdmin();
        }

        var issued = _jwt.Create(new TokenClaims(
            UserId: user.Id,
            Email: user.Email,
            DisplayName: user.DisplayName,
            TenantId: null,
            PlatformRole: PlatformRole.SuperAdmin.ToString(),
            TenantRole: null,
            Permissions: Array.Empty<string>()));

        return new SuperAdminLoginResult.Ok(new SuperAdminLoginResponse(
            Kind: "superadmin",
            AccessToken: issued.AccessToken,
            ExpiresAt: issued.ExpiresAt,
            UserId: user.Id,
            Email: user.Email,
            DisplayName: user.DisplayName,
            PlatformRole: PlatformRole.SuperAdmin.ToString()));
    }
}
