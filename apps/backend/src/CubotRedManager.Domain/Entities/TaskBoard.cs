using CubotRedManager.Domain.Common;

namespace CubotRedManager.Domain.Entities;

/// <summary>Tablero Kanban del equipo (Modulo 2.7). Por cliente o por campania transversal.</summary>
public class TaskBoard : TenantEntity
{
    public string Name { get; set; } = null!;
    public Guid? ClientId { get; set; }
    public string? CampaignName { get; set; }
    public string? Description { get; set; }

    public ICollection<TaskColumn> Columns { get; set; } = new List<TaskColumn>();
}
