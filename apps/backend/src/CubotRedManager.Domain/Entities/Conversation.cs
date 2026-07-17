using CubotRedManager.Domain.Common;

namespace CubotRedManager.Domain.Entities;

/// <summary>
/// Conversacion de WhatsApp con un contacto. Entidad TENANT-SCOPED.
/// Una por (TenantId, ContactPhone). Puede asociarse a un lead (en redmanager LeadId aun no
/// se usa porque no se ha portado el pipeline; se deja para paridad de esquema con travels).
/// Portado desde CUBOT.travels.
/// </summary>
public class Conversation : TenantEntity
{
    public string ContactPhone { get; set; } = null!;
    public string? ContactName { get; set; }
    public Guid? LeadId { get; set; }
    public Guid? WhatsAppLineId { get; set; }
    public DateTimeOffset? LastMessageAt { get; set; }

    /// <summary>Conversacion archivada (oculta de la bandeja activa). Null = activa. Si llega un mensaje
    /// entrante nuevo, se desarchiva automaticamente para que el asesor no lo pierda.</summary>
    public DateTimeOffset? ArchivedAt { get; set; }

    /// <summary>
    /// Punto de reinicio del contexto del agente. Cuando se archiva el lead de esta conversacion,
    /// se pone en "ahora": el AgentDispatcher solo considera los mensajes posteriores a esta marca
    /// al armar el historial para el LLM. Asi, si el contacto vuelve a escribir, el agente arranca
    /// la conversacion desde cero (saluda) en vez de continuar el hilo ya cerrado. Null = sin reinicio.
    /// </summary>
    public DateTimeOffset? AgentContextResetAt { get; set; }
}
