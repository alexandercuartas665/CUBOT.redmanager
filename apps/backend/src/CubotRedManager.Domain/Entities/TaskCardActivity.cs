using CubotRedManager.Domain.Common;
using CubotRedManager.Domain.Enums;

namespace CubotRedManager.Domain.Entities;

/// <summary>
/// Entrada en el log de actividad de una tarjeta. Puede ser un comentario escrito por un usuario
/// o un evento automatico (movio, marco checklist, asigno miembro, etc.). TENANT-SCOPED.
/// </summary>
public class TaskCardActivity : TenantEntity
{
    public Guid TaskCardId { get; set; }
    public TaskCard? TaskCard { get; set; }

    public TaskActivityType Type { get; set; }

    /// <summary>PlatformUser que origino la actividad (operador o tenant user).</summary>
    public Guid? ActorUserId { get; set; }

    /// <summary>Nombre legible del autor en el momento (capturado por si despues cambia/sale).</summary>
    public string ActorName { get; set; } = null!;

    /// <summary>Texto: el comentario, o la descripcion legible del action (ej. "movio la tarjeta a En Progreso").</summary>
    public string Text { get; set; } = null!;
}
