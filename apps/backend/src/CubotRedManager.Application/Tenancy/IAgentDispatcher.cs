namespace CubotRedManager.Application.Tenancy;

/// <summary>
/// Despachador del agente IA. Portado 1:1 desde CUBOT.travels con adaptaciones para redmanager
/// (sin Lead+Pipeline; el candado del asesor se salta, la creacion de lead no crea nada).
/// </summary>
public interface IAgentDispatcher
{
    Task DispatchAsync(Guid tenantId, Guid conversationId, Guid? whatsAppLineId, string inboundBody, CancellationToken cancellationToken = default);
    Task<AgentDispatchResult> DispatchForTestAsync(Guid tenantId, Guid conversationId, Guid? whatsAppLineId, string inboundBody, CancellationToken cancellationToken = default);
}

public sealed record AgentDispatchResult(
    bool Ok,
    string? SkipReason,
    string? ReplyText,
    string? RawLlmResponse,
    bool MarkersLeaked,
    IReadOnlyList<Guid> LeadsCreated,
    IReadOnlyList<string> LeadStages,
    int AttachmentCount,
    int ContextTurns,
    int InputTokens,
    int OutputTokens,
    bool Simulated = true);
