using System.Text;
using System.Text.Json;
using CubotRedManager.Application.Abstractions;
using CubotRedManager.Domain.Entities;
using CubotRedManager.Domain.Enums;
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
        // Proyeccion EXPLICITA (no MapResource): asi EF genera SELECT id,name,resource_type,...
        // sin traer file_content (bytea potencialmente grande). Y hace la query robusta ante
        // instancias intermedias donde la migracion de file_content aun no se aplico.
        var resources = await _db.AiAgentResources.AsNoTracking()
            .Where(r => r.AgentId == id)
            .OrderBy(r => r.SortOrder)
            .Select(r => new AiAgentResourceDto(
                r.Id, r.AgentId, r.Name, r.ResourceType, r.Detail, r.FileUrl, r.FileName, r.SortOrder))
            .ToListAsync(cancellationToken);
        var prompts = await _db.AiAgentPrompts.AsNoTracking()
            .Where(p => p.AgentId == id)
            .OrderBy(p => p.SortOrder)
            .Select(p => new AiAgentPromptDto(p.Id, p.AgentId, p.Name, p.Rule, p.Body, p.SortOrder))
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

        // Desasociar antes de borrar: la FK ai_agent_id en auto_reply_configs esta configurada como
        // RESTRICT (default EF Core), asi que un DELETE directo con configs referenciando falla con
        // "violates foreign key constraint". Como el campo es nullable, lo ponemos a NULL — el
        // AutoReplyWorker interpreta null como "usa el primer agente activo del tenant" (fallback
        // definido en la UI del modal). No hay perdida operativa.
        var referencing = await _db.AutoReplyConfigs
            .Where(c => c.AiAgentId == id)
            .ToListAsync(cancellationToken);
        foreach (var cfg in referencing) { cfg.AiAgentId = null; }

        _db.AiAgents.Remove(agent);
        _audit.Write(actorUserId, "ai-agent.delete", nameof(AiAgent), agent.Id,
            previousValue: new { agent.Name, UnlinkedConfigs = referencing.Count }, newValue: null, tenantId: agent.TenantId);
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<AiAgentResourceDto?> AddResourceAsync(CreateAgentResourceRequest request, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId) { return null; }
        var agent = await _db.AiAgents.FirstOrDefaultAsync(a => a.Id == request.AgentId, cancellationToken);
        if (agent is null) { return null; }
        var nextOrder = (await _db.AiAgentResources.Where(r => r.AgentId == request.AgentId).Select(r => (int?)r.SortOrder).MaxAsync(cancellationToken) ?? -1) + 1;
        var hasBinary = request.FileContent is { Length: > 0 };
        var res = new AiAgentResource
        {
            TenantId = tenantId,
            AgentId = request.AgentId,
            Name = (request.Name ?? "Recurso").Trim(),
            ResourceType = request.ResourceType,
            Detail = request.Detail,
            FileName = request.FileName,
            FileContent = hasBinary ? request.FileContent : null,
            FileMimeType = hasBinary ? request.FileMimeType : null,
            // Si vienen bytes: la URL sera /api/agent-resources/{id}/file (rellenada al conocer el Id).
            // Si viene una URL externa (import antiguo): se conserva. Si no hay ninguno: null.
            FileUrl = hasBinary ? null : request.FileUrl,
            SortOrder = nextOrder
        };
        _db.AiAgentResources.Add(res);
        await _db.SaveChangesAsync(cancellationToken);
        if (hasBinary)
        {
            res.FileUrl = $"/api/agent-resources/{res.Id}/file";
            await _db.SaveChangesAsync(cancellationToken);
        }
        return MapResource(res);
    }

    public async Task<AiAgentResourceDto?> UpdateResourceAsync(Guid id, UpdateAgentResourceRequest request, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var res = await _db.AiAgentResources.FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (res is null) { return null; }
        res.Name = (request.Name ?? res.Name).Trim();
        res.ResourceType = request.ResourceType;
        res.Detail = request.Detail;
        res.FileName = request.FileName;
        if (request.ClearFile)
        {
            res.FileContent = null;
            res.FileMimeType = null;
            res.FileUrl = null;
        }
        else if (request.FileContent is { Length: > 0 })
        {
            res.FileContent = request.FileContent;
            res.FileMimeType = request.FileMimeType;
            res.FileUrl = $"/api/agent-resources/{res.Id}/file";
        }
        else if (!string.IsNullOrWhiteSpace(request.FileUrl))
        {
            // Mantener o cambiar la URL externa sin tocar el binario.
            res.FileUrl = request.FileUrl;
        }
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

    public async Task<AgentExportResult?> ExportAsync(Guid agentId, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is null) { return null; }
        var agent = await _db.AiAgents.AsNoTracking().FirstOrDefaultAsync(a => a.Id == agentId, cancellationToken);
        if (agent is null) { return null; }
        var resources = await _db.AiAgentResources.AsNoTracking()
            .Where(r => r.AgentId == agentId).OrderBy(r => r.SortOrder).ToListAsync(cancellationToken);
        var prompts = await _db.AiAgentPrompts.AsNoTracking()
            .Where(p => p.AgentId == agentId).OrderBy(p => p.SortOrder).ToListAsync(cancellationToken);
        var cacheFields = await _db.AiAgentCacheFields.AsNoTracking()
            .Where(f => f.AgentId == agentId).OrderBy(f => f.SortOrder).ToListAsync(cancellationToken);

        var payload = new AgentExportPayload(
            Schema: 1,
            Agent: new AgentExportAgent(
                agent.Name, agent.Role, agent.Provider.ToString(), agent.Model,
                agent.SystemPrompt, agent.IsActive, agent.EnableDataContainerMcp, agent.SortOrder),
            Prompts: prompts.Select(p => new AgentExportPrompt(p.Name, p.Rule, p.Body, p.SortOrder)).ToList(),
            Resources: resources.Select(r => new AgentExportResource(
                r.Name, r.ResourceType.ToString(), r.Detail, r.FileUrl, r.FileName, r.SortOrder)).ToList(),
            CacheFields: cacheFields.Select(f => new AgentExportCacheField(
                f.FieldKey, f.Label, f.Description, f.SortOrder, f.IsUpdatable)).ToList());

        var opts = new JsonSerializerOptions { WriteIndented = true };
        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload, opts);
        return new AgentExportResult($"{Slugify(agent.Name)}.json", bytes);
    }

    public async Task<AgentImportResult> ImportAsync(byte[] jsonBytes, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId) { return new AgentImportResult(false, null, "No hay agencia activa."); }
        AgentExportPayload? payload;
        try
        {
            var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            payload = JsonSerializer.Deserialize<AgentExportPayload>(jsonBytes, opts);
        }
        catch (Exception ex) { return new AgentImportResult(false, null, $"JSON invalido: {ex.Message}"); }
        if (payload is null || payload.Agent is null)
        {
            return new AgentImportResult(false, null, "El archivo no contiene un agente valido.");
        }
        if (payload.Schema != 1)
        {
            return new AgentImportResult(false, null, $"Version de schema no soportada ({payload.Schema}). Este sistema entiende schema 1.");
        }

        // Nombre unico por tenant (case-insensitive). Rechaza si existe.
        var normalized = payload.Agent.Name?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return new AgentImportResult(false, null, "El agente no tiene nombre.");
        }
        var exists = await _db.AiAgents.AsNoTracking()
            .AnyAsync(a => a.Name.ToLower() == normalized.ToLower(), cancellationToken);
        if (exists)
        {
            return new AgentImportResult(false, null, $"Ya existe un agente llamado '{normalized}' en esta agencia. Renombralo antes de importar.");
        }

        var nextOrder = (await _db.AiAgents.Select(a => (int?)a.SortOrder).MaxAsync(cancellationToken) ?? -1) + 1;
        var agent = new AiAgent
        {
            TenantId = tenantId,
            Name = normalized,
            Role = payload.Agent.Role,
            Provider = Enum.TryParse<AiProvider>(payload.Agent.Provider, ignoreCase: true, out var prov) ? prov : AiProvider.Claude,
            Model = payload.Agent.Model,
            SystemPrompt = payload.Agent.SystemPrompt ?? "",
            IsActive = false, // Nunca se importa encendido: el operador debe revisarlo antes de activarlo.
            EnableDataContainerMcp = payload.Agent.EnableDataContainerMcp,
            SortOrder = nextOrder
        };
        _db.AiAgents.Add(agent);
        await _db.SaveChangesAsync(cancellationToken);

        if (payload.Prompts is not null)
        {
            foreach (var p in payload.Prompts)
            {
                _db.AiAgentPrompts.Add(new AiAgentPrompt
                {
                    TenantId = tenantId, AgentId = agent.Id,
                    Name = p.Name ?? "", Rule = p.Rule, Body = p.Body ?? "", SortOrder = p.SortOrder
                });
            }
        }
        if (payload.Resources is not null)
        {
            foreach (var r in payload.Resources)
            {
                // FileUrl del origen puede no existir en este servidor; lo dejamos como referencia
                // textual y el operador re-sube el archivo desde la UI si es necesario.
                _db.AiAgentResources.Add(new AiAgentResource
                {
                    TenantId = tenantId, AgentId = agent.Id,
                    Name = r.Name ?? "",
                    ResourceType = Enum.TryParse<AgentResourceType>(r.ResourceType, ignoreCase: true, out var rt) ? rt : AgentResourceType.Text,
                    Detail = r.Detail, FileUrl = null, FileName = r.FileName,
                    SortOrder = r.SortOrder
                });
            }
        }
        if (payload.CacheFields is not null)
        {
            foreach (var f in payload.CacheFields)
            {
                _db.AiAgentCacheFields.Add(new AiAgentCacheField
                {
                    TenantId = tenantId, AgentId = agent.Id,
                    FieldKey = f.FieldKey ?? "", Label = f.Label ?? "", Description = f.Description,
                    SortOrder = f.SortOrder, IsUpdatable = f.IsUpdatable
                });
            }
        }
        await _db.SaveChangesAsync(cancellationToken);
        _audit.Write(actorUserId, "ai-agent.import", nameof(AiAgent), agent.Id,
            previousValue: null, newValue: new { agent.Name, PromptCount = payload.Prompts?.Count ?? 0, ResourceCount = payload.Resources?.Count ?? 0 },
            tenantId: tenantId);
        return new AgentImportResult(true, agent.Id, null);
    }

    private static string Slugify(string name)
    {
        var sb = new StringBuilder();
        foreach (var ch in name.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(ch)) { sb.Append(ch); }
            else if (ch is ' ' or '-' or '_') { sb.Append('-'); }
        }
        var slug = sb.ToString().Trim('-');
        while (slug.Contains("--")) { slug = slug.Replace("--", "-"); }
        return string.IsNullOrEmpty(slug) ? "agente" : slug;
    }

    private static AiAgentDto Map(AiAgent a, int resourceCount) =>
        new(a.Id, a.Name, a.Role, a.Provider, a.Model, a.SystemPrompt, a.IsActive, a.EnableDataContainerMcp, a.SortOrder, resourceCount);

    private static AiAgentResourceDto MapResource(AiAgentResource r) =>
        new(r.Id, r.AgentId, r.Name, r.ResourceType, r.Detail, r.FileUrl, r.FileName, r.SortOrder);

    private static AiAgentPromptDto MapPrompt(AiAgentPrompt p) =>
        new(p.Id, p.AgentId, p.Name, p.Rule, p.Body, p.SortOrder);
}

// --- Schema del JSON export/import (v1) ---
internal sealed record AgentExportPayload(
    int Schema,
    AgentExportAgent? Agent,
    List<AgentExportPrompt>? Prompts,
    List<AgentExportResource>? Resources,
    List<AgentExportCacheField>? CacheFields);

internal sealed record AgentExportAgent(
    string Name, string? Role, string Provider, string? Model, string SystemPrompt,
    bool IsActive, bool EnableDataContainerMcp, int SortOrder);

internal sealed record AgentExportPrompt(string Name, string? Rule, string Body, int SortOrder);

internal sealed record AgentExportResource(
    string Name, string ResourceType, string? Detail, string? FileUrl, string? FileName, int SortOrder);

internal sealed record AgentExportCacheField(
    string FieldKey, string Label, string? Description, int SortOrder, bool IsUpdatable);
