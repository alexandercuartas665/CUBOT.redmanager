using CubotRedManager.Domain.Common;

namespace CubotRedManager.Domain.Entities;

/// <summary>
/// Tablero Kanban del tenant para gestionar tareas/proyectos. Cada agencia puede tener varios
/// tableros (ej. "Operacion", "Marketing", "Soporte"). Entidad TENANT-SCOPED.
/// </summary>
public class TaskBoard : TenantEntity
{
    /// <summary>Nombre visible del tablero (ej. "Operacion Q1").</summary>
    public string Name { get; set; } = null!;

    /// <summary>Descripcion opcional para que los miembros entiendan el proposito del tablero.</summary>
    public string? Description { get; set; }

    /// <summary>Color del tablero en la lista (hex). Solo visual.</summary>
    public string? Color { get; set; }

    /// <summary>Orden de visualizacion en la lista de tableros del tenant.</summary>
    public int SortOrder { get; set; }

    /// <summary>Tableros archivados quedan ocultos de la lista por defecto pero conservan sus datos.</summary>
    public bool IsArchived { get; set; }
}
