namespace CubotRedManager.Domain.Common;

/// <summary>
/// Base de las entidades tenant-scoped (agencia). El DbContext aplica HasQueryFilter
/// sobre TenantId para garantizar aislamiento entre agencias.
/// </summary>
public abstract class TenantEntity : BaseEntity, ITenantScoped
{
    public Guid TenantId { get; set; }
}
