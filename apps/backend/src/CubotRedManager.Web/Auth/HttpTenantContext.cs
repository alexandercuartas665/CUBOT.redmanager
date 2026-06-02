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
    private readonly IAmbientTenantOverride _ambient;

    public HttpTenantContext(IHttpContextAccessor accessor, IAmbientTenantOverride ambient)
    {
        _accessor = accessor;
        _ambient = ambient;
    }

    private ClaimsPrincipal? User => _accessor.HttpContext?.User;

    public Guid? CurrentTenantId => TenantId;

    public Guid? TenantId =>
        _ambient.TenantId ?? (Guid.TryParse(User?.FindFirst("tenant_id")?.Value, out var id) ? id : null);

    public Guid? UserId =>
        _ambient.UserId ?? (Guid.TryParse(User?.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id) ? id : null);
}
