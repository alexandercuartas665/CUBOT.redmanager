using CubotRedManager.Domain.Enums;

namespace CubotRedManager.Application.SuperAdminApi;

// DTOs de la Admin Agent API. camelCase (System.Text.Json default), enums viajan como NUMERO
// (int) porque el brief lo pide y evita romper a los clientes si agregamos valores nuevos al enum.

public sealed record AiAgentDto(
    Guid Id,
    string Name,
    string? Role,
    AiProvider Provider,
    string? Model,
    bool IsActive,
    int SortOrder,
    // Flags de "tools" (extensibles). Expuestos como bools para que el cliente los lea sin parsear.
    bool ReactionsEnabled,
    bool PaymentEnabled,
    bool EnableDataContainerMcp,
    IReadOnlyList<string> ToolKeys);

public sealed record AiAgentResourceDto(
    Guid Id,
    string Name,
    AgentResourceType ResourceType,
    string? Detail,
    string? FileUrl,
    string? FileName,
    string? FileMimeType,
    int SortOrder);

public sealed record AiAgentPromptDto(
    Guid Id,
    string Name,
    string? Rule,
    string Body,
    int SortOrder);

public sealed record AiAgentDetailDto(
    AiAgentDto Agent,
    string SystemPrompt,
    IReadOnlyList<AiAgentResourceDto> Resources,
    IReadOnlyList<AiAgentPromptDto> Prompts);

public sealed record CreateAiAgentRequest(
    string Name,
    string? Role,
    AiProvider Provider,
    string? Model,
    string? SystemPrompt,
    bool IsActive);

public sealed record UpdateAiAgentRequest(
    string Name,
    string? Role,
    AiProvider Provider,
    string? Model,
    string? SystemPrompt,
    bool IsActive);

/// <summary>Body del PUT tools. Solo se admiten keys del catalogo (ver AdminAgentTools).</summary>
public sealed record UpdateAgentToolsRequest(IReadOnlyList<string> ToolKeys);

public sealed record AgentRunLogConversationDto(
    Guid ConversationId,
    DateTimeOffset LastOccurredAt,
    int EntryCount);

public sealed record AgentRunLogEntryDto(
    Guid Id,
    DateTimeOffset OccurredAt,
    AiAgentRunLogKind Kind,
    string Title,
    string? Content,
    string? Response);

public sealed record AdminLineDto(
    Guid Id,
    string Label,               // WhatsAppLine.InstanceName
    WhatsAppProvider Provider,
    string? Phone,              // PhoneNumber
    WhatsAppLineStatus Status,  // enum como numero
    Guid? BoundAgentId);        // AiAgentLineBinding activo (IsConnected=true), si existe

public sealed record BindLineRequest(Guid WhatsAppLineId);

/// <summary>
/// Resultado de bind/unbind. `Ok=true` con `Error=null` en el happy path; `Ok=false` con
/// `Error="line_already_bound"` cuando otra agente ya la atiende (endpoint devuelve 409).
/// </summary>
public sealed record LineBindingResult(bool Ok, string? Error = null, Guid? CurrentAgentId = null);

/// <summary>
/// Catalogo cerrado de "tools" del agente en CUBOT.redmanager. Cada key mapea a un flag
/// del entity AiAgent. Si mas adelante hay tools extensibles (ej. plugins por-tenant), esto
/// se convierte en una tabla; por ahora es una constante para que la validacion sea trivial.
/// </summary>
public static class AdminAgentTools
{
    public const string Payment = "payment";              // AiAgent.PaymentEnabled
    public const string Reactions = "reactions";          // AiAgent.ReactionsEnabled
    public const string DataContainerMcp = "dataContainerMcp"; // AiAgent.EnableDataContainerMcp

    public static readonly IReadOnlyCollection<string> All = new[] { Payment, Reactions, DataContainerMcp };
}
