namespace CubotRedManager.Application.Abstractions;

/// <summary>
/// Registra acciones sensibles. Solo agrega la entrada al contexto; el caso de uso decide cuando
/// persistir (SaveChanges). La persistencia real se completa con el modulo de Auditoria (2.9);
/// por ahora puede haber una implementacion no-op.
/// </summary>
public interface IAuditWriter
{
    void Write(
        Guid actorUserId,
        string actionName,
        string entityName,
        Guid? entityId,
        object? previousValue,
        object? newValue,
        Guid? tenantId = null,
        string? reason = null);
}
