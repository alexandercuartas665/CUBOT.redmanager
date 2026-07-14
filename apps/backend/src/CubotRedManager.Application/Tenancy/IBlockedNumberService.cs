namespace CubotRedManager.Application.Tenancy;

/// <summary>Un numero en la lista negra global del tenant.</summary>
public sealed record BlockedNumberDto(Guid Id, string Phone, string? Note, DateTimeOffset CreatedAt);

/// <summary>
/// Lista negra GLOBAL del tenant: numeros que ningun agente de IA debe atender. Compartida por todos
/// los agentes de la agencia. Se administra desde su propio modulo y la consulta el dispatcher antes
/// de responder. Portado desde CUBOT.travels.
/// </summary>
public interface IBlockedNumberService
{
    Task<IReadOnlyList<BlockedNumberDto>> ListAsync(CancellationToken cancellationToken = default);
    /// <summary>Agrega un numero (normalizado a digitos). Devuelve null si el telefono es invalido o no hay tenant; si ya existia, devuelve el existente.</summary>
    Task<BlockedNumberDto?> AddAsync(string phone, string? note, Guid actorUserId, CancellationToken cancellationToken = default);
    Task<bool> RemoveAsync(Guid id, Guid actorUserId, CancellationToken cancellationToken = default);
}
