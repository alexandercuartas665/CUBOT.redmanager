using CubotRedManager.Domain.Enums;

namespace CubotRedManager.Application.Tenancy;

public sealed record WhatsAppLineDto(
    Guid Id,
    string InstanceName,
    string? PhoneNumber,
    WhatsAppLineStatus Status,
    Guid? AssignedToTenantUserId,
    DateTimeOffset? LastConnectedAt,
    DateTimeOffset? LastStatusAt);

public sealed record CreateWhatsAppLineRequest(string InstanceName, string? PhoneNumber = null);
