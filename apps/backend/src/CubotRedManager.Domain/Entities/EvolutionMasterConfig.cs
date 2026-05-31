using CubotRedManager.Domain.Common;
using CubotRedManager.Domain.Enums;

namespace CubotRedManager.Domain.Entities;

/// <summary>
/// Servidor Evolution API maestro de la plataforma (Super Admin SaaS). GLOBAL y singleton:
/// las agencias pueden usar este servidor compartido o configurar uno propio. La API key se
/// guarda cifrada (ISecretProtector) y nunca se expone completa ni se loggea.
/// </summary>
public class EvolutionMasterConfig : BaseEntity
{
    /// <summary>URL base del servidor Evolution API (p.ej. https://evo.cubot.com.co).</summary>
    public string? BaseUrl { get; set; }

    /// <summary>API key global del servidor (AUTHENTICATION_API_KEY) cifrada en reposo.</summary>
    public string? ApiKeyEncrypted { get; set; }

    public EvolutionIntegrationStatus Status { get; set; } = EvolutionIntegrationStatus.NotConfigured;
    public DateTimeOffset? LastValidatedAt { get; set; }

    // ===== Webhook entrante (mensajes en caliente) =====
    /// <summary>Modo del webhook: "Development" (tunel local) o "Production" (URL fija del dominio).</summary>
    public string WebhookMode { get; set; } = "Development";

    /// <summary>URL publica fija para produccion (p.ej. https://app.cubot.com.co).</summary>
    public string? WebhookPublicUrl { get; set; }

    /// <summary>URL publica activa en modo desarrollo (la fija el tunel al iniciarse).</summary>
    public string? WebhookActiveUrl { get; set; }

    /// <summary>Token compartido que valida los webhooks entrantes de Evolution.</summary>
    public string? WebhookToken { get; set; }
}
