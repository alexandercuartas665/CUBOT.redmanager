using CubotRedManager.Domain.Common;
using CubotRedManager.Domain.Enums;

namespace CubotRedManager.Domain.Entities;

/// <summary>
/// Configuracion maestra de Wompi del dueno de la plataforma (Super Admin SaaS). GLOBAL y singleton:
/// con ella CUBOT.redmanager cobra las suscripciones a las agencias. La llave privada y los secrets
/// se guardan cifrados (ISecretProtector) y nunca se exponen completos ni se loggean.
/// </summary>
public class WompiMasterConfig : BaseEntity
{
    public WompiEnvironment Environment { get; set; } = WompiEnvironment.Sandbox;
    public string? PublicKey { get; set; }
    public string? PrivateKeyEncrypted { get; set; }
    public string? EventsSecretEncrypted { get; set; }
    public string? IntegritySecretEncrypted { get; set; }
    public string? WebhookEndpoint { get; set; }
    public string Currency { get; set; } = "COP";
    public int MaxRetries { get; set; } = 3;
    public WompiIntegrationStatus Status { get; set; } = WompiIntegrationStatus.NotConfigured;
    public DateTimeOffset? LastValidatedAt { get; set; }
}
