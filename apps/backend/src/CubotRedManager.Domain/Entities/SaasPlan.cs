using CubotRedManager.Domain.Common;

namespace CubotRedManager.Domain.Entities;

/// <summary>Plan comercial del SaaS. Entidad global administrada por el Super Admin.</summary>
public class SaasPlan : BaseEntity
{
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public decimal? MonthlyPrice { get; set; }
    public decimal? YearlyPrice { get; set; }
    public string? Currency { get; set; }
    public bool IsActive { get; set; } = true;

    public ICollection<SaasPlanLimit> Limits { get; set; } = new List<SaasPlanLimit>();
}
