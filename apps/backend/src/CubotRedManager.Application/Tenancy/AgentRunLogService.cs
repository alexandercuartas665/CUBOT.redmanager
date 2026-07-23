using CubotRedManager.Application.Abstractions;
using CubotRedManager.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace CubotRedManager.Application.Tenancy;

/// <summary>
/// Bitacora del agente + reseteo de memoria. Ya con Conversations/Messages portados (Fase 1+2+3
/// del chat inbound), los metodos de borrado tocan las 4 tablas relevantes: AiAgentRunLogs,
/// AiAgentCacheValues, Messages y Conversation.AgentContextResetAt. Sin actualizar el reset el
/// dispatcher seguia reconstruyendo el contexto a partir del historial de mensajes.
/// </summary>
public sealed class AgentRunLogService : IAgentRunLogService
{
    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly TimeProvider _time;

    public AgentRunLogService(IApplicationDbContext db, ITenantContext tenantContext, TimeProvider time)
    {
        _db = db;
        _tenantContext = tenantContext;
        _time = time;
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
        // Filtro global por tenant se aplica automaticamente (todas TenantEntity); ExecuteDelete
        // corre el DELETE en BD sin traer filas a memoria.
        var logs = await _db.AiAgentRunLogs.ExecuteDeleteAsync(cancellationToken);
        var cache = await _db.AiAgentCacheValues.ExecuteDeleteAsync(cancellationToken);
        // CRITICO: sin borrar Messages y sin actualizar AgentContextResetAt, el dispatcher
        // reconstruye el contexto del chat leyendo directamente de _db.Messages con filtro por
        // AgentContextResetAt. Resultado sin este fix: "borrar bitacora" no afectaba lo que el
        // agente recordaba. Ver AgentDispatcher: lee _db.Messages filtrado por resetAt.
        await _db.Messages.ExecuteDeleteAsync(cancellationToken);
        var now = _time.GetUtcNow();
        await _db.Conversations.ExecuteUpdateAsync(
            s => s.SetProperty(c => c.AgentContextResetAt, (DateTimeOffset?)now)
                  .SetProperty(c => c.LastMessageAt, (DateTimeOffset?)null),
            cancellationToken);
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

    public async Task<IReadOnlyList<AgentConversationMessageDto>> GetConversationMessagesAsync(Guid conversationId, CancellationToken cancellationToken = default)
    {
        return await _db.Messages.AsNoTracking()
            .Where(m => m.ConversationId == conversationId)
            .OrderBy(m => m.SentAt)
            .Select(m => new AgentConversationMessageDto(
                m.SentAt,
                m.Direction == MessageDirection.Inbound ? "inbound" : "outbound",
                m.Body ?? "",
                m.SentByName))
            .ToListAsync(cancellationToken);
    }

    public async Task<(int Logs, int Cache, int Messages)> ResetConversationMemoryAsync(Guid conversationId, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is not Guid) { return (0, 0, 0); }
        // Filtro por tenant lo aplica el global filter en todas las tablas.
        var logs = await _db.AiAgentRunLogs.Where(l => l.ConversationId == conversationId)
            .ExecuteDeleteAsync(cancellationToken);
        var cache = await _db.AiAgentCacheValues.Where(v => v.SessionId == conversationId)
            .ExecuteDeleteAsync(cancellationToken);
        var messages = await _db.Messages.Where(m => m.ConversationId == conversationId)
            .ExecuteDeleteAsync(cancellationToken);
        // Setear AgentContextResetAt asegura que si llegan mensajes tarde a esta conversacion,
        // el dispatcher los filtre fuera del contexto. Sin esto, borrar la conversacion y hablar
        // por WhatsApp seguido reconstruia contexto con los mensajes recien borrados que se
        // guardaban en el intervalo entre reset y siguiente inbound.
        var now = _time.GetUtcNow();
        await _db.Conversations.Where(c => c.Id == conversationId)
            .ExecuteUpdateAsync(
                s => s.SetProperty(c => c.AgentContextResetAt, (DateTimeOffset?)now)
                      .SetProperty(c => c.LastMessageAt, (DateTimeOffset?)null),
                cancellationToken);
        return (logs, cache, messages);
    }

    private sealed record ConvAggregate(Guid Id, int Events, DateTimeOffset Last);
}
