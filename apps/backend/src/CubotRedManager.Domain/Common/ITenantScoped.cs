namespace CubotRedManager.Domain.Common;

/// <summary>
/// Marca una entidad como perteneciente a un tenant (agencia). Todas las entidades
/// operativas la implementan para que el DbContext aplique el filtro global por tenant.
/// </summary>
public interface ITenantScoped
{
    Guid TenantId { get; set; }
}
