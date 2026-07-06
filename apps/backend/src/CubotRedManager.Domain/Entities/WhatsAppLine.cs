using CubotRedManager.Domain.Common;
using CubotRedManager.Domain.Enums;

namespace CubotRedManager.Domain.Entities;

/// <summary>
/// Linea/instancia WhatsApp de una agencia. Entidad TENANT-SCOPED. Soporta tres proveedores:
/// Evolution API (QR no oficial, legacy), WhatsApp Cloud API oficial de Meta y YCloud (BSP oficial
/// con coexistencia). El proveedor se elige en el alta y no cambia despues. Las credenciales
/// especificas de cada proveedor viajan cifradas (DataProtection).
/// </summary>
public class WhatsAppLine : TenantEntity
{
    public string InstanceName { get; set; } = null!;
    public string? PhoneNumber { get; set; }
    public WhatsAppLineStatus Status { get; set; } = WhatsAppLineStatus.Created;
    public Guid? AssignedToTenantUserId { get; set; }
    public DateTimeOffset? LastConnectedAt { get; set; }
    public DateTimeOffset? LastStatusAt { get; set; }

    // === Proveedor ============================================================
    /// <summary>Proveedor de la linea. Inmutable despues del alta.</summary>
    public WhatsAppProvider Provider { get; set; } = WhatsAppProvider.Evolution;

    // === Credenciales Meta Cloud (solo cuando Provider == Cloud) ==============
    /// <summary>ID del numero de telefono en Meta (de la consola WhatsApp Manager). Indexado.</summary>
    public string? CloudPhoneNumberId { get; set; }

    /// <summary>ID de la WhatsApp Business Account (WABA) en Meta.</summary>
    public string? CloudBusinessAccountId { get; set; }

    /// <summary>Access token de la app de Meta, cifrado con DataProtection.</summary>
    public string? CloudAccessTokenEncrypted { get; set; }

    /// <summary>Verify token compartido entre Meta y el webhook (cifrado). Usado en el handshake GET.</summary>
    public string? CloudWebhookVerifyTokenEncrypted { get; set; }

    // === Credenciales YCloud (BSP oficial, solo cuando Provider == YCloud) =====
    /// <summary>API key de YCloud (header X-API-Key contra api.ycloud.com), cifrada con DataProtection.</summary>
    public string? YCloudApiKeyEncrypted { get; set; }

    /// <summary>Numero/sender (phone number) registrado en YCloud, en formato internacional sin "+".
    /// Sirve para resolver la linea desde el webhook entrante. Indexado unico cuando no es null.</summary>
    public string? YCloudPhoneNumberId { get; set; }

    /// <summary>ID de la WhatsApp Business Account (WABA) en YCloud/Meta. Necesario para plantillas HSM.</summary>
    public string? YCloudWabaId { get; set; }

    /// <summary>Secreto del webhook de YCloud (cifrado) para validar la firma de eventos entrantes.</summary>
    public string? YCloudWebhookSecretEncrypted { get; set; }
}
