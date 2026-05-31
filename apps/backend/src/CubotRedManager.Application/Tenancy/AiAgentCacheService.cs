using System.Globalization;
using System.Text;
using CubotRedManager.Application.Abstractions;
using CubotRedManager.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CubotRedManager.Application.Tenancy;

public sealed class AiAgentCacheService : IAiAgentCacheService
{
    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly IAuditWriter _audit;

    public AiAgentCacheService(IApplicationDbContext db, ITenantContext tenantContext, IAuditWriter audit)
    {
        _db = db;
        _tenantContext = tenantContext;
        _audit = audit;
    }

    public async Task<IReadOnlyList<AiAgentCacheFieldDto>> ListFieldsAsync(Guid agentId, CancellationToken cancellationToken = default)
    {
        return await _db.AiAgentCacheFields.AsNoTracking()
            .Where(f => f.AgentId == agentId)
            .OrderBy(f => f.SortOrder).ThenBy(f => f.Label)
            .Select(f => new AiAgentCacheFieldDto(f.Id, f.AgentId, f.FieldKey, f.Label, f.Description, f.SortOrder, f.IsUpdatable))
            .ToListAsync(cancellationToken);
    }

    public async Task<AiAgentCacheFieldDto?> CreateFieldAsync(CreateAgentCacheFieldRequest request, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId) { return null; }
        var agent = await _db.AiAgents.FirstOrDefaultAsync(a => a.Id == request.AgentId, cancellationToken);
        if (agent is null) { return null; }

        var label = (request.Label ?? "Dato").Trim();
        if (label.Length == 0) { return null; }

        var existing = await _db.AiAgentCacheFields.Where(f => f.AgentId == request.AgentId)
            .Select(f => f.FieldKey).ToListAsync(cancellationToken);
        var key = EnsureUniqueKey(Slugify(label), existing);

        var nextOrder = (await _db.AiAgentCacheFields.Where(f => f.AgentId == request.AgentId)
            .Select(f => (int?)f.SortOrder).MaxAsync(cancellationToken) ?? -1) + 1;

        var field = new AiAgentCacheField
        {
            TenantId = tenantId,
            AgentId = request.AgentId,
            FieldKey = key,
            Label = label,
            Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
            SortOrder = nextOrder,
            IsUpdatable = request.IsUpdatable
        };
        _db.AiAgentCacheFields.Add(field);
        _audit.Write(actorUserId, "ai-agent.cache-field.create", nameof(AiAgentCacheField), field.Id,
            previousValue: null, newValue: new { request.AgentId, field.FieldKey, field.Label }, tenantId: tenantId);
        await _db.SaveChangesAsync(cancellationToken);
        return new AiAgentCacheFieldDto(field.Id, field.AgentId, field.FieldKey, field.Label, field.Description, field.SortOrder, field.IsUpdatable);
    }

    public async Task<AiAgentCacheFieldDto?> UpdateFieldAsync(Guid fieldId, UpdateAgentCacheFieldRequest request, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var field = await _db.AiAgentCacheFields.FirstOrDefaultAsync(f => f.Id == fieldId, cancellationToken);
        if (field is null) { return null; }
        var label = (request.Label ?? field.Label).Trim();
        if (label.Length == 0) { return null; }
        field.Label = label;
        field.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        field.IsUpdatable = request.IsUpdatable;
        await _db.SaveChangesAsync(cancellationToken);
        return new AiAgentCacheFieldDto(field.Id, field.AgentId, field.FieldKey, field.Label, field.Description, field.SortOrder, field.IsUpdatable);
    }

    public async Task<bool> DeleteFieldAsync(Guid fieldId, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var field = await _db.AiAgentCacheFields.FirstOrDefaultAsync(f => f.Id == fieldId, cancellationToken);
        if (field is null) { return false; }
        var orphans = _db.AiAgentCacheValues.Where(v => v.AgentId == field.AgentId && v.FieldKey == field.FieldKey);
        _db.AiAgentCacheValues.RemoveRange(orphans);
        _db.AiAgentCacheFields.Remove(field);
        _audit.Write(actorUserId, "ai-agent.cache-field.delete", nameof(AiAgentCacheField), field.Id,
            previousValue: new { field.FieldKey, field.Label }, newValue: null, tenantId: field.TenantId);
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<IReadOnlyList<AiAgentCacheValueDto>> GetValuesAsync(Guid agentId, Guid sessionId, CancellationToken cancellationToken = default)
    {
        var fields = await _db.AiAgentCacheFields.AsNoTracking()
            .Where(f => f.AgentId == agentId)
            .OrderBy(f => f.SortOrder).ThenBy(f => f.Label)
            .ToListAsync(cancellationToken);
        if (fields.Count == 0) { return Array.Empty<AiAgentCacheValueDto>(); }

        var values = await _db.AiAgentCacheValues.AsNoTracking()
            .Where(v => v.AgentId == agentId && v.SessionId == sessionId)
            .ToDictionaryAsync(v => v.FieldKey, cancellationToken);

        return fields.Select(f =>
        {
            values.TryGetValue(f.FieldKey, out var v);
            return new AiAgentCacheValueDto(f.FieldKey, f.Label, f.Description, v?.Value, v?.Source, v?.UpdatedAt);
        }).ToList();
    }

    public async Task<AiAgentCacheValueDto?> SetValueAsync(SetAgentCacheValueRequest request, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId) { return null; }
        var field = await _db.AiAgentCacheFields.AsNoTracking()
            .FirstOrDefaultAsync(f => f.AgentId == request.AgentId && f.FieldKey == request.FieldKey, cancellationToken);
        if (field is null) { return null; }

        var entry = await _db.AiAgentCacheValues.FirstOrDefaultAsync(
            v => v.AgentId == request.AgentId && v.SessionId == request.SessionId && v.FieldKey == request.FieldKey,
            cancellationToken);
        if (entry is null)
        {
            entry = new AiAgentCacheValue
            {
                TenantId = tenantId,
                AgentId = request.AgentId,
                SessionId = request.SessionId,
                FieldKey = request.FieldKey,
                Value = request.Value,
                Source = request.Source
            };
            _db.AiAgentCacheValues.Add(entry);
        }
        else
        {
            if (!field.IsUpdatable && !string.IsNullOrWhiteSpace(entry.Value))
            {
                return new AiAgentCacheValueDto(field.FieldKey, field.Label, field.Description, entry.Value, entry.Source, entry.UpdatedAt);
            }
            entry.Value = request.Value;
            entry.Source = request.Source;
        }
        await _db.SaveChangesAsync(cancellationToken);
        return new AiAgentCacheValueDto(field.FieldKey, field.Label, field.Description, entry.Value, entry.Source, entry.UpdatedAt);
    }

    public async Task<int> BulkSetFieldsUpdatableAsync(Guid agentId, bool isUpdatable, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var fields = await _db.AiAgentCacheFields
            .Where(f => f.AgentId == agentId && f.IsUpdatable != isUpdatable)
            .ToListAsync(cancellationToken);
        if (fields.Count == 0) { return 0; }
        foreach (var f in fields) { f.IsUpdatable = isUpdatable; }
        _audit.Write(actorUserId, "ai-agent.cache-field.bulk-set-updatable", nameof(AiAgentCacheField), agentId,
            previousValue: null, newValue: new { isUpdatable, count = fields.Count }, tenantId: _tenantContext.TenantId);
        await _db.SaveChangesAsync(cancellationToken);
        return fields.Count;
    }

    public async Task<int> ClearValuesAsync(Guid agentId, Guid sessionId, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var values = await _db.AiAgentCacheValues
            .Where(v => v.AgentId == agentId && v.SessionId == sessionId)
            .ToListAsync(cancellationToken);
        if (values.Count == 0) { return 0; }
        _db.AiAgentCacheValues.RemoveRange(values);
        _audit.Write(actorUserId, "ai-agent.cache.clear", nameof(AiAgentCacheValue), agentId,
            previousValue: new { count = values.Count, sessionId }, newValue: null, tenantId: _tenantContext.TenantId);
        await _db.SaveChangesAsync(cancellationToken);
        return values.Count;
    }

    private static string Slugify(string label)
    {
        var normalized = label.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        foreach (var c in normalized)
        {
            var cat = CharUnicodeInfo.GetUnicodeCategory(c);
            if (cat == UnicodeCategory.NonSpacingMark) continue;
            if (char.IsLetterOrDigit(c)) sb.Append(c);
            else if (sb.Length > 0 && sb[^1] != '_') sb.Append('_');
        }
        var slug = sb.ToString().Trim('_');
        return string.IsNullOrEmpty(slug) ? "dato" : slug;
    }

    private static string EnsureUniqueKey(string baseKey, IReadOnlyCollection<string> existingKeys)
    {
        if (!existingKeys.Contains(baseKey)) { return baseKey; }
        var i = 2;
        while (existingKeys.Contains($"{baseKey}_{i}")) { i++; }
        return $"{baseKey}_{i}";
    }
}
