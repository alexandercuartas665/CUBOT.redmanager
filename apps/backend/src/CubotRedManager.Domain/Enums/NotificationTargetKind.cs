namespace CubotRedManager.Domain.Enums;

/// <summary>Tipo de receptor para destinos de notificacion de agentes IA ([[pedido: ...]]).</summary>
public enum NotificationTargetKind
{
    /// <summary>Numero individual de WhatsApp (formato internacional, ej. 573001234567).</summary>
    Phone = 0,

    /// <summary>JID de grupo WhatsApp (formato &lt;id&gt;@g.us). Solo soportado por Evolution API.</summary>
    Group
}
