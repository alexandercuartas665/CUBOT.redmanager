using CubotRedManager.Application.Tenancy;
using Microsoft.AspNetCore.SignalR;

namespace CubotRedManager.Web.Hubs;

/// <summary>
/// Publica eventos al hub /hubs/chat para los clientes conectados del tenant.
/// Reemplaza al NoOpChatBroadcaster cuando el host tiene SignalR activo (Web project).
/// </summary>
public sealed class SignalRChatBroadcaster : IChatBroadcaster
{
    private readonly IHubContext<ChatHub> _hub;

    public SignalRChatBroadcaster(IHubContext<ChatHub> hub)
    {
        _hub = hub;
    }

    public Task MessageAddedAsync(Guid tenantId, Guid conversationId, MessageDto message, CancellationToken cancellationToken = default)
    {
        if (tenantId == Guid.Empty) { return Task.CompletedTask; }
        return _hub.Clients.Group(ChatHub.GroupName(tenantId))
            .SendAsync("MessageAdded", message, cancellationToken);
    }

    public Task BoardChangedAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        if (tenantId == Guid.Empty) { return Task.CompletedTask; }
        return _hub.Clients.Group(ChatHub.GroupName(tenantId))
            .SendAsync("BoardChanged", cancellationToken);
    }
}
