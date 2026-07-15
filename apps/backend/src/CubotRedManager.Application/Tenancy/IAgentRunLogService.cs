using CubotRedManager.Domain.Enums;

namespace CubotRedManager.Application.Tenancy;

/// <summary>Fila de la lista de conversaciones que el agente ha atendido (panel izquierdo de la bitacora).</summary>
public sealed record AgentRunLogConversationDto(
    Guid ConversationId,
    string? ContactName,
    string ContactPhone,
    string? LineLabel,
    DateTimeOffset? LastActivityAt,
    int Events);

/// <summary>Entrada cronologica de la bitacora para una conversacion (panel derecho).</summary>
public sealed record AgentRunLogEntryDto(
    DateTimeOffset OccurredAt,
    AiAgentRunLogKind Kind,
    string Title,
    string? Content,
    string? Response);

/// <summary>Valor capturado en el cache del agente para una conversacion.</summary>
public sealed record AgentCacheItemDto(string FieldKey, string Label, string? Value, string? Source);

/// <summary>Mensaje del chat con el cliente (inbound o outbound).</summary>
public sealed record AgentConversationMessageDto(
    DateTimeOffset SentAt,
    string Direction,        // "inbound" | "outbound"
    string Body,
    string? SentByName);

/// <summary>
/// Lectura de la bitacora del agente IA: lista de conversaciones atendidas (con conteo de
/// eventos y ultima actividad) y traza cronologica detallada por conversacion (mensaje
/// recibido, prompts enviados al LLM, herramientas disparadas, respuesta enviada, errores).
/// </summary>
public interface IAgentRunLogService
{
    Task<IReadOnlyList<AgentRunLogConversationDto>> ListConversationsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AgentRunLogEntryDto>> GetConversationLogAsync(Guid conversationId, CancellationToken cancellationToken = default);
    /// <summary>
    /// Borra TODA la historia de la bitacora del tenant actual (todas las conversaciones) Y el
    /// cache de datos capturados por los agentes del tenant. Asi "Limpiar historia" deja al agente
    /// realmente en cero: sin esto, el cache quedaba huerfano e inaccesible (ya no se podia
    /// seleccionar la conversacion para reiniciarla). NO toca mensajes del chat ni leads.
    /// Devuelve cuantas filas se eliminaron de bitacora (Logs) y de cache (Cache).
    /// </summary>
    Task<(int Logs, int Cache)> ClearAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Devuelve el cache del agente para una conversacion (sessionId = conversationId).
    /// Lista todos los campos definidos del agente con su valor actual (o null si no se capturo).
    /// </summary>
    Task<IReadOnlyList<AgentCacheItemDto>> GetConversationCacheAsync(Guid conversationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Devuelve el historial completo de mensajes de la conversacion (inbound y outbound), en orden
    /// cronologico. Util para revisar como fluyo el chat con el cliente sin salir de la bitacora.
    /// </summary>
    Task<IReadOnlyList<AgentConversationMessageDto>> GetConversationMessagesAsync(Guid conversationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Borra la conversacion para que el agente la "olvide": elimina los logs de bitacora, el cache
    /// asociado (sessionId = conversationId) y los mensajes del chat con el cliente. Resetea
    /// Conversation.LastMessageAt para que la bandeja no muestre fecha vieja. NO toca el lead ni
    /// la conversacion en si — solo limpia su historial para que el dispatcher arme un contexto
    /// vacio en el siguiente inbound. Devuelve (logs, cache, mensajes) borrados.
    /// </summary>
    Task<(int Logs, int Cache, int Messages)> ResetConversationMemoryAsync(Guid conversationId, CancellationToken cancellationToken = default);
}
