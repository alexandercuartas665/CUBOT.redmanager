using CubotRedManager.Domain.Common;

namespace CubotRedManager.Domain.Entities;

/// <summary>
/// Mapea un campo cache del agente a un campo del Lead del pipeline. Se conserva en redmanager
/// por paridad de esquema aunque Lead+Pipeline aun no se portaron. Portado desde CUBOT.travels.
/// </summary>
public class AiAgentCacheLeadMapping : TenantEntity
{
    public Guid AgentId { get; set; }
    public AiAgent? Agent { get; set; }

    /// <summary>FieldKey del AiAgentCacheField del agente.</summary>
    public string CacheFieldKey { get; set; } = null!;

    /// <summary>Selector destino. "core:ContactName"/"core:ContactPhone"/... o "field:{FieldKey}".</summary>
    public string TargetSelector { get; set; } = null!;
}
