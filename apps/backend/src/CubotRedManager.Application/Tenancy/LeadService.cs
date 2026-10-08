using System.Text.Json;
using CubotRedManager.Application.Abstractions;
using CubotRedManager.Domain.Entities;
using CubotRedManager.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace CubotRedManager.Application.Tenancy;

/// <summary>
/// Leads del embudo. Adaptado desde CUBOT.travels:
///   - Multi-pipeline: todos los metodos validan que Stage pertenece al Pipeline del lead.
///   - LeadFile en BD (bytea), no en disco (Railway-safe — patron PublicationMedia).
///   - Sin IChatBroadcaster (en redmanager el refresh es StateHasChanged del propio Blazor).
///   - Sin IItineraryService (travel-specific).
///   - Visibilidad: todos los TenantMembers ven todos los leads (sin OwnOnly por ahora).
///     Purge solo Owner/Admin (destructivo).
/// </summary>
public sealed class LeadService : ILeadService
{
    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly TimeProvider _timeProvider;

    public LeadService(IApplicationDbContext db, ITenantContext tenantContext, TimeProvider timeProvider)
    {
        _db = db;
        _tenantContext = tenantContext;
        _timeProvider = timeProvider;
    }

    public async Task<IReadOnlyList<LeadDto>> ListAsync(Guid? pipelineId = null, Guid? stageId = null, CancellationToken cancellationToken = default)
    {
        var q = _db.Leads.AsNoTracking().Where(l => l.ArchivedAt == null);
        if (pipelineId is Guid pid) { q = q.Where(l => l.PipelineId == pid); }
        if (stageId is Guid sid) { q = q.Where(l => l.StageId == sid); }
        var rows = await q.OrderByDescending(l => l.StageChangedAt).ToListAsync(cancellationToken);
        return rows.Select(Map).ToList();
    }

    public async Task<LeadDetailDto?> GetAsync(Guid leadId, CancellationToken cancellationToken = default)
    {
        var lead = await _db.Leads.AsNoTracking().FirstOrDefaultAsync(l => l.Id == leadId, cancellationToken);
        if (lead is null) { return null; }

        var activities = await _db.LeadActivities.AsNoTracking()
            .Where(a => a.LeadId == leadId)
            .OrderBy(a => a.CreatedAt)
            .Select(a => new LeadActivityDto(a.Id, a.ActivityType, a.Description, a.CreatedAt,
                _db.PlatformUsers.Where(p => p.Id == a.CreatedBy).Select(p => p.DisplayName ?? p.Email).FirstOrDefault()))
            .ToListAsync(cancellationToken);

        return new LeadDetailDto(Map(lead), activities);
    }

    public async Task<LeadDto?> CreateAsync(CreateLeadRequest request, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId) { return null; }

        var pipeline = await _db.Pipelines.FirstOrDefaultAsync(p => p.Id == request.PipelineId, cancellationToken);
        if (pipeline is null) { return null; }

        // Stage destino: la indicada (debe pertenecer al pipeline) o la primera del pipeline.
        PipelineStage? stage;
        if (request.StageId is Guid stageId)
        {
            stage = await _db.PipelineStages.FirstOrDefaultAsync(s => s.Id == stageId && s.PipelineId == pipeline.Id, cancellationToken);
            if (stage is null) { return null; } // stage no pertenece a este pipeline
        }
        else
        {
            stage = await _db.PipelineStages
                .Where(s => s.PipelineId == pipeline.Id)
                .OrderBy(s => s.SortOrder).FirstOrDefaultAsync(cancellationToken);
        }
        if (stage is null) { return null; }

        // El lead queda asignado al asesor que lo crea (para visibilidad personal futura).
        Guid? assignedTo = null;
        if (_tenantContext.UserId is Guid creatorUserId)
        {
            assignedTo = await _db.TenantUsers
                .Where(tu => tu.PlatformUserId == creatorUserId)
                .Select(tu => (Guid?)tu.Id)
                .FirstOrDefaultAsync(cancellationToken);
        }

        var now = _timeProvider.GetUtcNow();
        var lead = new Lead
        {
            TenantId = tenantId,
            PipelineId = pipeline.Id,
            StageId = stage.Id,
            ContactName = request.ContactName.Trim(),
            ContactPhone = request.ContactPhone?.Trim(),
            Topic = request.Topic?.Trim(),
            EstimatedValue = request.EstimatedValue,
            Currency = request.Currency?.Trim(),
            Status = LeadStatus.Open,
            StageChangedAt = now,
            AssignedToTenantUserId = assignedTo,
            AttentionDurationSeconds = request.AttentionDurationSeconds
        };

        if (request.FieldValues is { Count: > 0 })
        {
            var clean = request.FieldValues
                .Where(kv => !string.IsNullOrWhiteSpace(kv.Value))
                .ToDictionary(kv => kv.Key, kv => kv.Value);
            if (clean.Count > 0) { lead.FieldValuesJson = JsonSerializer.Serialize(clean); }
        }

        _db.Leads.Add(lead);
        AddActivity(tenantId, lead.Id, "lead.created", $"Lead creado en {pipeline.Name} / {stage.Name}");
        await _db.SaveChangesAsync(cancellationToken);
        return Map(lead);
    }

    public async Task<LeadDto?> UpdateAsync(Guid leadId, UpdateLeadRequest request, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var lead = await _db.Leads.FirstOrDefaultAsync(l => l.Id == leadId, cancellationToken);
        if (lead is null) { return null; }

        var previousPhoneDigits = PhoneDigits(lead.ContactPhone);

        lead.ContactName = request.ContactName.Trim();
        lead.ContactPhone = request.ContactPhone?.Trim();
        lead.Topic = request.Topic?.Trim();
        lead.EstimatedValue = request.EstimatedValue;
        lead.Currency = request.Currency?.Trim();

        var values = request.FieldValues ?? new Dictionary<string, string?>();
        var clean = values.Where(kv => !string.IsNullOrWhiteSpace(kv.Value))
                          .ToDictionary(kv => kv.Key, kv => kv.Value);
        lead.FieldValuesJson = clean.Count == 0 ? null : JsonSerializer.Serialize(clean);

        await SyncConversationPhoneAsync(lead, previousPhoneDigits, cancellationToken);
        AddActivity(lead.TenantId, lead.Id, "lead.updated", "Datos del lead actualizados");
        await _db.SaveChangesAsync(cancellationToken);
        return Map(lead);
    }

    public async Task<LeadDto?> MoveAsync(Guid leadId, MoveLeadRequest request, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var lead = await _db.Leads.FirstOrDefaultAsync(l => l.Id == leadId, cancellationToken);
        if (lead is null) { return null; }

        var stage = await _db.PipelineStages.FirstOrDefaultAsync(s => s.Id == request.StageId, cancellationToken);
        if (stage is null) { return null; }

        // Blindaje multi-pipeline: la stage destino DEBE pertenecer al mismo pipeline del lead.
        if (stage.PipelineId != lead.PipelineId) { return null; }

        if (lead.StageId != stage.Id)
        {
            lead.StageId = stage.Id;
            lead.StageChangedAt = _timeProvider.GetUtcNow();
            lead.Status = stage.IsClosedWon ? LeadStatus.Won : stage.IsClosedLost ? LeadStatus.Lost : LeadStatus.Open;
            lead.LossReason = stage.IsClosedLost ? request.LossReason?.Trim() : null;
            AddActivity(lead.TenantId, lead.Id, "lead.stage.changed",
                $"Movido a {stage.Name}" + (stage.IsClosedLost && !string.IsNullOrWhiteSpace(request.LossReason) ? $" (motivo: {request.LossReason})" : ""));
            await _db.SaveChangesAsync(cancellationToken);
        }
        return Map(lead);
    }

    public async Task<LeadDto?> AssignAsync(Guid leadId, Guid? tenantUserId, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var lead = await _db.Leads.FirstOrDefaultAsync(l => l.Id == leadId, cancellationToken);
        if (lead is null) { return null; }

        var label = "Lead sin asignar";
        if (tenantUserId is Guid userId)
        {
            // Buscar el email del usuario destino via PlatformUser (TenantUser NO tiene Email propio en redmanager).
            var email = await _db.TenantUsers.AsNoTracking()
                .Where(tu => tu.Id == userId)
                .Join(_db.PlatformUsers.AsNoTracking(), tu => tu.PlatformUserId, pu => pu.Id,
                      (tu, pu) => pu.Email).FirstOrDefaultAsync(cancellationToken);
            if (email is null) { return null; }
            label = $"Asignado a {email}";
        }

        lead.AssignedToTenantUserId = tenantUserId;
        AddActivity(lead.TenantId, lead.Id, "lead.assigned", label);
        await _db.SaveChangesAsync(cancellationToken);
        return Map(lead);
    }

    public async Task<bool> ArchiveAsync(Guid leadId, string reason, string? note, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var lead = await _db.Leads.FirstOrDefaultAsync(l => l.Id == leadId, cancellationToken);
        if (lead is null) { return false; }

        var actorName = await ResolveActorNameAsync(actorUserId, cancellationToken);
        var now = _timeProvider.GetUtcNow();
        lead.ArchivedAt = now;
        lead.ArchiveReason = string.IsNullOrWhiteSpace(reason) ? "Otro" : reason.Trim();
        lead.ArchiveNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        lead.ArchivedByName = actorName;

        // Al archivar: soltar link lead<->conversacion y resetear contexto del agente para que el
        // cliente, si vuelve a escribir, arranque fresh (sin reusar cache obsoleto del lead cerrado).
        var linkedConvs = await _db.Conversations
            .Where(c => c.TenantId == lead.TenantId && c.LeadId == lead.Id)
            .ToListAsync(cancellationToken);
        foreach (var c in linkedConvs)
        {
            c.AgentContextResetAt = now;
            c.LeadId = null;
            var staleCache = await _db.AiAgentCacheValues
                .Where(v => v.TenantId == lead.TenantId && v.SessionId == c.Id)
                .ToListAsync(cancellationToken);
            if (staleCache.Count > 0) { _db.AiAgentCacheValues.RemoveRange(staleCache); }
        }

        var desc = $"Enviado a historial - {lead.ArchiveReason}" + (lead.ArchiveNote is null ? "" : $": {lead.ArchiveNote}");
        AddActivity(lead.TenantId, lead.Id, "lead.archived", desc);
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<LeadDto?> UnarchiveAsync(Guid leadId, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var lead = await _db.Leads.FirstOrDefaultAsync(l => l.Id == leadId, cancellationToken);
        if (lead is null) { return null; }

        lead.ArchivedAt = null;
        lead.ArchiveReason = null;
        lead.ArchiveNote = null;
        lead.ArchivedByName = null;

        AddActivity(lead.TenantId, lead.Id, "lead.restored", "Regresado al tablero desde historial");
        await _db.SaveChangesAsync(cancellationToken);
        return Map(lead);
    }

    public async Task<IReadOnlyList<ArchivedLeadDto>> ListArchivedAsync(Guid? pipelineId = null, CancellationToken cancellationToken = default)
    {
        var q = _db.Leads.AsNoTracking().Where(l => l.ArchivedAt != null);
        if (pipelineId is Guid pid) { q = q.Where(l => l.PipelineId == pid); }

        var rows = await q
            .OrderByDescending(l => l.ArchivedAt)
            .Select(l => new
            {
                l.Id, l.ContactName, l.ContactPhone, l.Topic, l.EstimatedValue, l.Currency,
                l.ArchiveReason, l.ArchiveNote, l.ArchivedAt, l.ArchivedByName, l.AssignedToTenantUserId,
                l.PipelineId, l.StageId, l.Status, l.StageChangedAt, l.FieldValuesJson
            })
            .ToListAsync(cancellationToken);

        return rows.Select(r => new ArchivedLeadDto(r.Id, r.ContactName, r.ContactPhone, r.Topic,
            r.EstimatedValue, r.Currency, r.ArchiveReason, r.ArchiveNote, r.ArchivedAt, r.ArchivedByName,
            r.AssignedToTenantUserId, r.PipelineId, r.StageId, r.Status, r.StageChangedAt, DeserializeValues(r.FieldValuesJson))).ToList();
    }

    public async Task<PurgeArchivedResult?> PurgeArchivedHistoryAsync(CancellationToken cancellationToken = default)
    {
        if (_tenantContext.UserId is not Guid userId) { return null; }
        var me = await _db.TenantUsers.AsNoTracking()
            .FirstOrDefaultAsync(tu => tu.PlatformUserId == userId, cancellationToken);
        if (me is null || me.TenantRole is not (TenantRole.Owner or TenantRole.Admin)) { return null; }

        var ids = await _db.Leads
            .Where(l => l.ArchivedAt != null)
            .Select(l => l.Id)
            .ToListAsync(cancellationToken);
        if (ids.Count == 0) { return new PurgeArchivedResult(0); }

        // Hijos primero (cascade declarado, pero por explicitud y para liberar FK en Conversation).
        await _db.LeadNotes.Where(n => ids.Contains(n.LeadId)).ExecuteDeleteAsync(cancellationToken);
        await _db.LeadActivities.Where(a => ids.Contains(a.LeadId)).ExecuteDeleteAsync(cancellationToken);
        await _db.LeadFiles.Where(f => ids.Contains(f.LeadId)).ExecuteDeleteAsync(cancellationToken);
        await _db.FollowUpTasks.Where(t => ids.Contains(t.LeadId)).ExecuteDeleteAsync(cancellationToken);
        await _db.Conversations.Where(c => c.LeadId != null && ids.Contains(c.LeadId.Value))
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.LeadId, (Guid?)null), cancellationToken);
        var deleted = await _db.Leads.Where(l => ids.Contains(l.Id)).ExecuteDeleteAsync(cancellationToken);
        return new PurgeArchivedResult(deleted);
    }

    // === Notas ===========================================================================

    public async Task<IReadOnlyList<LeadNoteDto>> ListNotesAsync(Guid leadId, CancellationToken cancellationToken = default)
    {
        return await _db.LeadNotes.AsNoTracking()
            .Where(n => n.LeadId == leadId)
            .OrderByDescending(n => n.CreatedAt)
            .Select(n => new LeadNoteDto(n.Id, n.Content, n.Color, n.CreatedAt,
                _db.PlatformUsers.Where(p => p.Id == n.CreatedBy).Select(p => p.DisplayName ?? p.Email).FirstOrDefault()))
            .ToListAsync(cancellationToken);
    }

    public async Task<LeadNoteDto?> AddNoteAsync(Guid leadId, string content, string color, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(content)) { return null; }
        var lead = await _db.Leads.FirstOrDefaultAsync(l => l.Id == leadId, cancellationToken);
        if (lead is null) { return null; }

        var note = new LeadNote
        {
            TenantId = lead.TenantId,
            LeadId = leadId,
            Content = content.Trim(),
            Color = string.IsNullOrWhiteSpace(color) ? "yellow" : color.Trim()
        };
        _db.LeadNotes.Add(note);
        await _db.SaveChangesAsync(cancellationToken);
        var actorName = await ResolveActorNameAsync(actorUserId, cancellationToken);
        return new LeadNoteDto(note.Id, note.Content, note.Color, note.CreatedAt, actorName);
    }

    public async Task<bool> DeleteNoteAsync(Guid noteId, CancellationToken cancellationToken = default)
    {
        var note = await _db.LeadNotes.FirstOrDefaultAsync(n => n.Id == noteId, cancellationToken);
        if (note is null) { return false; }
        _db.LeadNotes.Remove(note);
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    // === Files (bytea en BD) =============================================================

    public async Task<IReadOnlyList<LeadFileDto>> ListFilesAsync(Guid leadId, CancellationToken cancellationToken = default)
    {
        // No cargamos Content aqui (bytea pesado). Solo metadata. El binario se sirve via
        // GET /api/leads/files/{id}.
        return await _db.LeadFiles.AsNoTracking()
            .Where(f => f.LeadId == leadId)
            .OrderByDescending(f => f.CreatedAt)
            .Select(f => new LeadFileDto(
                f.Id, f.FileName, $"/api/leads/files/{f.Id:D}", f.ContentType, f.SizeBytes, f.CreatedAt,
                _db.PlatformUsers.Where(p => p.Id == f.CreatedBy).Select(p => p.DisplayName ?? p.Email).FirstOrDefault()))
            .ToListAsync(cancellationToken);
    }

    public async Task<LeadFileDto?> AddFileAsync(Guid leadId, string fileName, byte[] content, string contentType, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(fileName) || content is null || content.Length == 0) { return null; }
        var lead = await _db.Leads.FirstOrDefaultAsync(l => l.Id == leadId, cancellationToken);
        if (lead is null) { return null; }

        var file = new LeadFile
        {
            TenantId = lead.TenantId,
            LeadId = leadId,
            FileName = fileName.Trim(),
            ContentType = string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType.Trim(),
            SizeBytes = content.Length,
            Content = content
        };
        _db.LeadFiles.Add(file);
        AddActivity(lead.TenantId, lead.Id, "lead.file.added", $"Archivo adjuntado: {file.FileName}");
        await _db.SaveChangesAsync(cancellationToken);

        var actorName = await ResolveActorNameAsync(actorUserId, cancellationToken);
        return new LeadFileDto(file.Id, file.FileName, $"/api/leads/files/{file.Id:D}",
            file.ContentType, file.SizeBytes, file.CreatedAt, actorName);
    }

    public async Task<bool> DeleteFileAsync(Guid fileId, CancellationToken cancellationToken = default)
    {
        var file = await _db.LeadFiles.FirstOrDefaultAsync(f => f.Id == fileId, cancellationToken);
        if (file is null) { return false; }
        _db.LeadFiles.Remove(file);
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    // === Helpers =========================================================================

    private async Task<string?> ResolveActorNameAsync(Guid actorUserId, CancellationToken ct)
    {
        if (actorUserId == Guid.Empty) { return null; }
        return await _db.PlatformUsers.AsNoTracking()
            .Where(p => p.Id == actorUserId)
            .Select(p => p.DisplayName ?? p.Email)
            .FirstOrDefaultAsync(ct);
    }

    private void AddActivity(Guid tenantId, Guid leadId, string type, string description)
    {
        _db.LeadActivities.Add(new LeadActivity
        {
            TenantId = tenantId,
            LeadId = leadId,
            ActivityType = type,
            Description = description
        });
    }

    /// <summary>Si al editar un lead cambia su telefono, movemos la conversacion WhatsApp del lead
    /// para que el agente siga escribiendo al numero actual (si ya hay otra conversacion con el
    /// numero nuevo, religamos el lead a esa y liberamos la vieja).</summary>
    private async Task SyncConversationPhoneAsync(Lead lead, string previousPhoneDigits, CancellationToken ct)
    {
        var newDigits = PhoneDigits(lead.ContactPhone);
        if (string.IsNullOrEmpty(newDigits) || newDigits == previousPhoneDigits) { return; }

        var linked = await _db.Conversations.FirstOrDefaultAsync(c => c.LeadId == lead.Id, ct);
        var withNewNumber = await _db.Conversations.FirstOrDefaultAsync(c => c.ContactPhone == newDigits, ct);

        if (withNewNumber is not null)
        {
            if (linked is not null && linked.Id != withNewNumber.Id) { linked.LeadId = null; }
            withNewNumber.LeadId = lead.Id;
            if (string.IsNullOrWhiteSpace(withNewNumber.ContactName)) { withNewNumber.ContactName = lead.ContactName; }
        }
        else if (linked is not null)
        {
            linked.ContactPhone = newDigits;
        }
    }

    private static string PhoneDigits(string? s) => string.IsNullOrEmpty(s) ? string.Empty : new string(s.Where(char.IsDigit).ToArray());

    private static LeadDto Map(Lead l) =>
        new(l.Id, l.ContactName, l.ContactPhone, l.Topic, l.EstimatedValue, l.Currency,
            l.PipelineId, l.StageId, l.Status, l.AssignedToTenantUserId, l.StageChangedAt,
            DeserializeValues(l.FieldValuesJson), l.AttentionDurationSeconds, l.CreatedAt);

    private static IReadOnlyDictionary<string, string?> DeserializeValues(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) { return new Dictionary<string, string?>(); }
        try { return JsonSerializer.Deserialize<Dictionary<string, string?>>(json) ?? new Dictionary<string, string?>(); }
        catch { return new Dictionary<string, string?>(); }
    }
}
