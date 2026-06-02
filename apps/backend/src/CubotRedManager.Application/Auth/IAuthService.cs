namespace CubotRedManager.Application.Auth;

/// <summary>
/// Casos de uso de autenticacion local y resolucion de tenant. Devuelve null cuando la
/// operacion no es valida (credenciales incorrectas, sin acceso al tenant, usuario inexistente).
/// </summary>
public sealed record ChangePasswordResult(bool Ok, string? Error);

public interface IAuthService
{
    Task<TokenResponse?> AuthenticateAsync(LoginRequest request, CancellationToken cancellationToken = default);
    Task<TokenResponse?> SwitchTenantAsync(Guid userId, Guid tenantId, CancellationToken cancellationToken = default);
    Task<MeResponse?> GetMeAsync(Guid userId, Guid? currentTenantId, CancellationToken cancellationToken = default);

    /// <summary>Cambia la clave del usuario verificando la actual. Ok=false con motivo si falla.</summary>
    Task<ChangePasswordResult> ChangePasswordAsync(Guid userId, string currentPassword, string newPassword, CancellationToken cancellationToken = default);
}
