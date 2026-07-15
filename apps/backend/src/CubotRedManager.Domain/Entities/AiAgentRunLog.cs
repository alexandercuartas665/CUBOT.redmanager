using CubotRedManager.Domain.Common;
using CubotRedManager.Domain.Enums;

namespace CubotRedManager.Domain.Entities;

/// <summary>
/// Bitacora de atencion del agente de IA (capa 3). Entidad TENANT-SCOPED. Persiste, por
/// conversacion, el rastro del proceso: mensajes recibidos, prompts enviados a la IA,
/// herramientas ejecutadas (con argumentos y resultado) y respuestas enviadas. Equivale al
/// panel "PROMPTS enviados a la IA" del chat de prueba, pero guardado para revisar la atencion
/// real de cada cliente. Portado 1:1 desde CUBOT.travels.
/// </summary>
public class AiAgentRunLog : TenantEntity
{
    public Guid ConversationId { get; set; }
    public Guid AgentId { get; set; }

    public DateTimeOffset OccurredAt { get; set; }
    public AiAgentRunLogKind Kind { get; set; }

    /// <summary>Titulo corto del evento (ej. "Prompt principal", "Lead creado").</summary>
    public string Title { get; set; } = null!;

    /// <summary>Contenido principal (prompt enviado, texto recibido, etc.).</summary>
    public string? Content { get; set; }

    /// <summary>Respuesta asociada (texto del LLM, resultado JSON de la herramienta).</summary>
    public string? Response { get; set; }
}
