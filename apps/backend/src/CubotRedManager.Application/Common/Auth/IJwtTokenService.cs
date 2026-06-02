namespace CubotRedManager.Application.Common.Auth;

/// <summary>Datos que se incrustan en el JWT propio.</summary>
public sealed record TokenClaims(
    Guid UserId,
    string Email,
    string? DisplayName,
    Guid? TenantId,
    string? PlatformRole,
    string? TenantRole,
    IReadOnlyList<string> Permissions);

/// <summary>Resultado de emitir un token.</summary>
public sealed record IssuedToken(string AccessToken, DateTimeOffset ExpiresAt);

public interface IJwtTokenService
{
    IssuedToken Create(TokenClaims claims);
}
