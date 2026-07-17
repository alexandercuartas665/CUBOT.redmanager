using Microsoft.Extensions.Logging;

namespace CubotRedManager.Application.Tenancy;

/// <summary>
/// Cola de despacho del agente de IA. El webhook entrante NO ejecuta el dispatch inline: persiste
/// y encola. Un procesador en background aplica: respuesta rapida al webhook, serializacion por
/// conversacion y debounce de rafagas. Portado desde CUBOT.travels.
/// </summary>
public interface IAgentDispatchQueue
{
    void Enqueue(Guid tenantId, Guid conversationId, Guid? whatsAppLineId, string inboundBody);
}

/// <summary>Fallback para hosts que no levantan procesador en background.</summary>
public sealed class NoOpAgentDispatchQueue : IAgentDispatchQueue
{
    private readonly ILogger<NoOpAgentDispatchQueue> _logger;
    public NoOpAgentDispatchQueue(ILogger<NoOpAgentDispatchQueue> logger) => _logger = logger;

    public void Enqueue(Guid tenantId, Guid conversationId, Guid? whatsAppLineId, string inboundBody)
    {
        _logger.LogWarning(
            "NoOpAgentDispatchQueue: mensaje de conv {ConvId} no se despacho (host sin procesador del agente).",
            conversationId);
    }
}
