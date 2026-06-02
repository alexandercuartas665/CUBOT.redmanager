namespace CubotRedManager.Application.Admin;

/// <summary>Vista de la config de Google para el Super Admin (sin exponer el secret).</summary>
public sealed record GoogleAuthConfigDto(string? ClientId, bool HasSecret, bool IsEnabled);

public sealed record SaveGoogleAuthConfigRequest(string? ClientId, string? ClientSecret, bool IsEnabled);

/// <summary>Credenciales en claro para el flujo OAuth (uso interno; nunca se devuelve al cliente).</summary>
public sealed record GoogleAuthCredentials(string ClientId, string ClientSecret, bool IsEnabled);

public interface IGoogleAuthConfigService
{
    Task<GoogleAuthConfigDto?> GetAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(SaveGoogleAuthConfigRequest request, Guid actorUserId, CancellationToken cancellationToken = default);
    /// <summary>Devuelve ClientId + secret descifrado para el flujo. Null si no esta configurado/habilitado.</summary>
    Task<GoogleAuthCredentials?> GetCredentialsAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// TODO redmanager: travels persiste esto en una tabla GoogleAuthConfig. Mientras no exista en
/// redmanager devolvemos null (Google deshabilitado) para que el login muestre solo correo/clave.
/// Cuando se agregue la entidad, replicar el servicio de travels (audit + ISecretProtector).
/// </summary>
public sealed class GoogleAuthConfigService : IGoogleAuthConfigService
{
    public Task<GoogleAuthConfigDto?> GetAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<GoogleAuthConfigDto?>(null);

    public Task SaveAsync(SaveGoogleAuthConfigRequest request, Guid actorUserId, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task<GoogleAuthCredentials?> GetCredentialsAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<GoogleAuthCredentials?>(null);
}
