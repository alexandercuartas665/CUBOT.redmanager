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
    int ResourceCount,
    bool ReactionsEnabled = false,
    int ReactionRatioN = 3,
    int ReactionRatioM = 4,
    string? ReactionEmojis = null);

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

public sealed record AiAgentDetailDto(AiAgentDto Agent, IReadOnlyList<AiAgentResourceDto> Resources, IReadOnlyList<AiAgentPromptDto> Prompts, AgentPaymentConfigDto? PaymentConfig = null);

public sealed record CreateAiAgentRequest(string Name, string? Role, AiProvider Provider, string? Model, string SystemPrompt, bool EnableDataContainerMcp = false, bool ReactionsEnabled = false, int ReactionRatioN = 3, int ReactionRatioM = 4, string? ReactionEmojis = null);
public sealed record UpdateAiAgentRequest(string Name, string? Role, AiProvider Provider, string? Model, string SystemPrompt, bool EnableDataContainerMcp = false, bool ReactionsEnabled = false, int ReactionRatioN = 3, int ReactionRatioM = 4, string? ReactionEmojis = null);

// --- Configuracion de pagos FUXION (capa 3.5 - integracion externa) ---
// El TokenPresent es bool porque JAMAS se expone el token descifrado al cliente. Para saber si
// hay token guardado la UI mira este flag. Para renovarlo, el operador escribe uno nuevo que
// se cifra al guardarlo (SetAgentPaymentConfigRequest.NewToken).
public sealed record AgentPaymentConfigDto(
    bool Enabled,
    string? UserId,
    string? Country,
    bool TokenPresent,
    DateTimeOffset? TokenExpiresAt,
    DateTimeOffset? TokenLastVerifiedAt,
    string? CatalogContainerName,
    string? CatalogNameColumn,
    string? CatalogProductIdColumn,
    string? CatalogCountryColumn,
    string? ApiBaseUrl,
    string? ApiPathTemplate,
    string? ResponseUrlPath,
    DateTimeOffset? LastPriceSyncAt = null);

// NewToken:
//   null -> no tocar el token guardado (usuario solo edita otros campos).
//   ""   -> BORRAR el token guardado (usuario limpia la config).
//   otro -> reemplazar con este nuevo token; se cifra al guardar y se parsea su exp.
public sealed record SetAgentPaymentConfigRequest(
    bool Enabled,
    string? UserId,
    string? Country,
    string? NewToken,
    string? CatalogContainerName,
    string? CatalogNameColumn,
    string? CatalogProductIdColumn,
    string? CatalogCountryColumn,
    string? ApiBaseUrl,
    string? ApiPathTemplate,
    string? ResponseUrlPath);

public sealed record CreateAgentResourceRequest(Guid AgentId, string Name, AgentResourceType ResourceType, string? Detail, string? FileUrl, string? FileName, byte[]? FileContent = null, string? FileMimeType = null);
public sealed record UpdateAgentResourceRequest(string Name, AgentResourceType ResourceType, string? Detail, string? FileUrl, string? FileName, byte[]? FileContent = null, string? FileMimeType = null, bool ClearFile = false);

public sealed record CreateAgentPromptRequest(Guid AgentId, string Name, string? Rule, string Body);
public sealed record UpdateAgentPromptRequest(string Name, string? Rule, string Body);

// --- Datos Cache del agente (capa 3) ---
public sealed record AiAgentCacheFieldDto(Guid Id, Guid AgentId, string FieldKey, string Label, string? Description, int SortOrder, bool IsUpdatable);
public sealed record CreateAgentCacheFieldRequest(Guid AgentId, string Label, string? Description, bool IsUpdatable = true);
public sealed record UpdateAgentCacheFieldRequest(string Label, string? Description, bool IsUpdatable = true);

public sealed record AiAgentCacheValueDto(string FieldKey, string Label, string? Description, string? Value, string? Source, DateTimeOffset? UpdatedAt);
public sealed record SetAgentCacheValueRequest(Guid AgentId, Guid SessionId, string FieldKey, string? Value, string? Source);
