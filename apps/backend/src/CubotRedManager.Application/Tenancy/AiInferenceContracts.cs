using CubotRedManager.Domain.Enums;

namespace CubotRedManager.Application.Tenancy;

/// <summary>Un turno de la conversacion de prueba. Role: "user" (cliente) o "model" (agente).</summary>
public sealed record AiChatTurn(string Role, string Text, IReadOnlyList<AiChatAttachment>? Attachments = null);

/// <summary>Recurso que el agente decidio entregar en el chat (imagen, video, pdf, ubicacion o texto).</summary>
public sealed record AiChatAttachment(string Name, AgentResourceType ResourceType, string? FileUrl, string? FileName, string? Detail);

/// <summary>Entrada del log de depuracion de prompts (una por cada llamada al proveedor de IA).</summary>
public sealed record AiDebugPrompt(string Title, DateTimeOffset SentAt, string Content, string? Response = null);

/// <summary>Resultado de una llamada de inferencia, con el consumo de tokens y los recursos a adjuntar.</summary>
public sealed record AiChatResult(bool Ok, string? Text, string? Error, int InputTokens = 0, int OutputTokens = 0,
    IReadOnlyList<AiChatAttachment>? Attachments = null, IReadOnlyList<AiDebugPrompt>? DebugPrompts = null);

/// <summary>
/// Cliente HTTP que habla con cada proveedor de IA (Gemini, OpenAI/ChatGPT, DeepSeek, Claude).
/// Recibe la API key ya descifrada; no persiste ni loggea secretos.
/// </summary>
public interface IAiProviderClient
{
    Task<AiChatResult> CompleteAsync(
        AiProvider provider,
        string apiKey,
        string? baseUrl,
        string model,
        string systemPrompt,
        IReadOnlyList<AiChatTurn> turns,
        CancellationToken cancellationToken = default);
}

/// <summary>Inferencia de agentes de la agencia: arma el prompt con la config del agente y llama al proveedor.</summary>
public interface IAiInferenceService
{
    Task<AiChatResult> TestChatAsync(Guid agentId, IReadOnlyList<AiChatTurn> turns, string? systemPromptOverride = null, CancellationToken cancellationToken = default);
}
