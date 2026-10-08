using CubotRedManager.Domain.Common;

namespace CubotRedManager.Domain.Entities;

/// <summary>
/// Contenedor de un embudo comercial dentro de un tenant. Permite varios embudos paralelos
/// (ej. "Productos" y "Negocios"): cada agente IA o asesor puede trabajar leads en el embudo
/// que corresponda. Diferencia clave con CUBOT.travels (que solo tiene UN embudo por tenant).
///
/// Un Lead vive siempre en un unico Pipeline. Las PipelineStage cuelgan de Pipeline, no del
/// tenant directo. No se permite mover un Lead entre pipelines (reasignar es rehacer el lead).
/// </summary>
public class Pipeline : TenantEntity
{
    public string Name { get; set; } = null!;

    /// <summary>Orden de presentacion en el selector de pipelines (menor primero).</summary>
    public int SortOrder { get; set; }

    /// <summary>Pipeline creado por el sistema en el seed del tenant (Productos / Negocios).
    /// Los pipelines del sistema no se pueden eliminar — solo renombrar o archivar stages.</summary>
    public bool IsSystem { get; set; }

    public ICollection<PipelineStage> Stages { get; set; } = new List<PipelineStage>();
}
