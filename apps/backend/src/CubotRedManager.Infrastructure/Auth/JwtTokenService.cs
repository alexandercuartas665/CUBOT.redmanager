using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using CubotRedManager.Application.Common.Auth;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace CubotRedManager.Infrastructure.Auth;

/// <summary>Emite el JWT propio de CUBOT.redmanager firmado con HMAC-SHA256.</summary>
public sealed class JwtTokenService : IJwtTokenService
{
    private readonly JwtSettings _settings;
    private readonly TimeProvider _timeProvider;

    public JwtTokenService(IOptions<JwtSettings> options, TimeProvider timeProvider)
    {
        _settings = options.Value;
        _timeProvider = timeProvider;
    }

    public IssuedToken Create(TokenClaims input)
    {
        var now = _timeProvider.GetUtcNow();
        var expiresAt = now.AddMinutes(_settings.AccessTokenMinutes);

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_settings.SigningKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, input.UserId.ToString()),
            new(JwtRegisteredClaimNames.Email, input.Email),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        if (!string.IsNullOrWhiteSpace(input.DisplayName))
        {
            claims.Add(new Claim("name", input.DisplayName));
        }

        if (input.TenantId is Guid tenantId)
        {
            claims.Add(new Claim("tenant_id", tenantId.ToString()));
        }

        if (!string.IsNullOrWhiteSpace(input.PlatformRole))
        {
            claims.Add(new Claim("platform_role", input.PlatformRole));
        }

        if (!string.IsNullOrWhiteSpace(input.TenantRole))
        {
            claims.Add(new Claim("tenant_role", input.TenantRole));
        }

        foreach (var permission in input.Permissions)
        {
            claims.Add(new Claim("permissions", permission));
        }

        var token = new JwtSecurityToken(
            issuer: _settings.Issuer,
            audience: _settings.Audience,
            claims: claims,
            notBefore: now.UtcDateTime,
            expires: expiresAt.UtcDateTime,
            signingCredentials: credentials);

        var encoded = new JwtSecurityTokenHandler().WriteToken(token);
        return new IssuedToken(encoded, expiresAt);
    }
}
