namespace CubotRedManager.Application.Tenancy;

/// <summary>Gestion de agentes de IA de la agencia (capa 3): proveedor, prompt, encendido y recursos.</summary>
public interface IAiAgentService
{
    Task<IReadOnlyList<AiAgentDto>> ListAsync(CancellationToken cancellationToken = default);
    Task<AiAgentDetailDto?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task<AiAgentDto?> CreateAsync(CreateAiAgentRequest request, Guid actorUserId, CancellationToken cancellationToken = default);
    Task<AiAgentDto?> UpdateAsync(Guid id, UpdateAiAgentRequest request, Guid actorUserId, CancellationToken cancellationToken = default);

    /// <summary>Enciende (produccion) o apaga el agente.</summary>
    Task<AiAgentDto?> SetActiveAsync(Guid id, bool active, Guid actorUserId, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(Guid id, Guid actorUserId, CancellationToken cancellationToken = default);

    Task<AiAgentResourceDto?> AddResourceAsync(CreateAgentResourceRequest request, Guid actorUserId, CancellationToken cancellationToken = default);
    Task<AiAgentResourceDto?> UpdateResourceAsync(Guid id, UpdateAgentResourceRequest request, Guid actorUserId, CancellationToken cancellationToken = default);
    Task<bool> DeleteResourceAsync(Guid id, Guid actorUserId, CancellationToken cancellationToken = default);

    Task<AiAgentPromptDto?> AddPromptAsync(CreateAgentPromptRequest request, Guid actorUserId, CancellationToken cancellationToken = default);
    Task<AiAgentPromptDto?> UpdatePromptAsync(Guid id, UpdateAgentPromptRequest request, Guid actorUserId, CancellationToken cancellationToken = default);
    Task<bool> DeletePromptAsync(Guid id, Guid actorUserId, CancellationToken cancellationToken = default);
}
