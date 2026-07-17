namespace CubotRedManager.Application.Tenancy;

/// <summary>
/// Notifica en tiempo real que se agrego un mensaje a una conversacion o que el tablero cambio.
/// La implementacion real va por SignalR; el NoOp sirve para hosts sin SignalR. Portado desde
/// CUBOT.travels.
/// </summary>
public interface IChatBroadcaster
{
    Task MessageAddedAsync(Guid tenantId, Guid conversationId, MessageDto message, CancellationToken cancellationToken = default);
    Task BoardChangedAsync(Guid tenantId, CancellationToken cancellationToken = default);
}

/// <summary>Implementacion por defecto (no hace nada). Redmanager la usa hasta que se porte SignalR.</summary>
public sealed class NoOpChatBroadcaster : IChatBroadcaster
{
    public Task MessageAddedAsync(Guid tenantId, Guid conversationId, MessageDto message, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
    public Task BoardChangedAsync(Guid tenantId, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
