using CubotRedManager.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace CubotRedManager.Application.Tenancy;

/// <summary>
/// Implementacion adaptada desde CUBOT.travels. La version original hacia JOIN contra las tablas
/// Conversations y Messages (bandeja WhatsApp) para enriquecer con ContactName/Phone/LineLabel y
/// mostrar el hilo del chat. En redmanager esas entidades aun no se portaron (dependen del
/// modulo Lead+Pipeline). Mientras tanto, este servicio agrupa por ConversationId leyendo solo
/// de AiAgentRunLogs y AiAgentCacheFields/Values (que si existen). Cuando se porte
/// Conversations/Messages, restaurar los joins verbatim del original.
/// </summary>
public sealed class AgentRunLogService : IAgentRunLogService
{
    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenantContext;

    public AgentRunLogService(IApplicationDbContext db, ITenantContext tenantContext)
    {
        _db = db;
        _tenantContext = tenantContext;
    }

    public async Task<IReadOnlyList<AgentRunLogConversationDto>> ListConversationsAsync(CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is not Guid) { return Array.Empty<AgentRunLogConversationDto>(); }

        // Conteo y ultima actividad por conversation_id. Filtro por tenant viene del global filter.
        var grouped = await _db.AiAgentRunLogs.AsNoTracking()
            .GroupBy(l => l.ConversationId)
            .Select(g => new ConvAggregate(g.Key, g.Count(), g.Max(x => x.OccurredAt)))
            .ToListAsync(cancellationToken);

        if (grouped.Count == 0) { return Array.Empty<AgentRunLogConversationDto>(); }

        // TODO(port-conversations): cuando exista _db.Conversations, hacer JOIN para
        // recuperar ContactName/ContactPhone/WhatsAppLineId y enriquecer las tarjetas.
        return grouped
            .Select(g => new AgentRunLogConversationDto(
                g.Id,
                ContactName: null,
                ContactPhone: g.Id.ToString().Substring(0, 8),
                LineLabel: null,
                LastActivityAt: g.Last,
                Events: g.Events))
            .OrderByDescending(c => c.LastActivityAt)
            .ToList();
    }

    public async Task<IReadOnlyList<AgentRunLogEntryDto>> GetConversationLogAsync(Guid conversationId, CancellationToken cancellationToken = default)
        => await _db.AiAgentRunLogs.AsNoTracking()
            .Where(l => l.ConversationId == conversationId)
            .OrderBy(l => l.OccurredAt)
            .Select(l => new AgentRunLogEntryDto(l.OccurredAt, l.Kind, l.Title, l.Content, l.Response))
            .ToListAsync(cancellationToken);

    public async Task<(int Logs, int Cache)> ClearAllAsync(CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is not Guid) { return (0, 0); }
        // Filtro global por tenant se aplica automaticamente (ambas entidades son TenantEntity);
        // ExecuteDelete corre el DELETE en BD sin traer las filas a memoria.
        // Borramos TAMBIEN el cache de datos capturados: "Limpiar historia" debe dejar al agente
        // en cero. Antes solo se borraba la bitacora y el cache quedaba huerfano (ya no se podia
        // seleccionar la conversacion en la UI para reiniciarlo). No tocamos mensajes ni leads.
        var logs = await _db.AiAgentRunLogs.ExecuteDeleteAsync(cancellationToken);
        var cache = await _db.AiAgentCacheValues.ExecuteDeleteAsync(cancellationToken);
        return (logs, cache);
    }

    public async Task<IReadOnlyList<AgentCacheItemDto>> GetConversationCacheAsync(Guid conversationId, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is not Guid) { return Array.Empty<AgentCacheItemDto>(); }

        // Para mostrar el cache necesitamos el agente que atendio: lo deducimos de cualquier
        // entrada de bitacora de la conversacion (todas comparten AgentId).
        var agentId = await _db.AiAgentRunLogs.AsNoTracking()
            .Where(l => l.ConversationId == conversationId)
            .Select(l => l.AgentId)
            .FirstOrDefaultAsync(cancellationToken);
        if (agentId == Guid.Empty) { return Array.Empty<AgentCacheItemDto>(); }

        // SessionId == ConversationId desde que el dispatcher lo cablea explicitamente.
        var fields = await _db.AiAgentCacheFields.AsNoTracking()
            .Where(f => f.AgentId == agentId)
            .OrderBy(f => f.SortOrder).ThenBy(f => f.Label)
            .Select(f => new { f.FieldKey, f.Label })
            .ToListAsync(cancellationToken);

        var values = await _db.AiAgentCacheValues.AsNoTracking()
            .Where(v => v.AgentId == agentId && v.SessionId == conversationId)
            .ToDictionaryAsync(v => v.FieldKey, v => new { v.Value, v.Source }, cancellationToken);

        return fields
            .Select(f => values.TryGetValue(f.FieldKey, out var x)
                ? new AgentCacheItemDto(f.FieldKey, f.Label, x.Value, x.Source)
                : new AgentCacheItemDto(f.FieldKey, f.Label, null, null))
            .ToList();
    }

    public Task<IReadOnlyList<AgentConversationMessageDto>> GetConversationMessagesAsync(Guid conversationId, CancellationToken cancellationToken = default)
    {
        // TODO(port-conversations): en travels lee de _db.Messages ordenado por SentAt y mapea
        // Direction (Inbound/Outbound). En redmanager esa tabla aun no existe; se devuelve vacio.
        _ = conversationId;
        return Task.FromResult<IReadOnlyList<AgentConversationMessageDto>>(Array.Empty<AgentConversationMessageDto>());
    }

    public async Task<(int Logs, int Cache, int Messages)> ResetConversationMemoryAsync(Guid conversationId, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is not Guid) { return (0, 0, 0); }
        // Filtro por tenant lo aplica el global filter. Borramos los logs y el cache de esta
        // conversacion (sessionId == conversationId). Cuando exista _db.Messages tambien habra
        // que borrar mensajes y resetear Conversation.LastMessageAt (ver original en travels).
        var logs = await _db.AiAgentRunLogs.Where(l => l.ConversationId == conversationId)
            .ExecuteDeleteAsync(cancellationToken);
        var cache = await _db.AiAgentCacheValues.Where(v => v.SessionId == conversationId)
            .ExecuteDeleteAsync(cancellationToken);
        return (logs, cache, Messages: 0);
    }

    private sealed record ConvAggregate(Guid Id, int Events, DateTimeOffset Last);
}
