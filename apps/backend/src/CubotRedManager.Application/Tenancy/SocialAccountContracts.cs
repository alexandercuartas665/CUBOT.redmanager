using CubotRedManager.Domain.Enums;

namespace CubotRedManager.Application.Tenancy;

public sealed record SocialNetworkDto(string Code, string DisplayName, string ColorHex, bool IsEnabled);

/// <summary>Vista cruzada de una cuenta social conectada (Modulo 2.3).</summary>
public sealed record SocialAccountDto(
    Guid Id,
    Guid ClientId,
    string ClientName,
    string NetworkCode,
    string NetworkDisplay,
    string ColorHex,
    string? Handle,
    string? DisplayName,
    SocialAccountStatus Status,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset? LastSyncAt,
    long? FollowersCount = null,
    string? AvatarUrl = null,
    string? Bio = null,
    string? LastSyncError = null,
    int RefreshFailureCount = 0)
{
    /// <summary>Token por expirar (menos de 7 dias) y cuenta conectada.</summary>
    public bool ExpiringSoon => Status == SocialAccountStatus.Connected && ExpiresAt is { } e && e <= DateTimeOffset.UtcNow.AddDays(7);

    /// <summary>Cuenta marcada como Connected pero con error de refresh pendiente. El operador ve
    /// un badge "Con problema" para no confiar ciegamente en el estado.</summary>
    public bool HasRefreshProblem => Status == SocialAccountStatus.Connected && !string.IsNullOrWhiteSpace(LastSyncError);
}

public sealed record ConnectSocialAccountRequest(Guid ClientId, string NetworkCode, string? Handle);

/// <summary>
/// Vista de cuentas sociales conectadas por cliente y red (Modulo 2.3). La conexion real via OAuth
/// (Modulo 2.2) se integra despues; por ahora se permite registrar una cuenta de forma manual (demo).
/// </summary>
public interface ISocialAccountService
{
    Task<IReadOnlyList<SocialNetworkDto>> ListNetworksAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SocialAccountDto>> ListAsync(Guid? clientId = null, CancellationToken cancellationToken = default);
    /// <summary>Trae una cuenta por su Id (tenant-scoped). Null si no existe o no pertenece al tenant activo.</summary>
    Task<SocialAccountDto?> GetAsync(Guid id, CancellationToken cancellationToken = default);
    Task<SocialAccountDto?> ConnectDemoAsync(ConnectSocialAccountRequest request, Guid actorUserId, CancellationToken cancellationToken = default);
    Task<SocialAccountDto?> ChangeStatusAsync(Guid id, SocialAccountStatus status, Guid actorUserId, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(Guid id, Guid actorUserId, CancellationToken cancellationToken = default);
}
