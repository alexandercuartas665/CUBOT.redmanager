using CubotRedManager.Domain.Enums;

namespace CubotRedManager.Application.Tenancy;

public sealed record TenantUserDto(
    Guid Id,
    Guid PlatformUserId,
    string Email,
    TenantRole TenantRole,
    PlatformUserStatus Status);
