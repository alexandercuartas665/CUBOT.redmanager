using CubotRedManager.Domain.Common;

namespace CubotRedManager.Domain.Entities;

/// <summary>
/// Plantilla de mensaje reutilizable (copy de publicacion, DM, comentario). Tenant-scoped.
/// Permite al operador tener un banco de mensajes para reutilizar en publicaciones, respuestas
/// de bandeja y autorespuesta. Las plantillas de autorespuesta especificas viven en
/// AutoReplyTemplate (atadas a una cuenta); estas son globales del tenant.
/// </summary>
public class MessageTemplate : TenantEntity
{
    public string Name { get; set; } = "";
    public string Body { get; set; } = "";

    /// <summary>Etiquetas separadas por coma para busqueda y agrupacion.</summary>
    public string? Tags { get; set; }

    public Guid? AuthorTenantUserId { get; set; }
}
