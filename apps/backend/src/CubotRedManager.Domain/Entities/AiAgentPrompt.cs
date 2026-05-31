using CubotRedManager.Domain.Common;

namespace CubotRedManager.Domain.Entities;

/// <summary>
/// Prompt enrutado de un agente de IA (capa 3). Entidad TENANT-SCOPED. Un agente tiene un prompt
/// base (AiAgent.SystemPrompt) y opcionalmente varios prompts con nombre + regla de uso; el enrutador
/// aplica el prompt cuyo criterio coincide con el mensaje (ej. "cuando pregunte por precios").
/// </summary>
public class AiAgentPrompt : TenantEntity
{
    public Guid AgentId { get; set; }
    public AiAgent? Agent { get; set; }

    public string Name { get; set; } = null!;

    /// <summary>Regla/criterio en lenguaje natural que indica cuando usar este prompt.</summary>
    public string? Rule { get; set; }

    public string Body { get; set; } = "";

    public int SortOrder { get; set; }
}
