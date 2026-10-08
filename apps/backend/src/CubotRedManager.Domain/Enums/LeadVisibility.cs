namespace CubotRedManager.Domain.Enums;

/// <summary>Alcance de leads que puede ver un asesor dentro de su agencia.</summary>
public enum LeadVisibility
{
    /// <summary>Solo ve los leads que tiene asignados.</summary>
    OwnOnly,

    /// <summary>Ve todos los leads de la agencia.</summary>
    All
}
