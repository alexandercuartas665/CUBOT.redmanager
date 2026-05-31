using CubotRedManager.Domain.Common;

namespace CubotRedManager.Domain.Entities;

/// <summary>Respuesta enviada desde la consola a un mensaje de la bandeja (Modulo 2.6).</summary>
public class InboxReply : TenantEntity
{
    public Guid InboxMessageId { get; set; }
    public InboxMessage? Message { get; set; }

    public string Body { get; set; } = "";
    public Guid SentByTenantUserId { get; set; }
    public DateTimeOffset SentAt { get; set; }
    public string? ExternalRefId { get; set; }
    public string Status { get; set; } = "Sent";
}
