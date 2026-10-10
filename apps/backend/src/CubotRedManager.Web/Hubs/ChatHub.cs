using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace CubotRedManager.Web.Hubs;

/// <summary>
/// Hub SignalR del chat del lead en Pipelines. Clientes se unen al grupo del tenant al cual
/// pertenecen (via JoinTenant). El broadcaster publica a ese grupo cuando se agrega un mensaje.
/// Portado reducido desde CUBOT.travels.
/// </summary>
[Authorize(Policy = Authorization.AppPolicies.TenantMember)]
public sealed class ChatHub : Hub
{
    public async Task JoinTenant(Guid tenantId)
    {
        if (tenantId == Guid.Empty) { return; }
        await Groups.AddToGroupAsync(Context.ConnectionId, GroupName(tenantId));
    }

    public async Task LeaveTenant(Guid tenantId)
    {
        if (tenantId == Guid.Empty) { return; }
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupName(tenantId));
    }

    public static string GroupName(Guid tenantId) => $"tenant:{tenantId:D}";
}
