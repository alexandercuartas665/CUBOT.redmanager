using CubotRedManager.Application.Abstractions;

namespace CubotRedManager.Infrastructure.Security;

/// <summary>
/// Implementacion temporal no-op de auditoria. La auditoria persistente real se implementa con el
/// Modulo 2.9 (AuditLog). Permite portar servicios que dependen de IAuditWriter sin arrastrar aun
/// la tabla de auditoria.
/// </summary>
public sealed class NoOpAuditWriter : IAuditWriter
{
    public void Write(Guid actorUserId, string actionName, string entityName, Guid? entityId,
        object? previousValue, object? newValue, Guid? tenantId = null, string? reason = null)
    {
        // Intencionalmente vacio por ahora.
    }
}
