namespace CubotRedManager.Domain.Enums;

/// <summary>Tipo de entrada en el log de actividad de una tarjeta del tablero.</summary>
public enum TaskActivityType
{
    /// <summary>Cambio automatico del sistema (ej. movio la tarjeta, marco checklist, asigno miembro).</summary>
    Action = 0,
    /// <summary>Comentario escrito por un usuario.</summary>
    Comment
}
