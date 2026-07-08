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

    /// <summary>Exporta un agente completo (agente + prompts + recursos + campos cache) como JSON.
    /// Los archivos binarios de recursos NO se incluyen (solo la ruta local original en el campo
    /// FileUrl). Devuelve null si el agente no existe o no hay tenant activo.</summary>
    Task<AgentExportResult?> ExportAsync(Guid agentId, CancellationToken cancellationToken = default);

    /// <summary>Importa un agente desde un JSON generado por <see cref="ExportAsync"/> en el tenant
    /// activo. Rechaza si ya existe un agente con el mismo nombre (case-insensitive).</summary>
    Task<AgentImportResult> ImportAsync(byte[] jsonBytes, Guid actorUserId, CancellationToken cancellationToken = default);
}

public sealed record AgentExportResult(string FileName, byte[] JsonBytes);
public sealed record AgentImportResult(bool Ok, Guid? AgentId, string? Error);
