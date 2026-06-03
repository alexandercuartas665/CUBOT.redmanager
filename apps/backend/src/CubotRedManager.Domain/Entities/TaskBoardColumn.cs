using CubotRedManager.Domain.Common;

namespace CubotRedManager.Domain.Entities;

/// <summary>
/// Columna (etapa) de un tablero Kanban. Ej.: "Por Hacer", "En Progreso", "Revision", "Completado".
/// El tenant las configura por tablero. Entidad TENANT-SCOPED.
/// </summary>
public class TaskBoardColumn : TenantEntity
{
    public Guid BoardId { get; set; }
    public TaskBoard? Board { get; set; }

    /// <summary>Nombre visible de la columna.</summary>
    public string Name { get; set; } = null!;

    /// <summary>Color asociado a la columna (hex o token). Solo visual.</summary>
    public string? Color { get; set; }

    /// <summary>Orden horizontal en el tablero (0 = mas a la izquierda).</summary>
    public int SortOrder { get; set; }

    /// <summary>
    /// Marca la columna como "estado final" (ej. Completado). Sirve para calcular tasa de
    /// finalizacion y para que las tarjetas dejen de generar atrasos al llegar aqui.
    /// </summary>
    public bool IsDone { get; set; }
}
