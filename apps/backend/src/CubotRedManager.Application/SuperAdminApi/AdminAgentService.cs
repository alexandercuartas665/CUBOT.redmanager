using CubotRedManager.Application.Abstractions;
using CubotRedManager.Domain.Entities;
using CubotRedManager.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace CubotRedManager.Application.SuperAdminApi;

/// <summary>
/// Servicio cross-tenant que expone las mutaciones de agentes del brief "Admin Agent API".
/// El super admin no tiene tenant en el JWT — este servicio fija el tenant recibido por ruta en
/// AmbientTenantOverride ANTES de tocar el DbContext, y confia en el HasQueryFilter (modelo B,
/// sin RLS) para el aislamiento. Todo endpoint debe usar el patron:
///   using (svc.Impersonate(tenantId)) { ... }
/// asi el cleanup ocurre incluso si algo lanza (evita fuga de tenant entre requests reusando el
/// scope, aunque en HTTP cada request tiene scope nuevo — la disciplina cuesta poco y protege).
/// </summary>
public interface IAdminAgentService
{
    IDisposable Impersonate(Guid tenantId);

    Task<IReadOnlyList<AiAgentDto>> ListAgentsAsync(Guid tenantId, CancellationToken ct = default);
    Task<AiAgentDetailDto?> GetAgentAsync(Guid tenantId, Guid agentId, CancellationToken ct = default);
    Task<AiAgentDto> CreateAgentAsync(Guid tenantId, CreateAiAgentRequest request, CancellationToken ct = default);
    Task<AiAgentDto?> UpdateAgentAsync(Guid tenantId, Guid agentId, UpdateAiAgentRequest request, CancellationToken ct = default);
    Task<AiAgentDetailDto?> SetAgentToolsAsync(Guid tenantId, Guid agentId, IReadOnlyList<string> toolKeys, CancellationToken ct = default);

    Task<IReadOnlyList<AgentRunLogConversationDto>> ListRunLogConversationsAsync(Guid tenantId, int take = 100, CancellationToken ct = default);
    Task<IReadOnlyList<AgentRunLogEntryDto>> GetRunLogEntriesAsync(Guid tenantId, Guid conversationId, CancellationToken ct = default);

    // Lines (WhatsApp) — PR3
    Task<IReadOnlyList<AdminLineDto>> ListLinesAsync(Guid tenantId, CancellationToken ct = default);
    Task<LineBindingResult> BindLineAsync(Guid tenantId, Guid agentId, Guid whatsAppLineId, CancellationToken ct = default);
    Task<bool> UnbindLineAsync(Guid tenantId, Guid agentId, Guid whatsAppLineId, CancellationToken ct = default);
}

public sealed class AdminAgentService : IAdminAgentService
{
    private readonly IApplicationDbContext _db;
    private readonly IAmbientTenantOverride _ambient;

    public AdminAgentService(IApplicationDbContext db, IAmbientTenantOverride ambient)
    {
        _db = db;
        _ambient = ambient;
    }

    public IDisposable Impersonate(Guid tenantId)
    {
        _ambient.Set(tenantId, null);
        return new ImpersonationScope(_ambient);
    }

    private sealed class ImpersonationScope : IDisposable
    {
        private readonly IAmbientTenantOverride _amb;
        public ImpersonationScope(IAmbientTenantOverride amb) { _amb = amb; }
        public void Dispose() => _amb.Set(null, null);
    }

    // -----------------------------------------------------------------------------------
    // Agents
    // -----------------------------------------------------------------------------------

    public async Task<IReadOnlyList<AiAgentDto>> ListAgentsAsync(Guid tenantId, CancellationToken ct = default)
    {
        using var _ = Impersonate(tenantId);
        var agents = await _db.AiAgents
            .AsNoTracking()
            .OrderBy(a => a.SortOrder).ThenBy(a => a.Name)
            .ToListAsync(ct);
        return agents.Select(ToDto).ToList();
    }

    public async Task<AiAgentDetailDto?> GetAgentAsync(Guid tenantId, Guid agentId, CancellationToken ct = default)
    {
        using var _ = Impersonate(tenantId);
        var agent = await _db.AiAgents.AsNoTracking().FirstOrDefaultAsync(a => a.Id == agentId, ct);
        if (agent is null) { return null; }

        var resources = await _db.AiAgentResources.AsNoTracking()
            .Where(r => r.AgentId == agentId)
            .OrderBy(r => r.SortOrder).ThenBy(r => r.Name)
            .ToListAsync(ct);

        var prompts = await _db.AiAgentPrompts.AsNoTracking()
            .Where(p => p.AgentId == agentId)
            .OrderBy(p => p.SortOrder).ThenBy(p => p.Name)
            .ToListAsync(ct);

        return new AiAgentDetailDto(
            Agent: ToDto(agent),
            SystemPrompt: agent.SystemPrompt ?? string.Empty,
            Resources: resources.Select(r => new AiAgentResourceDto(
                r.Id, r.Name, r.ResourceType, r.Detail, r.FileUrl, r.FileName, r.FileMimeType, r.SortOrder)).ToList(),
            Prompts: prompts.Select(p => new AiAgentPromptDto(
                p.Id, p.Name, p.Rule, p.Body ?? string.Empty, p.SortOrder)).ToList());
    }

    public async Task<AiAgentDto> CreateAgentAsync(Guid tenantId, CreateAiAgentRequest request, CancellationToken ct = default)
    {
        using var _ = Impersonate(tenantId);
        var nextSort = await _db.AiAgents.AsNoTracking().MaxAsync(a => (int?)a.SortOrder, ct) ?? 0;
        var entity = new AiAgent
        {
            // TenantId hay que setearlo explicito: el SaveChanges NO lo autopoblado (solo maneja
            // CreatedAt/UpdatedAt). Sin esto, TenantId quedaria en Guid.Empty y el HasQueryFilter
            // lo escondera para siempre.
            TenantId = tenantId,
            Name = request.Name?.Trim() ?? throw new ArgumentException("name required"),
            Role = string.IsNullOrWhiteSpace(request.Role) ? null : request.Role.Trim(),
            Provider = request.Provider,
            Model = string.IsNullOrWhiteSpace(request.Model) ? null : request.Model.Trim(),
            SystemPrompt = request.SystemPrompt ?? string.Empty,
            IsActive = request.IsActive,
            SortOrder = nextSort + 1
        };
        _db.AiAgents.Add(entity);
        await _db.SaveChangesAsync(ct);
        return ToDto(entity);
    }

    public async Task<AiAgentDto?> UpdateAgentAsync(Guid tenantId, Guid agentId, UpdateAiAgentRequest request, CancellationToken ct = default)
    {
        using var _ = Impersonate(tenantId);
        var entity = await _db.AiAgents.FirstOrDefaultAsync(a => a.Id == agentId, ct);
        if (entity is null) { return null; }

        entity.Name = request.Name?.Trim() ?? entity.Name;
        entity.Role = string.IsNullOrWhiteSpace(request.Role) ? null : request.Role.Trim();
        entity.Provider = request.Provider;
        entity.Model = string.IsNullOrWhiteSpace(request.Model) ? null : request.Model.Trim();
        entity.SystemPrompt = request.SystemPrompt ?? string.Empty;
        entity.IsActive = request.IsActive;
        await _db.SaveChangesAsync(ct);
        return ToDto(entity);
    }

    public async Task<AiAgentDetailDto?> SetAgentToolsAsync(Guid tenantId, Guid agentId, IReadOnlyList<string> toolKeys, CancellationToken ct = default)
    {
        using var _ = Impersonate(tenantId);
        var entity = await _db.AiAgents.FirstOrDefaultAsync(a => a.Id == agentId, ct);
        if (entity is null) { return null; }

        var normalized = (toolKeys ?? Array.Empty<string>())
            .Where(k => !string.IsNullOrWhiteSpace(k))
            .Select(k => k.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Los flags binarios se setean todos: los que estan en toolKeys quedan true, el resto false.
        // Esto convierte al PUT en un "reemplazo" (idempotente por definicion) y no un "toggle".
        entity.PaymentEnabled = normalized.Contains(AdminAgentTools.Payment);
        entity.ReactionsEnabled = normalized.Contains(AdminAgentTools.Reactions);
        entity.EnableDataContainerMcp = normalized.Contains(AdminAgentTools.DataContainerMcp);
        await _db.SaveChangesAsync(ct);

        return await GetAgentAsync(tenantId, agentId, ct);
    }

    // -----------------------------------------------------------------------------------
    // Run logs (bitacora del agente)
    // -----------------------------------------------------------------------------------

    public async Task<IReadOnlyList<AgentRunLogConversationDto>> ListRunLogConversationsAsync(Guid tenantId, int take = 100, CancellationToken ct = default)
    {
        using var _ = Impersonate(tenantId);
        // GroupBy con Select-a-DTO no siempre lo traduce Npgsql cuando el DTO es un record con
        // ctor complejo. Proyectamos a anonimo (que EF traduce a SELECT ... FROM (GROUP BY ...))
        // y materializamos al DTO en memoria — el conteo total es < 500 por Take, no es costoso.
        var raw = await _db.AiAgentRunLogs
            .AsNoTracking()
            .GroupBy(l => l.ConversationId)
            .Select(g => new
            {
                ConversationId = g.Key,
                LastOccurredAt = g.Max(x => x.OccurredAt),
                EntryCount = g.Count()
            })
            .OrderByDescending(x => x.LastOccurredAt)
            .Take(Math.Clamp(take, 1, 500))
            .ToListAsync(ct);
        return raw.Select(r => new AgentRunLogConversationDto(r.ConversationId, r.LastOccurredAt, r.EntryCount)).ToList();
    }

    public async Task<IReadOnlyList<AgentRunLogEntryDto>> GetRunLogEntriesAsync(Guid tenantId, Guid conversationId, CancellationToken ct = default)
    {
        using var _ = Impersonate(tenantId);
        var entries = await _db.AiAgentRunLogs
            .AsNoTracking()
            .Where(l => l.ConversationId == conversationId)
            .OrderBy(l => l.OccurredAt)
            .Select(l => new AgentRunLogEntryDto(
                l.Id, l.OccurredAt, l.Kind, l.Title, l.Content, l.Response))
            .ToListAsync(ct);
        return entries;
    }

    // -----------------------------------------------------------------------------------
    // Lines (WhatsApp) + line-binding
    // -----------------------------------------------------------------------------------

    public async Task<IReadOnlyList<AdminLineDto>> ListLinesAsync(Guid tenantId, CancellationToken ct = default)
    {
        using var _ = Impersonate(tenantId);
        // Outer join manual con AiAgentLineBinding (IsConnected=true) para el BoundAgentId.
        // No usamos navegacion inversa (WhatsAppLine no la tiene declarada) — LINQ explicito.
        var lines = await _db.WhatsAppLines.AsNoTracking()
            .OrderBy(l => l.InstanceName)
            .ToListAsync(ct);
        var bindings = await _db.AiAgentLineBindings.AsNoTracking()
            .Where(b => b.IsConnected)
            .ToListAsync(ct);
        var boundByLine = bindings.ToDictionary(b => b.WhatsAppLineId, b => b.AgentId);
        return lines.Select(l => new AdminLineDto(
            l.Id, l.InstanceName, l.Provider, l.PhoneNumber, l.Status,
            boundByLine.TryGetValue(l.Id, out var aid) ? aid : (Guid?)null)).ToList();
    }

    public async Task<LineBindingResult> BindLineAsync(Guid tenantId, Guid agentId, Guid whatsAppLineId, CancellationToken ct = default)
    {
        using var _ = Impersonate(tenantId);

        // El agente y la linea deben existir dentro del tenant.
        var agentExists = await _db.AiAgents.AsNoTracking().AnyAsync(a => a.Id == agentId, ct);
        if (!agentExists) { return new LineBindingResult(false, "agent_not_found"); }
        var lineExists = await _db.WhatsAppLines.AsNoTracking().AnyAsync(l => l.Id == whatsAppLineId, ct);
        if (!lineExists) { return new LineBindingResult(false, "line_not_found"); }

        // Regla del brief: una linea solo puede tener UN agente activo (IsConnected=true).
        var current = await _db.AiAgentLineBindings
            .FirstOrDefaultAsync(b => b.WhatsAppLineId == whatsAppLineId && b.IsConnected, ct);

        if (current is not null && current.AgentId != agentId)
        {
            // Otro agente ya atiende — devolvemos 409. Para reasignar, el cliente hace DELETE
            // primero. Mantenerlo explicito evita "reasignaciones silenciosas" que pierden trafico.
            return new LineBindingResult(false, "line_already_bound", current.AgentId);
        }

        if (current is not null && current.AgentId == agentId)
        {
            // Ya estaba vinculado a este agente — idempotente.
            return new LineBindingResult(true);
        }

        // Reactivar binding existente (misma pareja agent+line) o crear nuevo.
        var existing = await _db.AiAgentLineBindings
            .FirstOrDefaultAsync(b => b.AgentId == agentId && b.WhatsAppLineId == whatsAppLineId, ct);
        if (existing is null)
        {
            _db.AiAgentLineBindings.Add(new AiAgentLineBinding
            {
                TenantId = tenantId,
                AgentId = agentId,
                WhatsAppLineId = whatsAppLineId,
                IsConnected = true,
                AutoConfirm = true
            });
        }
        else
        {
            existing.IsConnected = true;
        }
        await _db.SaveChangesAsync(ct);
        return new LineBindingResult(true);
    }

    public async Task<bool> UnbindLineAsync(Guid tenantId, Guid agentId, Guid whatsAppLineId, CancellationToken ct = default)
    {
        using var _ = Impersonate(tenantId);
        var binding = await _db.AiAgentLineBindings
            .FirstOrDefaultAsync(b => b.AgentId == agentId && b.WhatsAppLineId == whatsAppLineId && b.IsConnected, ct);
        if (binding is null) { return false; }

        // Marcamos IsConnected=false en vez de borrar la fila: conservamos historial de
        // reasignaciones (patron del brief; ver comentario en la entidad).
        binding.IsConnected = false;
        await _db.SaveChangesAsync(ct);
        return true;
    }

    // -----------------------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------------------

    private static AiAgentDto ToDto(AiAgent a)
    {
        var tools = new List<string>(3);
        if (a.PaymentEnabled) { tools.Add(AdminAgentTools.Payment); }
        if (a.ReactionsEnabled) { tools.Add(AdminAgentTools.Reactions); }
        if (a.EnableDataContainerMcp) { tools.Add(AdminAgentTools.DataContainerMcp); }
        return new AiAgentDto(
            a.Id, a.Name, a.Role, a.Provider, a.Model, a.IsActive, a.SortOrder,
            a.ReactionsEnabled, a.PaymentEnabled, a.EnableDataContainerMcp,
            tools);
    }
}
