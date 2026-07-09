using CubotRedManager.Domain.Common;
using CubotRedManager.Domain.Enums;

namespace CubotRedManager.Domain.Entities;

/// <summary>
/// Configuracion de alertas operativas del tenant enviadas por WhatsApp (via linea Evolution).
/// Una fila por tenant. Actualmente se usa para avisar al admin cuando el refresh de un token
/// TikTok falla; el diseno esta abierto a que se sumen otras alertas en el futuro (webhooks
/// caidos, cuotas superadas, etc.) sin requerir un modelo por tipo de alerta.
/// </summary>
public class TenantAlertConfig : TenantEntity
{
    /// <summary>Master switch. Si esta apagado, el sistema no envia ninguna alerta.</summary>
    public bool IsActive { get; set; }

    /// <summary>Linea Evolution del tenant a traves de la cual se envian las alertas.</summary>
    public Guid? WhatsAppLineId { get; set; }
    public WhatsAppLine? WhatsAppLine { get; set; }

    /// <summary>Destinatario: telefono directo o grupo WhatsApp.</summary>
    public AutoReplySummaryTargetType TargetType { get; set; } = AutoReplySummaryTargetType.Phone;

    /// <summary>E.164 sin '+' si TargetType=Phone; JID xxx@g.us si TargetType=Group.</summary>
    public string? Target { get; set; }
}
