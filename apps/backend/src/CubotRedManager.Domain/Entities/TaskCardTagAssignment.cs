using CubotRedManager.Domain.Common;

namespace CubotRedManager.Domain.Entities;

/// <summary>Junction tarjeta + etiqueta (M-N). TENANT-SCOPED.</summary>
public class TaskCardTagAssignment : TenantEntity
{
    public Guid TaskCardId { get; set; }
    public TaskCard? TaskCard { get; set; }

    public Guid TagId { get; set; }
    public TaskCardTag? Tag { get; set; }
}
