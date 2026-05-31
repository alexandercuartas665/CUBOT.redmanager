using CubotRedManager.Domain.Common;
using CubotRedManager.Domain.Enums;

namespace CubotRedManager.Domain.Entities;

/// <summary>
/// Recurso que un agente de IA puede usar para entregar informacion (modulo capa 3).
/// Entidad TENANT-SCOPED. Puede ser imagen/video/audio/pdf/ubicacion/texto, con un detalle y
/// opcionalmente un archivo cargado.
/// </summary>
public class AiAgentResource : TenantEntity
{
    public Guid AgentId { get; set; }
    public AiAgent? Agent { get; set; }

    public string Name { get; set; } = null!;
    public AgentResourceType ResourceType { get; set; } = AgentResourceType.Text;

    /// <summary>Detalle/descripcion del recurso (o el texto si el tipo es Text; o "lat,lng" si Location).</summary>
    public string? Detail { get; set; }

    /// <summary>Ruta local servible del archivo (/uploads/agents/...). Null para texto/ubicacion.</summary>
    public string? FileUrl { get; set; }

    public string? FileName { get; set; }

    public int SortOrder { get; set; }
}
