using CubotRedManager.Domain.Common;
using CubotRedManager.Domain.Enums;

namespace CubotRedManager.Domain.Entities;

/// <summary>
/// Linea/instancia WhatsApp de una agencia. Entidad TENANT-SCOPED. La conexion real (QR, sesion)
/// se gestiona via el Evolution Connector; aqui se modela el ciclo de vida, el estado y la
/// asignacion operativa a un operador.
/// </summary>
public class WhatsAppLine : TenantEntity
{
    public string InstanceName { get; set; } = null!;
    public string? PhoneNumber { get; set; }
    public WhatsAppLineStatus Status { get; set; } = WhatsAppLineStatus.Created;
    public Guid? AssignedToTenantUserId { get; set; }
    public DateTimeOffset? LastConnectedAt { get; set; }
    public DateTimeOffset? LastStatusAt { get; set; }
}
