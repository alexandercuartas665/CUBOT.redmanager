using CubotRedManager.Application.Abstractions;
using CubotRedManager.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CubotRedManager.Application.Tenancy;

public sealed class AiAgentService : IAiAgentService
{
    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly IAuditWriter _audit;

    public AiAgentService(IApplicationDbContext db, ITenantContext tenantContext, IAuditWriter audit)
    {
        _db = db;
        _tenantContext = tenantContext;
        _audit = audit;
    }

    public async Task<IReadOnlyList<AiAgentDto>> ListAsync(CancellationToken cancellationToken = default)
    {
        var agents = await _db.AiAgents.AsNoTracking()
            .OrderBy(a => a.SortOrder).ThenBy(a => a.Name)
            .ToListAsync(cancellationToken);
        var counts = await _db.AiAgentResources.AsNoTracking()
            .GroupBy(r => r.AgentId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, cancellationToken);
        return agents.Select(a => Map(a, counts.TryGetValue(a.Id, out var c) ? c : 0)).ToList();
    }

    public async Task<AiAgentDetailDto?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var agent = await _db.AiAgents.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
        if (agent is null) { return null; }
        var resources = await _db.AiAgentResources.AsNoTracking()
            .Where(r => r.AgentId == id)
            .OrderBy(r => r.SortOrder)
            .Select(r => MapResource(r))
            .ToListAsync(cancellationToken);
        var prompts = await _db.AiAgentPrompts.AsNoTracking()
            .Where(p => p.AgentId == id)
            .OrderBy(p => p.SortOrder)
            .Select(p => MapPrompt(p))
            .ToListAsync(cancellationToken);
        return new AiAgentDetailDto(Map(agent, resources.Count), resources, prompts);
    }

    public async Task<AiAgentDto?> CreateAsync(CreateAiAgentRequest request, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId) { return null; }
        var nextOrder = (await _db.AiAgents.Select(a => (int?)a.SortOrder).MaxAsync(cancellationToken) ?? -1) + 1;
        var agent = new AiAgent
        {
            TenantId = tenantId,
            Name = (request.Name ?? "Agente").Trim(),
            Role = request.Role?.Trim(),
            Provider = request.Provider,
            Model = string.IsNullOrWhiteSpace(request.Model) ? null : request.Model.Trim(),
            SystemPrompt = request.SystemPrompt ?? "",
            IsActive = false,
            EnableDataContainerMcp = request.EnableDataContainerMcp,
            SortOrder = nextOrder
        };
        _db.AiAgents.Add(agent);
        _audit.Write(actorUserId, "ai-agent.create", nameof(AiAgent), agent.Id,
            previousValue: null, newValue: new { agent.Name, agent.Provider }, tenantId: tenantId);
        await _db.SaveChangesAsync(cancellationToken);
        return Map(agent, 0);
    }

    public async Task<AiAgentDto?> UpdateAsync(Guid id, UpdateAiAgentRequest request, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var agent = await _db.AiAgents.FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
        if (agent is null) { return null; }
        agent.Name = (request.Name ?? agent.Name).Trim();
        agent.Role = request.Role?.Trim();
        agent.Provider = request.Provider;
        agent.Model = string.IsNullOrWhiteSpace(request.Model) ? null : request.Model.Trim();
        agent.SystemPrompt = request.SystemPrompt ?? "";
        agent.EnableDataContainerMcp = request.EnableDataContainerMcp;
        await _db.SaveChangesAsync(cancellationToken);
        var count = await _db.AiAgentResources.CountAsync(r => r.AgentId == id, cancellationToken);
        return Map(agent, count);
    }

    public async Task<AiAgentDto?> SetActiveAsync(Guid id, bool active, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var agent = await _db.AiAgents.FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
        if (agent is null) { return null; }
        agent.IsActive = active;
        _audit.Write(actorUserId, active ? "ai-agent.activate" : "ai-agent.deactivate", nameof(AiAgent), agent.Id,
            previousValue: null, newValue: new { active }, tenantId: agent.TenantId);
        await _db.SaveChangesAsync(cancellationToken);
        var count = await _db.AiAgentResources.CountAsync(r => r.AgentId == id, cancellationToken);
        return Map(agent, count);
    }

    public async Task<bool> DeleteAsync(Guid id, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var agent = await _db.AiAgents.FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
        if (agent is null) { return false; }
        _db.AiAgents.Remove(agent);
        _audit.Write(actorUserId, "ai-agent.delete", nameof(AiAgent), agent.Id,
            previousValue: new { agent.Name }, newValue: null, tenantId: agent.TenantId);
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<AiAgentResourceDto?> AddResourceAsync(CreateAgentResourceRequest request, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId) { return null; }
        var agent = await _db.AiAgents.FirstOrDefaultAsync(a => a.Id == request.AgentId, cancellationToken);
        if (agent is null) { return null; }
        var nextOrder = (await _db.AiAgentResources.Where(r => r.AgentId == request.AgentId).Select(r => (int?)r.SortOrder).MaxAsync(cancellationToken) ?? -1) + 1;
        var res = new AiAgentResource
        {
            TenantId = tenantId,
            AgentId = request.AgentId,
            Name = (request.Name ?? "Recurso").Trim(),
            ResourceType = request.ResourceType,
            Detail = request.Detail,
            FileUrl = request.FileUrl,
            FileName = request.FileName,
            SortOrder = nextOrder
        };
        _db.AiAgentResources.Add(res);
        await _db.SaveChangesAsync(cancellationToken);
        return MapResource(res);
    }

    public async Task<AiAgentResourceDto?> UpdateResourceAsync(Guid id, UpdateAgentResourceRequest request, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var res = await _db.AiAgentResources.FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (res is null) { return null; }
        res.Name = (request.Name ?? res.Name).Trim();
        res.ResourceType = request.ResourceType;
        res.Detail = request.Detail;
        res.FileUrl = request.FileUrl;
        res.FileName = request.FileName;
        await _db.SaveChangesAsync(cancellationToken);
        return MapResource(res);
    }

    public async Task<bool> DeleteResourceAsync(Guid id, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var res = await _db.AiAgentResources.FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (res is null) { return false; }
        _db.AiAgentResources.Remove(res);
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<AiAgentPromptDto?> AddPromptAsync(CreateAgentPromptRequest request, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId) { return null; }
        var agent = await _db.AiAgents.FirstOrDefaultAsync(a => a.Id == request.AgentId, cancellationToken);
        if (agent is null) { return null; }
        var nextOrder = (await _db.AiAgentPrompts.Where(p => p.AgentId == request.AgentId).Select(p => (int?)p.SortOrder).MaxAsync(cancellationToken) ?? -1) + 1;
        var prompt = new AiAgentPrompt
        {
            TenantId = tenantId,
            AgentId = request.AgentId,
            Name = (request.Name ?? "Prompt").Trim(),
            Rule = string.IsNullOrWhiteSpace(request.Rule) ? null : request.Rule.Trim(),
            Body = request.Body ?? "",
            SortOrder = nextOrder
        };
        _db.AiAgentPrompts.Add(prompt);
        await _db.SaveChangesAsync(cancellationToken);
        return MapPrompt(prompt);
    }

    public async Task<AiAgentPromptDto?> UpdatePromptAsync(Guid id, UpdateAgentPromptRequest request, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var prompt = await _db.AiAgentPrompts.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (prompt is null) { return null; }
        prompt.Name = (request.Name ?? prompt.Name).Trim();
        prompt.Rule = string.IsNullOrWhiteSpace(request.Rule) ? null : request.Rule.Trim();
        prompt.Body = request.Body ?? "";
        await _db.SaveChangesAsync(cancellationToken);
        return MapPrompt(prompt);
    }

    public async Task<bool> DeletePromptAsync(Guid id, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var prompt = await _db.AiAgentPrompts.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (prompt is null) { return false; }
        _db.AiAgentPrompts.Remove(prompt);
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    private static AiAgentDto Map(AiAgent a, int resourceCount) =>
        new(a.Id, a.Name, a.Role, a.Provider, a.Model, a.SystemPrompt, a.IsActive, a.EnableDataContainerMcp, a.SortOrder, resourceCount);

    private static AiAgentResourceDto MapResource(AiAgentResource r) =>
        new(r.Id, r.AgentId, r.Name, r.ResourceType, r.Detail, r.FileUrl, r.FileName, r.SortOrder);

    private static AiAgentPromptDto MapPrompt(AiAgentPrompt p) =>
        new(p.Id, p.AgentId, p.Name, p.Rule, p.Body, p.SortOrder);
}
