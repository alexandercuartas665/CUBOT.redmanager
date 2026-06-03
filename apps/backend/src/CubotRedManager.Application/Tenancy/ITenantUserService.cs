namespace CubotRedManager.Application.Tenancy;

/// <summary>
/// Gestion de usuarios dentro del tenant activo (modulo 1.2). Las operaciones quedan acotadas
/// al tenant del contexto (filtro global de consulta + estampado en alta).
/// </summary>
public interface ITenantUserService
{
    Task<IReadOnlyList<TenantUserDto>> ListAsync(CancellationToken cancellationToken = default);
}
