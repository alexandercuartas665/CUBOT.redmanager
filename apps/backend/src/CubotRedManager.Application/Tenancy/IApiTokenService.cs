namespace CubotRedManager.Application.Tenancy;

/// <summary>Fila visible en la UI de gestion de tokens del user (nunca incluye el token en plain).</summary>
public sealed record ApiTokenDto(
    Guid Id,
    string Label,
    string Prefix,          // primeros 6 chars del token en plain, para distinguir cual es cual
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? LastUsedAt,
    bool Revoked);

/// <summary>Resultado de creacion: incluye UNA sola vez el token en plain para que el operador lo copie.</summary>
public sealed record CreatedApiTokenDto(ApiTokenDto Token, string PlainToken);

/// <summary>Contexto validado tras aceptar un token: al llamador le sirve para setear el ambient tenant.</summary>
public sealed record ApiTokenIdentity(Guid TokenId, Guid TenantId, Guid UserId);

/// <summary>Emision / listado / revocacion de tokens de API opacos por usuario del tenant.</summary>
public interface IApiTokenService
{
    Task<CreatedApiTokenDto?> CreateAsync(string label, TimeSpan ttl, Guid actorUserId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ApiTokenDto>> ListForCurrentUserAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<bool> RevokeAsync(Guid tokenId, Guid actorUserId, CancellationToken cancellationToken = default);

    /// <summary>Valida un token en plain contra la BD. Devuelve la identidad si es valido, no
    /// revocado y no expirado. Actualiza LastUsedAt de forma perezosa. Devuelve null en cualquier
    /// otro caso (token invalido, expirado, revocado, hash no encontrado).</summary>
    Task<ApiTokenIdentity?> ValidateAsync(string plainToken, CancellationToken cancellationToken = default);
}
