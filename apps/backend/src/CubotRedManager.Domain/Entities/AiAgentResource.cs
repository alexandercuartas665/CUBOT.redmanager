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

    /// <summary>Ruta relativa que sirve el archivo. Cuando FileContent no es null, apunta a
    /// /api/agent-resources/{id}/file (contenido servido desde la BD). Null si el recurso no
    /// tiene archivo (Text/Location).</summary>
    public string? FileUrl { get; set; }

    public string? FileName { get; set; }

    /// <summary>Contenido binario del archivo. Se guarda en la BD para que sobreviva a restarts
    /// del host (filesystem efimero en PaaS como Railway). Null si el recurso no tiene archivo.</summary>
    public byte[]? FileContent { get; set; }

    /// <summary>MIME del archivo (image/png, video/mp4, application/pdf, etc). Null si no hay.</summary>
    public string? FileMimeType { get; set; }

    public int SortOrder { get; set; }
}
