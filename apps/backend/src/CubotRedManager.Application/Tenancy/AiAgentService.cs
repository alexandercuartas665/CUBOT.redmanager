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
    private readonly ISecretProtector _protector;

    public AiAgentService(IApplicationDbContext db, ITenantContext tenantContext, IAuditWriter audit, ISecretProtector protector)
    {
        _db = db;
        _tenantContext = tenantContext;
        _audit = audit;
        _protector = protector;
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
        var payment = new AgentPaymentConfigDto(
            Enabled: agent.PaymentEnabled,
            UserId: agent.PaymentUserId,
            Country: agent.PaymentCountry,
            TokenPresent: !string.IsNullOrEmpty(agent.PaymentTokenEncrypted),
            TokenExpiresAt: agent.PaymentTokenExpiresAt,
            TokenLastVerifiedAt: agent.PaymentTokenLastVerifiedAt,
            CatalogContainerName: agent.PaymentCatalogContainerName,
            CatalogNameColumn: agent.PaymentCatalogNameColumn,
            CatalogProductIdColumn: agent.PaymentCatalogProductIdColumn,
            CatalogCountryColumn: agent.PaymentCatalogCountryColumn,
            ApiBaseUrl: agent.PaymentApiBaseUrl,
            ApiPathTemplate: agent.PaymentApiPathTemplate,
            ResponseUrlPath: agent.PaymentResponseUrlPath);
        return new AiAgentDetailDto(Map(agent, resources.Count), resources, prompts, payment);
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
            ReactionsEnabled = request.ReactionsEnabled,
            ReactionRatioN = Math.Max(0, request.ReactionRatioN),
            ReactionRatioM = Math.Max(1, request.ReactionRatioM),
            ReactionEmojis = string.IsNullOrWhiteSpace(request.ReactionEmojis) ? null : request.ReactionEmojis.Trim(),
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
        agent.ReactionsEnabled = request.ReactionsEnabled;
        agent.ReactionRatioN = Math.Max(0, request.ReactionRatioN);
        agent.ReactionRatioM = Math.Max(1, request.ReactionRatioM);
        agent.ReactionEmojis = string.IsNullOrWhiteSpace(request.ReactionEmojis) ? null : request.ReactionEmojis.Trim();
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
        new(a.Id, a.Name, a.Role, a.Provider, a.Model, a.SystemPrompt, a.IsActive, a.EnableDataContainerMcp, a.SortOrder, resourceCount,
            a.ReactionsEnabled, a.ReactionRatioN, a.ReactionRatioM, a.ReactionEmojis);

    private static AiAgentResourceDto MapResource(AiAgentResource r) =>
        new(r.Id, r.AgentId, r.Name, r.ResourceType, r.Detail, r.FileUrl, r.FileName, r.SortOrder);

    private static AiAgentPromptDto MapPrompt(AiAgentPrompt p) =>
        new(p.Id, p.AgentId, p.Name, p.Rule, p.Body, p.SortOrder);

    // ===== Pagos FUXION =====

    public async Task<AgentPaymentConfigDto?> SetPaymentConfigAsync(Guid agentId, SetAgentPaymentConfigRequest request, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var agent = await _db.AiAgents.FirstOrDefaultAsync(a => a.Id == agentId, cancellationToken);
        if (agent is null) { return null; }

        agent.PaymentEnabled = request.Enabled;
        agent.PaymentUserId = Trim(request.UserId);
        agent.PaymentCountry = Trim(request.Country)?.ToLowerInvariant();
        agent.PaymentCatalogContainerName = Trim(request.CatalogContainerName);
        agent.PaymentCatalogNameColumn = Trim(request.CatalogNameColumn);
        agent.PaymentCatalogProductIdColumn = Trim(request.CatalogProductIdColumn);
        agent.PaymentCatalogCountryColumn = Trim(request.CatalogCountryColumn);
        agent.PaymentApiBaseUrl = Trim(request.ApiBaseUrl);
        agent.PaymentApiPathTemplate = Trim(request.ApiPathTemplate);
        agent.PaymentResponseUrlPath = Trim(request.ResponseUrlPath);

        // Token: null=no tocar, ""=borrar, otro=reemplazar
        if (request.NewToken is not null)
        {
            var t = request.NewToken.Trim();
            if (t.Length == 0)
            {
                agent.PaymentTokenEncrypted = null;
                agent.PaymentTokenExpiresAt = null;
                agent.PaymentTokenLastVerifiedAt = null;
                agent.PaymentTokenExpiryNotifiedAt = null;
            }
            else
            {
                agent.PaymentTokenEncrypted = _protector.Protect(t);
                agent.PaymentTokenExpiresAt = TryReadJwtExpiration(t);
                agent.PaymentTokenLastVerifiedAt = null; // se marca en la primera verify-session exitosa
                // Token nuevo -> resetear dedupe de alertas para que la proxima vez que este
                // por expirar el worker vuelva a avisar (en vez de callarse por el flag viejo).
                agent.PaymentTokenExpiryNotifiedAt = null;
            }
        }

        // Auditoria SIN el token: solo cambios de metadata visible.
        _audit.Write(actorUserId, "ai-agent.payment-config.set", nameof(AiAgent), agent.Id,
            previousValue: null,
            newValue: new
            {
                agent.PaymentEnabled, agent.PaymentUserId, agent.PaymentCountry,
                TokenPresent = agent.PaymentTokenEncrypted is not null,
                agent.PaymentTokenExpiresAt,
                agent.PaymentCatalogContainerName, agent.PaymentCatalogNameColumn, agent.PaymentCatalogProductIdColumn
            },
            tenantId: agent.TenantId);
        await _db.SaveChangesAsync(cancellationToken);

        return new AgentPaymentConfigDto(
            Enabled: agent.PaymentEnabled,
            UserId: agent.PaymentUserId,
            Country: agent.PaymentCountry,
            TokenPresent: !string.IsNullOrEmpty(agent.PaymentTokenEncrypted),
            TokenExpiresAt: agent.PaymentTokenExpiresAt,
            TokenLastVerifiedAt: agent.PaymentTokenLastVerifiedAt,
            CatalogContainerName: agent.PaymentCatalogContainerName,
            CatalogNameColumn: agent.PaymentCatalogNameColumn,
            CatalogProductIdColumn: agent.PaymentCatalogProductIdColumn,
            CatalogCountryColumn: agent.PaymentCatalogCountryColumn,
            ApiBaseUrl: agent.PaymentApiBaseUrl,
            ApiPathTemplate: agent.PaymentApiPathTemplate,
            ResponseUrlPath: agent.PaymentResponseUrlPath);
    }

    public async Task<string?> GetDecryptedPaymentTokenAsync(Guid agentId, CancellationToken cancellationToken = default)
    {
        var ciphertext = await _db.AiAgents.AsNoTracking()
            .Where(a => a.Id == agentId)
            .Select(a => a.PaymentTokenEncrypted)
            .FirstOrDefaultAsync(cancellationToken);
        if (string.IsNullOrEmpty(ciphertext)) { return null; }
        try { return _protector.Unprotect(ciphertext); }
        catch { return null; } // Llave DataProtection rotada o token corrupto: tratar como ausente.
    }

    private static string? Trim(string? s)
        => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    /// <summary>Parsea la claim "exp" (unix seconds) del JWT sin validar firma. Solo se usa para
    /// mostrar "expira en X" en la UI y para alertas proactivas; la validez real la determina la
    /// API de FUXION al usar el token. Devuelve null si el token no es JWT o no tiene exp.</summary>
    private static DateTimeOffset? TryReadJwtExpiration(string token)
    {
        try
        {
            var parts = token.Split('.');
            if (parts.Length != 3) { return null; }
            var payload = parts[1];
            // base64url -> base64
            var padded = payload.Replace('-', '+').Replace('_', '/');
            switch (padded.Length % 4) { case 2: padded += "=="; break; case 3: padded += "="; break; }
            var json = Encoding.UTF8.GetString(Convert.FromBase64String(padded));
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("exp", out var exp)) { return null; }
            return DateTimeOffset.FromUnixTimeSeconds(exp.GetInt64());
        }
        catch { return null; }
    }
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
