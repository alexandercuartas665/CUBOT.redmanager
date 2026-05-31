namespace CubotRedManager.Domain.Common;

/// <summary>
/// Raiz de todas las entidades. Id Guid v7 (ordenable por tiempo) y campos de auditoria.
/// </summary>
public abstract class BaseEntity
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
}
