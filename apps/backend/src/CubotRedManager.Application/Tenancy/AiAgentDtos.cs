using CubotRedManager.Domain.Enums;

namespace CubotRedManager.Application.Tenancy;

public sealed record AiAgentDto(
    Guid Id,
    string Name,
    string? Role,
    AiProvider Provider,
    string? Model,
    string SystemPrompt,
    bool IsActive,
    bool EnableDataContainerMcp,
    int SortOrder,
    int ResourceCount);

public sealed record AiAgentResourceDto(
    Guid Id,
    Guid AgentId,
    string Name,
    AgentResourceType ResourceType,
    string? Detail,
    string? FileUrl,
    string? FileName,
    int SortOrder);

public sealed record AiAgentPromptDto(Guid Id, Guid AgentId, string Name, string? Rule, string Body, int SortOrder);

public sealed record AiAgentDetailDto(AiAgentDto Agent, IReadOnlyList<AiAgentResourceDto> Resources, IReadOnlyList<AiAgentPromptDto> Prompts);

public sealed record CreateAiAgentRequest(string Name, string? Role, AiProvider Provider, string? Model, string SystemPrompt, bool EnableDataContainerMcp = false);
public sealed record UpdateAiAgentRequest(string Name, string? Role, AiProvider Provider, string? Model, string SystemPrompt, bool EnableDataContainerMcp = false);

public sealed record CreateAgentResourceRequest(Guid AgentId, string Name, AgentResourceType ResourceType, string? Detail, string? FileUrl, string? FileName);
public sealed record UpdateAgentResourceRequest(string Name, AgentResourceType ResourceType, string? Detail, string? FileUrl, string? FileName);

public sealed record CreateAgentPromptRequest(Guid AgentId, string Name, string? Rule, string Body);
public sealed record UpdateAgentPromptRequest(string Name, string? Rule, string Body);

// --- Datos Cache del agente (capa 3) ---
public sealed record AiAgentCacheFieldDto(Guid Id, Guid AgentId, string FieldKey, string Label, string? Description, int SortOrder, bool IsUpdatable);
public sealed record CreateAgentCacheFieldRequest(Guid AgentId, string Label, string? Description, bool IsUpdatable = true);
public sealed record UpdateAgentCacheFieldRequest(string Label, string? Description, bool IsUpdatable = true);

public sealed record AiAgentCacheValueDto(string FieldKey, string Label, string? Description, string? Value, string? Source, DateTimeOffset? UpdatedAt);
public sealed record SetAgentCacheValueRequest(Guid AgentId, Guid SessionId, string FieldKey, string? Value, string? Source);
