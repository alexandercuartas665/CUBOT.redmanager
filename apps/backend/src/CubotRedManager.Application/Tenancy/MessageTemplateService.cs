using CubotRedManager.Application.Abstractions;
using CubotRedManager.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CubotRedManager.Application.Tenancy;

public sealed class MessageTemplateService : IMessageTemplateService
{
    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly IAuditWriter _audit;

    public MessageTemplateService(IApplicationDbContext db, ITenantContext tenantContext, IAuditWriter audit)
    {
        _db = db;
        _tenantContext = tenantContext;
        _audit = audit;
    }

    public async Task<IReadOnlyList<MessageTemplateDto>> ListAsync(string? search = null, CancellationToken ct = default)
    {
        var q = _db.MessageTemplates.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLower();
            q = q.Where(t => t.Name.ToLower().Contains(s) || t.Body.ToLower().Contains(s));
        }
        var rows = await q.OrderByDescending(t => t.UpdatedAt ?? t.CreatedAt).ToListAsync(ct);
        return rows.Select(Map).ToList();
    }

    public async Task<MessageTemplateDto?> SaveAsync(SaveMessageTemplateRequest request, Guid actorUserId, CancellationToken ct = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId) { return null; }
        var name = (request.Name ?? string.Empty).Trim();
        var body = (request.Body ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(body)) { return null; }

        var tags = string.IsNullOrWhiteSpace(request.Tags) ? null : NormalizeTags(request.Tags!);

        MessageTemplate? entity = null;
        if (request.Id is { } id)
        {
            entity = await _db.MessageTemplates.FirstOrDefaultAsync(t => t.Id == id, ct);
            if (entity is null) { return null; }
            entity.Name = name;
            entity.Body = body;
            entity.Tags = tags;
            _audit.Write(actorUserId, "message-template.update", nameof(MessageTemplate), entity.Id,
                previousValue: null, newValue: new { entity.Name }, tenantId: tenantId);
        }
        else
        {
            entity = new MessageTemplate
            {
                TenantId = tenantId,
                Name = name,
                Body = body,
                Tags = tags,
                AuthorTenantUserId = actorUserId == Guid.Empty ? null : actorUserId
            };
            _db.MessageTemplates.Add(entity);
            _audit.Write(actorUserId, "message-template.create", nameof(MessageTemplate), entity.Id,
                previousValue: null, newValue: new { entity.Name }, tenantId: tenantId);
        }

        await _db.SaveChangesAsync(ct);
        return Map(entity);
    }

    public async Task<bool> DeleteAsync(Guid id, Guid actorUserId, CancellationToken ct = default)
    {
        var entity = await _db.MessageTemplates.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (entity is null) { return false; }
        _db.MessageTemplates.Remove(entity);
        _audit.Write(actorUserId, "message-template.delete", nameof(MessageTemplate), entity.Id,
            previousValue: new { entity.Name }, newValue: null, tenantId: entity.TenantId);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    private static MessageTemplateDto Map(MessageTemplate t) => new(
        t.Id,
        t.Name,
        t.Body,
        t.Tags,
        t.UpdatedAt ?? t.CreatedAt,
        t.CreatedAt);

    private static string? NormalizeTags(string raw)
    {
        var parts = raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0) { return null; }
        var clean = parts
            .Select(p => p.ToLowerInvariant())
            .Where(p => p.Length > 0)
            .Distinct()
            .Take(12)
            .ToArray();
        return clean.Length == 0 ? null : string.Join(",", clean);
    }
}
