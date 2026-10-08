using CubotRedManager.Domain.Common;

namespace CubotRedManager.Domain.Entities;

/// <summary>
/// Entrada del historial de actividad de un lead. Tenant-scoped. Base para eventos de negocio
/// (lead.created, lead.stage.changed, lead.won, lead.lost, lead.archived, etc.).
/// </summary>
public class LeadActivity : TenantEntity
{
    public Guid LeadId { get; set; }
    public Lead? Lead { get; set; }
    public string ActivityType { get; set; } = null!;
    public string? Description { get; set; }
}
