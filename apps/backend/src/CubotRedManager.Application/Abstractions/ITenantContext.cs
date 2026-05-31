namespace CubotRedManager.Application.Abstractions;

/// <summary>
/// Expone el tenant (agencia) y usuario del contexto de ejecucion actual. Lo resuelve la capa de
/// presentacion desde los claims (tenant_id). En procesos sin tenant, TenantId puede ser null.
/// </summary>
public interface ITenantContext
{
    Guid? TenantId { get; }
    Guid? UserId { get; }
}
