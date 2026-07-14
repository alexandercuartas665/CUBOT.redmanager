namespace CubotRedManager.Application.Tenancy;

/// <summary>
/// Item de UI: una linea WhatsApp del tenant con su estado de binding hacia un agente.
/// IsBoundHere indica si la linea esta atendida por el agente indicado.
/// AutoConfirm es el modo del binding (true = la IA envia sola; false = redacta y deja al
/// asesor confirmar - pendiente de UI de bandeja).
/// BoundAgentId/BoundAgentName: si la linea ya esta tomada por OTRO agente, se muestra
/// como ocupada en la UI.
/// </summary>
public sealed record AgentLineBindingRowDto(
    Guid WhatsAppLineId,
    string LineInstanceName,
    string? LinePhoneNumber,
    bool IsBoundHere,
    bool AutoConfirm,
    Guid? BoundAgentId,
    string? BoundAgentName);

/// <summary>Solicitud de toggle: el llamador define si la linea queda atendida por el agente.</summary>
public sealed record SetAgentLineBindingRequest(Guid AgentId, Guid WhatsAppLineId, bool Connected, bool AutoConfirm = true);

/// <summary>
/// Gobierna el vinculo agente-linea: que linea WhatsApp atiende cada agente IA.
/// Una linea puede estar atendida por un solo agente a la vez (regla: unique index
/// (tenant, line, is_connected=true) en BD).
/// Portado 1:1 desde CUBOT.travels.
/// </summary>
public interface IAiAgentLineBindingService
{
    /// <summary>Lista todas las lineas del tenant indicando si la atiende el agente dado.</summary>
    Task<IReadOnlyList<AgentLineBindingRowDto>> ListForAgentAsync(Guid agentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Crea o desconecta el binding. Si otra linea esta atendida por OTRO agente, el
    /// servicio rehusa y devuelve false (la UI debe desconectar primero al otro agente).
    /// </summary>
    Task<bool> SetAsync(SetAgentLineBindingRequest request, Guid actorUserId, CancellationToken cancellationToken = default);
}
