using System.Security.Claims;
using CubotRedManager.Application.Abstractions;

namespace CubotRedManager.SuperAdmin.Auth;

/// <summary>
/// Resuelve tenant/usuario desde los claims de la cookie. En la consola Super Admin el operador
/// puede no tener tenant (TenantId null); los servicios globales (config de IA/Evolution) no lo
/// requieren. Implementa ITenantProvider (filtro del DbContext) e ITenantContext (servicios).
/// </summary>
public sealed class HttpTenantContext : ITenantProvider, ITenantContext
{
    private readonly IHttpContextAccessor _accessor;
    public HttpTenantContext(IHttpContextAccessor accessor) => _accessor = accessor;
    private ClaimsPrincipal? User => _accessor.HttpContext?.User;

    public Guid? CurrentTenantId => TenantId;
    public Guid? TenantId => Guid.TryParse(User?.FindFirst("tenant_id")?.Value, out var id) ? id : null;
    public Guid? UserId => Guid.TryParse(User?.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id) ? id : null;
}
