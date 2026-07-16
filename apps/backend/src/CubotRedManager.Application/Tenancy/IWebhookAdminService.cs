namespace CubotRedManager.Application.Tenancy;

public sealed record WebhookConfigDto(
    string Mode,
    string? PublicUrl,
    string? Token,
    string? ActiveUrl,
    bool TunnelRunning,
    string? EffectiveUrl);

/// <summary>Administra la config del webhook entrante (modo dev/prod, URL, token) y el tunel de desarrollo.</summary>
public interface IWebhookAdminService
{
    Task<WebhookConfigDto> GetAsync(CancellationToken cancellationToken = default);
    Task<WebhookConfigDto> SaveAsync(string mode, string? publicUrl, string? token, Guid actorUserId, CancellationToken cancellationToken = default);

    /// <summary>Modo desarrollo: levanta el tunel, guarda su URL y reaplica el webhook a las lineas conectadas.</summary>
    Task<WebhookConfigDto> StartTunnelAsync(Guid actorUserId, CancellationToken cancellationToken = default);
    Task<WebhookConfigDto> StopTunnelAsync(Guid actorUserId, CancellationToken cancellationToken = default);
}

/// <summary>Tunel de desarrollo (cloudflared). Implementacion real en la app host.</summary>
public interface IDevTunnel
{
    bool IsRunning { get; }
    string? PublicUrl { get; }
    Task<string?> StartAsync(int port, CancellationToken cancellationToken = default);
    void Stop();
}

/// <summary>Sin tunel (procesos sin soporte de cloudflared).</summary>
public sealed class NoOpDevTunnel : IDevTunnel
{
    public bool IsRunning => false;
    public string? PublicUrl => null;
    public Task<string?> StartAsync(int port, CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
    public void Stop() { }
}
