namespace CubotRedManager.Application.Abstractions;

/// <summary>
/// Permite establecer un tenant y usuario "ambient" para un IServiceScope cuando NO hay HTTP
/// context (workers, jobs, cron). Si esta seteado, los implementadores de ITenantProvider y
/// ITenantContext deben priorizarlo sobre los claims de la cookie.
/// Uso tipico:
///   using var scope = scopeFactory.CreateScope();
///   scope.ServiceProvider.GetRequiredService&lt;IAmbientTenantOverride&gt;().Set(tenantId, systemUserId);
///   var svc = scope.ServiceProvider.GetRequiredService&lt;ITikTokSyncService&gt;();
///   await svc.SyncAllAsync(...);
/// </summary>
public interface IAmbientTenantOverride
{
    Guid? TenantId { get; }
    Guid? UserId { get; }
    void Set(Guid? tenantId, Guid? userId);
}

/// <summary>Implementacion scoped (un override por scope; el HTTP no la usa).</summary>
public sealed class AmbientTenantOverride : IAmbientTenantOverride
{
    public Guid? TenantId { get; private set; }
    public Guid? UserId { get; private set; }
    public void Set(Guid? tenantId, Guid? userId) { TenantId = tenantId; UserId = userId; }
}
