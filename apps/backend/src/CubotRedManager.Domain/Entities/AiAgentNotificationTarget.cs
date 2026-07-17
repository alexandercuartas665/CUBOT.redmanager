using CubotRedManager.Domain.Common;
using CubotRedManager.Domain.Enums;

namespace CubotRedManager.Domain.Entities;

/// <summary>
/// Destino de notificacion del agente: a donde se envia el contenido del marcador [[pedido: ...]]
/// cuando el agente cierra una atencion. Tenant-scoped. Portado desde CUBOT.travels.
/// </summary>
public class AiAgentNotificationTarget : TenantEntity
{
    public Guid AgentId { get; set; }
    public AiAgent? Agent { get; set; }

    /// <summary>Linea WhatsApp desde la que se envia el aviso.</summary>
    public Guid FromWhatsAppLineId { get; set; }
    public WhatsAppLine? FromWhatsAppLine { get; set; }

    public NotificationTargetKind TargetKind { get; set; } = NotificationTargetKind.Phone;

    /// <summary>Identificador del destinatario. Phone: numero con codigo de pais. Group: JID.</summary>
    public string TargetValue { get; set; } = null!;

    /// <summary>Etiqueta legible (ej. "Gerencia"). Opcional.</summary>
    public string? Label { get; set; }

    public int SortOrder { get; set; }
}
