using System.Text.Json;
using CubotRedManager.Application.Abstractions;
using CubotRedManager.Domain.Entities;

namespace CubotRedManager.Infrastructure.Security;

/// <summary>
/// Escribe acciones sensibles en super_admin_audit_logs. Solo agrega la entrada al contexto;
/// el caso de uso decide cuando persistir (SaveChanges). Nunca debe recibir secretos en claro.
/// </summary>
public sealed class AuditWriter : IAuditWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IApplicationDbContext _db;

    public AuditWriter(IApplicationDbContext db) => _db = db;

    public void Write(Guid actorUserId, string actionName, string entityName, Guid? entityId,
        object? previousValue, object? newValue, Guid? tenantId = null, string? reason = null)
    {
        _db.SuperAdminAuditLogs.Add(new SuperAdminAuditLog
        {
            ActorUserId = actorUserId,
            ActionName = actionName,
            EntityName = entityName,
            EntityId = entityId,
            TenantId = tenantId,
            PreviousValue = previousValue is null ? null : JsonSerializer.Serialize(previousValue, JsonOptions),
            NewValue = newValue is null ? null : JsonSerializer.Serialize(newValue, JsonOptions),
            Reason = reason
        });
    }
}
