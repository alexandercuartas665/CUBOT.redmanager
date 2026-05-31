using CubotRedManager.Domain.Common;
using CubotRedManager.Domain.Enums;

namespace CubotRedManager.Domain.Entities;

/// <summary>
/// Evento recibido del webhook de Wompi. Entidad global. Garantiza idempotencia: ProviderEventId
/// es unico, asi un reenvio del mismo evento no se procesa dos veces. Guarda el payload crudo y el
/// resultado para conciliacion.
/// </summary>
public class WompiWebhookEvent : BaseEntity
{
    public string ProviderEventId { get; set; } = null!;
    public bool SignatureValid { get; set; }
    public string RawPayload { get; set; } = null!;
    public WebhookProcessingStatus ProcessingStatus { get; set; } = WebhookProcessingStatus.Received;
    public string? TransactionId { get; set; }
    public string? Reference { get; set; }
    public string? Note { get; set; }
    public DateTimeOffset ReceivedAt { get; set; }
    public DateTimeOffset? ProcessedAt { get; set; }
}
