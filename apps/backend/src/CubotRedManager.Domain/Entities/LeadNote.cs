using CubotRedManager.Domain.Common;

namespace CubotRedManager.Domain.Entities;

/// <summary>Nota de seguimiento sobre un lead (observaciones del asesor). Tenant-scoped.
/// Autor y fecha provienen de BaseEntity (CreatedBy / CreatedAt).</summary>
public class LeadNote : TenantEntity
{
    public Guid LeadId { get; set; }
    public Lead? Lead { get; set; }

    public string Content { get; set; } = null!;

    /// <summary>Color de la nota (yellow, green, blue, pink, purple, gray).</summary>
    public string Color { get; set; } = "yellow";
}
