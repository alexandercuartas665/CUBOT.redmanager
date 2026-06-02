using CubotRedManager.Domain.Common;

namespace CubotRedManager.Domain.Entities;

/// <summary>
/// Plantilla de autorespuesta. Si el comentario contiene alguna de las Keywords (lowercase, sin
/// acentos, \bword\b), se responde con Body. Tenant-scoped indirectamente (via Config).
/// </summary>
public class AutoReplyTemplate : TenantEntity
{
    public Guid ConfigId { get; set; }
    public AutoReplyConfig? Config { get; set; }

    /// <summary>Palabras clave separadas por coma. Ej. "hermoso,bello,lindo".</summary>
    public string Keywords { get; set; } = "";

    /// <summary>Mensaje a publicar como respuesta cuando una keyword matchea.</summary>
    public string Body { get; set; } = "";

    /// <summary>Orden de evaluacion (la primera plantilla cuya keyword matchea gana).</summary>
    public int SortOrder { get; set; }
}
