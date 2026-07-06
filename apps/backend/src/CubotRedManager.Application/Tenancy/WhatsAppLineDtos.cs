using CubotRedManager.Domain.Enums;

namespace CubotRedManager.Application.Tenancy;

public sealed record WhatsAppLineDto(
    Guid Id,
    string InstanceName,
    string? PhoneNumber,
    WhatsAppLineStatus Status,
    Guid? AssignedToTenantUserId,
    DateTimeOffset? LastConnectedAt,
    DateTimeOffset? LastStatusAt,
    WhatsAppProvider Provider,
    string? CloudPhoneNumberId,
    string? CloudBusinessAccountId,
    bool HasCloudAccessToken,
    bool HasCloudWebhookVerifyToken,
    // === YCloud (solo cuando Provider == YCloud) ===
    string? YCloudPhoneNumberId = null,
    string? YCloudWabaId = null,
    bool HasYCloudApiKey = false);

public sealed record CreateWhatsAppLineRequest(
    string InstanceName,
    string? PhoneNumber = null,
    WhatsAppProvider Provider = WhatsAppProvider.Evolution,
    /// <summary>Solo si Provider == Cloud.</summary>
    string? CloudPhoneNumberId = null,
    /// <summary>Solo si Provider == Cloud.</summary>
    string? CloudBusinessAccountId = null,
    /// <summary>Solo si Provider == Cloud. Texto plano; el servicio lo cifra.</summary>
    string? CloudAccessToken = null,
    /// <summary>Solo si Provider == Cloud. Texto plano; el servicio lo cifra.</summary>
    string? CloudWebhookVerifyToken = null,
    /// <summary>Solo si Provider == YCloud. Texto plano; el servicio lo cifra.</summary>
    string? YCloudApiKey = null,
    /// <summary>Solo si Provider == YCloud. Sender registrado en YCloud (numero).</summary>
    string? YCloudPhoneNumberId = null,
    /// <summary>Solo si Provider == YCloud. WABA id (para plantillas HSM).</summary>
    string? YCloudWabaId = null,
    /// <summary>Solo si Provider == YCloud. Secreto del webhook; el servicio lo cifra.</summary>
    string? YCloudWebhookSecret = null);

public sealed record ChangeLineStatusRequest(WhatsAppLineStatus Status);

public sealed record AssignLineRequest(Guid? TenantUserId);
