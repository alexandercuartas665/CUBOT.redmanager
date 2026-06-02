namespace CubotRedManager.Application.Auth;

public sealed record LoginRequest(string Email, string Password, Guid? TenantId = null);

public sealed record TokenResponse(
    string AccessToken,
    DateTimeOffset ExpiresAt,
    Guid? TenantId,
    bool TenantSelectionRequired);

public sealed record SwitchTenantRequest(Guid TenantId);

public sealed record TenantSummary(Guid TenantId, string Name, string TenantRole);

public sealed record MeResponse(
    Guid UserId,
    string Email,
    string? DisplayName,
    string? PlatformRole,
    Guid? CurrentTenantId,
    IReadOnlyList<TenantSummary> Tenants);
