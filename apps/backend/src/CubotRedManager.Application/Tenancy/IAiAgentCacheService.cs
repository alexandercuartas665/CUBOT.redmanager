namespace CubotRedManager.Application.Tenancy;

/// <summary>
/// Gestion de "Datos Cache" del agente (capa 3): definicion de campos que el agente debe capturar
/// durante la conversacion y los valores percibidos por sesion. La sesion se identifica por SessionId
/// (AgentId en pruebas; ConversationId en chat real).
/// </summary>
public interface IAiAgentCacheService
{
    Task<IReadOnlyList<AiAgentCacheFieldDto>> ListFieldsAsync(Guid agentId, CancellationToken cancellationToken = default);
    Task<AiAgentCacheFieldDto?> CreateFieldAsync(CreateAgentCacheFieldRequest request, Guid actorUserId, CancellationToken cancellationToken = default);
    Task<AiAgentCacheFieldDto?> UpdateFieldAsync(Guid fieldId, UpdateAgentCacheFieldRequest request, Guid actorUserId, CancellationToken cancellationToken = default);
    Task<bool> DeleteFieldAsync(Guid fieldId, Guid actorUserId, CancellationToken cancellationToken = default);
    Task<int> BulkSetFieldsUpdatableAsync(Guid agentId, bool isUpdatable, Guid actorUserId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AiAgentCacheValueDto>> GetValuesAsync(Guid agentId, Guid sessionId, CancellationToken cancellationToken = default);
    Task<AiAgentCacheValueDto?> SetValueAsync(SetAgentCacheValueRequest request, CancellationToken cancellationToken = default);
    Task<int> ClearValuesAsync(Guid agentId, Guid sessionId, Guid actorUserId, CancellationToken cancellationToken = default);
}
