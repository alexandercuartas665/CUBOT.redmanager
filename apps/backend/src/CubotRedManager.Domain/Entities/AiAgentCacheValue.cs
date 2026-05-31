using CubotRedManager.Domain.Common;

namespace CubotRedManager.Domain.Entities;

/// <summary>
/// Valor capturado para un campo de cache durante una sesion de conversacion (capa 3, datos cache).
/// Entidad TENANT-SCOPED. La sesion se identifica por SessionId (AgentId en pruebas; ConversationId
/// en chat real).
/// </summary>
public class AiAgentCacheValue : TenantEntity
{
    public Guid AgentId { get; set; }
    public AiAgent? Agent { get; set; }

    /// <summary>Identificador de la sesion (AgentId para pruebas, ConversationId en chat real).</summary>
    public Guid SessionId { get; set; }

    /// <summary>Clave del dato, coincide con AiAgentCacheField.FieldKey.</summary>
    public string FieldKey { get; set; } = null!;

    /// <summary>Valor capturado por el motor de inferencia.</summary>
    public string? Value { get; set; }

    /// <summary>Fuente del dato (ej. "manual", "inference", "system"). Util para auditoria.</summary>
    public string? Source { get; set; }
}
