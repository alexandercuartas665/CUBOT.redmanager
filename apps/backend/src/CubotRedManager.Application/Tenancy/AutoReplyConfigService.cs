using CubotRedManager.Application.Abstractions;
using CubotRedManager.Domain.Entities;
using CubotRedManager.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace CubotRedManager.Application.Tenancy;

public sealed class AutoReplyConfigService : IAutoReplyConfigService
{
    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly IAuditWriter _audit;

    public AutoReplyConfigService(IApplicationDbContext db, ITenantContext tenantContext, IAuditWriter audit)
    {
        _db = db;
        _tenantContext = tenantContext;
        _audit = audit;
    }

    public async Task<AutoReplyConfigDto> GetOrDefaultAsync(Guid socialAccountId, CancellationToken cancellationToken = default)
    {
        var cfg = await _db.AutoReplyConfigs.AsNoTracking()
            .FirstOrDefaultAsync(c => c.SocialAccountId == socialAccountId, cancellationToken);
        if (cfg is null)
        {
            // Defaults sugeridos en el prototipo
            return new AutoReplyConfigDto(
                null, socialAccountId, false, AutoReplyMode.Mixed, 20, 3, 10, null,
                AutoReplyFrequency.Every30m, "*/30 * * * *", 0x1FFF00, 0x7F, null,
                null,
                false, null, AutoReplySummaryTargetType.Phone, null, null,
                Array.Empty<AutoReplyTemplateDto>());
        }
        var templates = await _db.AutoReplyTemplates.AsNoTracking()
            .Where(t => t.ConfigId == cfg.Id)
            .OrderBy(t => t.SortOrder)
            .Select(t => new AutoReplyTemplateDto(t.Id, t.Keywords, t.Body, t.SortOrder))
            .ToListAsync(cancellationToken);
        return Map(cfg, templates);
    }

    public async Task<AutoReplyConfigDto?> SaveAsync(SaveAutoReplyConfigRequest request, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId) { return null; }

        // Validaciones duras (regla del vault).
        if (request.DelayMinSeconds < 2) { throw new InvalidOperationException("DelayMin debe ser >= 2 segundos (anti-bot)."); }
        if (request.DelayMaxSeconds < request.DelayMinSeconds) { throw new InvalidOperationException("DelayMax debe ser >= DelayMin."); }
        if (request.MaxRepliesPerRun is < 1 or > 200) { throw new InvalidOperationException("MaxRepliesPerRun debe estar entre 1 y 200."); }

        var account = await _db.SocialAccounts.FirstOrDefaultAsync(a => a.Id == request.SocialAccountId, cancellationToken);
        if (account is null) { return null; }

        var cfg = await _db.AutoReplyConfigs
            .Include(c => c.Templates)
            .FirstOrDefaultAsync(c => c.SocialAccountId == request.SocialAccountId, cancellationToken);

        if (cfg is null)
        {
            cfg = new AutoReplyConfig { TenantId = tenantId, SocialAccountId = request.SocialAccountId };
            _db.AutoReplyConfigs.Add(cfg);
        }

        cfg.IsActive = request.IsActive;
        cfg.Mode = request.Mode;
        cfg.MaxRepliesPerRun = request.MaxRepliesPerRun;
        cfg.DelayMinSeconds = request.DelayMinSeconds;
        cfg.DelayMaxSeconds = request.DelayMaxSeconds;
        cfg.BlacklistKeywords = string.IsNullOrWhiteSpace(request.BlacklistKeywords) ? null : request.BlacklistKeywords.Trim();
        cfg.Frequency = request.Frequency;
        cfg.CronCustom = string.IsNullOrWhiteSpace(request.CronCustom) ? null : request.CronCustom.Trim();
        cfg.ActiveHoursMask = request.ActiveHoursMask;
        cfg.ActiveDaysOfWeekMask = request.ActiveDaysOfWeekMask;
        cfg.DefaultTemplate = string.IsNullOrWhiteSpace(request.DefaultTemplate) ? null : request.DefaultTemplate.Trim();
        cfg.AiAgentId = request.AiAgentId;
        cfg.SummaryEnabled = request.SummaryEnabled;
        cfg.SummaryLineId = request.SummaryLineId;
        cfg.SummaryTargetType = request.SummaryTargetType;
        cfg.SummaryTarget = string.IsNullOrWhiteSpace(request.SummaryTarget) ? null : request.SummaryTarget.Trim();
        cfg.SummaryTemplate = string.IsNullOrWhiteSpace(request.SummaryTemplate) ? null : request.SummaryTemplate;

        // Reemplazar plantillas (full sync).
        foreach (var existing in cfg.Templates.ToList())
        {
            _db.AutoReplyTemplates.Remove(existing);
        }
        var order = 0;
        foreach (var t in request.Templates)
        {
            if (string.IsNullOrWhiteSpace(t.Keywords) || string.IsNullOrWhiteSpace(t.Body)) { continue; }
            _db.AutoReplyTemplates.Add(new AutoReplyTemplate
            {
                TenantId = tenantId,
                Config = cfg,
                Keywords = t.Keywords.Trim(),
                Body = t.Body.Trim(),
                SortOrder = t.SortOrder == 0 ? order : t.SortOrder
            });
            order++;
        }

        _audit.Write(actorUserId, "autoreply.save", nameof(AutoReplyConfig), cfg.Id,
            previousValue: null,
            newValue: new { cfg.SocialAccountId, cfg.IsActive, cfg.Mode, TemplateCount = request.Templates.Count },
            tenantId: tenantId);
        await _db.SaveChangesAsync(cancellationToken);

        return await GetOrDefaultAsync(request.SocialAccountId, cancellationToken);
    }

    public async Task<bool> ToggleActiveAsync(Guid socialAccountId, bool isActive, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId) { return false; }
        var cfg = await _db.AutoReplyConfigs.FirstOrDefaultAsync(c => c.SocialAccountId == socialAccountId, cancellationToken);
        if (cfg is null)
        {
            // Si no existe, crear con defaults y el flag.
            cfg = new AutoReplyConfig { TenantId = tenantId, SocialAccountId = socialAccountId, IsActive = isActive };
            _db.AutoReplyConfigs.Add(cfg);
        }
        else
        {
            cfg.IsActive = isActive;
        }
        _audit.Write(actorUserId, "autoreply.toggle", nameof(AutoReplyConfig), cfg.Id,
            previousValue: null, newValue: new { isActive }, tenantId: tenantId);
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<IReadOnlyList<AutoReplyJobLogDto>> ListLogsAsync(Guid? socialAccountId = null, AutoReplyJobStatus? status = null, int take = 100, CancellationToken cancellationToken = default)
    {
        var q = from l in _db.AutoReplyJobLogs.AsNoTracking()
                join a in _db.SocialAccounts.AsNoTracking() on l.SocialAccountId equals a.Id
                join c in _db.Clients.AsNoTracking() on a.ClientId equals c.Id
                select new { l, a.Handle, ClientName = c.Name };
        if (socialAccountId is { } sid) { q = q.Where(x => x.l.SocialAccountId == sid); }
        if (status is { } s) { q = q.Where(x => x.l.Status == s); }
        var rows = await q.OrderByDescending(x => x.l.StartedAt).Take(take).ToListAsync(cancellationToken);
        return rows.Select(x => new AutoReplyJobLogDto(
            x.l.Id, x.l.SocialAccountId, x.Handle, x.ClientName, x.l.Status,
            x.l.StartedAt, x.l.DurationMs, x.l.Processed, x.l.Replied, x.l.Errors, x.l.Omitted)).ToList();
    }

    public async Task<AutoReplyJobLogDetailDto?> GetLogDetailAsync(Guid logId, CancellationToken cancellationToken = default)
    {
        var row = await (from l in _db.AutoReplyJobLogs.AsNoTracking()
                         join a in _db.SocialAccounts.AsNoTracking() on l.SocialAccountId equals a.Id
                         join c in _db.Clients.AsNoTracking() on a.ClientId equals c.Id
                         where l.Id == logId
                         select new { l, a.Handle, ClientName = c.Name }).FirstOrDefaultAsync(cancellationToken);
        if (row is null) { return null; }
        var dto = new AutoReplyJobLogDto(row.l.Id, row.l.SocialAccountId, row.Handle, row.ClientName, row.l.Status,
            row.l.StartedAt, row.l.DurationMs, row.l.Processed, row.l.Replied, row.l.Errors, row.l.Omitted);
        return new AutoReplyJobLogDetailDto(dto, row.l.Trace);
    }

    /// <summary>
    /// Borrado masivo de logs del tenant activo. El filtro global de TenantId aplica via
    /// HasQueryFilter, asi que solo afecta los logs del tenant que esta haciendo la peticion.
    /// La purga automatica por retencion (5 dias por defecto) la hace el worker; este metodo
    /// es el boton manual de la UI "Eliminar logs".
    /// </summary>
    public async Task<int> DeleteAllLogsAsync(Guid actorUserId, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId) { return 0; }
        var count = await _db.AutoReplyJobLogs
            .Where(l => l.TenantId == tenantId)
            .ExecuteDeleteAsync(cancellationToken);
        _audit.Write(actorUserId, "auto-reply.logs.delete-all", nameof(AutoReplyJobLog), Guid.Empty,
            previousValue: new { count }, newValue: null, tenantId: tenantId);
        return count;
    }

    private static AutoReplyConfigDto Map(AutoReplyConfig c, IReadOnlyList<AutoReplyTemplateDto> templates) =>
        new(c.Id, c.SocialAccountId, c.IsActive, c.Mode, c.MaxRepliesPerRun,
            c.DelayMinSeconds, c.DelayMaxSeconds, c.BlacklistKeywords, c.Frequency, c.CronCustom,
            c.ActiveHoursMask, c.ActiveDaysOfWeekMask, c.DefaultTemplate, c.AiAgentId,
            c.SummaryEnabled, c.SummaryLineId, c.SummaryTargetType, c.SummaryTarget, c.SummaryTemplate,
            templates);

    public async Task<IReadOnlyList<AutoReplyAgentOptionDto>> ListAgentOptionsAsync(CancellationToken cancellationToken = default)
    {
        return await _db.AiAgents.AsNoTracking()
            .OrderByDescending(a => a.IsActive).ThenBy(a => a.Name)
            .Select(a => new AutoReplyAgentOptionDto(a.Id, a.Name, a.Provider, a.IsActive))
            .ToListAsync(cancellationToken);
    }
}
