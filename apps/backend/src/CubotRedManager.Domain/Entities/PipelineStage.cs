using CubotRedManager.Domain.Common;

namespace CubotRedManager.Domain.Entities;

/// <summary>
/// Etapa configurable dentro de un Pipeline. IsClosedWon/IsClosedLost marcan etapas terminales
/// que cierran el lead (al moverlo alli, Lead.Status pasa a Won/Lost y StageChangedAt se setea).
///
/// Diferencia vs CUBOT.travels: aqui hay FK obligatoria a Pipeline (travels tiene uno solo por
/// tenant, por eso omite la FK). Se descarto `GeneratesItinerary` (travel-specific).
/// </summary>
public class PipelineStage : TenantEntity
{
    public Guid PipelineId { get; set; }
    public Pipeline? Pipeline { get; set; }

    public string Name { get; set; } = null!;
    public int SortOrder { get; set; }
    public bool IsClosedWon { get; set; }
    public bool IsClosedLost { get; set; }
}
