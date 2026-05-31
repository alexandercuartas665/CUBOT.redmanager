using System.Security.Claims;
using CubotRedManager.Application.Abstractions;

namespace CubotRedManager.Web.Auth;

/// <summary>
/// Resuelve la agencia (tenant) y el usuario activos desde los claims de la cookie. Implementa
/// ITenantProvider (consumido por el DbContext para el filtro global) e ITenantContext (consumido
/// por los servicios de Application).
/// </summary>
public sealed class HttpTenantContext : ITenantProvider, ITenantContext
{
    private readonly IHttpContextAccessor _accessor;

    public HttpTenantContext(IHttpContextAccessor accessor) => _accessor = accessor;

    private ClaimsPrincipal? User => _accessor.HttpContext?.User;

    public Guid? CurrentTenantId => TenantId;

    public Guid? TenantId =>
        Guid.TryParse(User?.FindFirst("tenant_id")?.Value, out var id) ? id : null;

    public Guid? UserId =>
        Guid.TryParse(User?.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id) ? id : null;
}
