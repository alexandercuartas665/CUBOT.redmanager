using CubotRedManager.Application.Abstractions;
using CubotRedManager.Domain.Entities;
using CubotRedManager.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace CubotRedManager.Application.Tenancy;

public sealed class TaskCardService : ITaskCardService
{
    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly IAuditWriter _audit;

    public TaskCardService(IApplicationDbContext db, ITenantContext tenantContext, IAuditWriter audit)
    {
        _db = db;
        _tenantContext = tenantContext;
        _audit = audit;
    }

    public async Task<IReadOnlyList<TaskCardSummaryDto>> ListByBoardAsync(Guid boardId, bool includeArchived = false, CancellationToken cancellationToken = default)
    {
        var cards = await _db.TaskCards.AsNoTracking()
            .Where(c => c.BoardId == boardId && (includeArchived || !c.IsArchived))
            .OrderBy(c => c.ColumnId).ThenBy(c => c.SortOrder)
            .ToListAsync(cancellationToken);
        if (cards.Count == 0) { return Array.Empty<TaskCardSummaryDto>(); }

        var cardIds = cards.Select(c => c.Id).ToList();
        var members = await LoadMembersAsync(cardIds, cancellationToken);
        var tags = await LoadTagsAsync(cardIds, cancellationToken);
        var checklistStats = (await _db.TaskCardChecklistItems.AsNoTracking()
            .Where(i => cardIds.Contains(i.TaskCardId))
            .GroupBy(i => i.TaskCardId)
            .Select(g => new { CardId = g.Key, Total = g.Count(), Done = g.Count(i => i.IsCompleted) })
            .ToListAsync(cancellationToken))
            .ToDictionary(x => x.CardId, x => (Total: x.Total, Done: x.Done));
        var commentCounts = await _db.TaskCardActivities.AsNoTracking()
            .Where(a => cardIds.Contains(a.TaskCardId) && a.Type == TaskActivityType.Comment)
            .GroupBy(a => a.TaskCardId)
            .Select(g => new { CardId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.CardId, x => x.Count, cancellationToken);
        var attachmentCounts = await _db.TaskCardAttachments.AsNoTracking()
            .Where(a => cardIds.Contains(a.TaskCardId))
            .GroupBy(a => a.TaskCardId)
            .Select(g => new { CardId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.CardId, x => x.Count, cancellationToken);

        return cards.Select(c => ToSummary(c, members, tags, checklistStats, commentCounts, attachmentCounts)).ToList();
    }

    public async Task<TaskCardDetailDto?> GetDetailAsync(Guid cardId, CancellationToken cancellationToken = default)
    {
        var card = await _db.TaskCards.AsNoTracking().FirstOrDefaultAsync(c => c.Id == cardId, cancellationToken);
        if (card is null) { return null; }

        var members = await LoadMembersAsync(new List<Guid> { cardId }, cancellationToken);
        var tags = await LoadTagsAsync(new List<Guid> { cardId }, cancellationToken);

        var checklist = await _db.TaskCardChecklistItems.AsNoTracking()
            .Where(i => i.TaskCardId == cardId)
            .OrderBy(i => i.SortOrder)
            .Select(i => new TaskCardChecklistItemDto(i.Id, i.Text, i.IsCompleted, i.SortOrder))
            .ToListAsync(cancellationToken);

        var activity = await _db.TaskCardActivities.AsNoTracking()
            .Where(a => a.TaskCardId == cardId)
            .OrderByDescending(a => a.CreatedAt)
            .Select(a => new TaskCardActivityDto(a.Id, a.Type, a.ActorName, a.Text, a.CreatedAt))
            .ToListAsync(cancellationToken);

        var attachments = await _db.TaskCardAttachments.AsNoTracking()
            .Where(a => a.TaskCardId == cardId)
            .OrderByDescending(a => a.CreatedAt)
            .Select(a => new TaskCardAttachmentDto(a.Id, a.FileName, a.Url, a.MimeType, a.SizeBytes, a.UploadedByName, a.CreatedAt))
            .ToListAsync(cancellationToken);

        var commentCount = activity.Count(a => a.Type == TaskActivityType.Comment);
        var summary = ToSummary(card, members, tags,
            new Dictionary<Guid, (int Total, int Done)> { { cardId, (checklist.Count, checklist.Count(x => x.IsCompleted)) } },
            new Dictionary<Guid, int> { { cardId, commentCount } },
            new Dictionary<Guid, int> { { cardId, attachments.Count } });

        return new TaskCardDetailDto(summary, checklist, activity, attachments);
    }

    public async Task<TaskCardSummaryDto?> CreateAsync(CreateTaskCardRequest request, Guid actorUserId, string actorName, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId) { return null; }
        var title = (request.Title ?? "Nueva tarea").Trim();
        if (title.Length == 0) { return null; }
        // La columna debe existir y pertenecer al board.
        var column = await _db.TaskBoardColumns.FirstOrDefaultAsync(c => c.Id == request.ColumnId && c.BoardId == request.BoardId, cancellationToken);
        if (column is null) { return null; }

        var nextOrder = (await _db.TaskCards.Where(c => c.ColumnId == request.ColumnId).Select(c => (int?)c.SortOrder).MaxAsync(cancellationToken) ?? -1) + 1;
        var card = new TaskCard
        {
            TenantId = tenantId,
            BoardId = request.BoardId,
            ColumnId = request.ColumnId,
            Title = title,
            Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
            Color = string.IsNullOrWhiteSpace(request.Color) ? null : request.Color.Trim(),
            DueAt = request.DueAt,
            SortOrder = nextOrder
        };
        _db.TaskCards.Add(card);
        _db.TaskCardActivities.Add(BuildActivity(tenantId, card.Id, actorUserId, actorName, TaskActivityType.Action, "creo esta tarjeta"));
        await _db.SaveChangesAsync(cancellationToken);
        return ToSummary(card, new(), new(), new(), new(), new());
    }

    public async Task<TaskCardSummaryDto?> UpdateAsync(Guid cardId, UpdateTaskCardRequest request, Guid actorUserId, string actorName, CancellationToken cancellationToken = default)
    {
        var card = await _db.TaskCards.FirstOrDefaultAsync(c => c.Id == cardId, cancellationToken);
        if (card is null) { return null; }
        card.Title = (request.Title ?? card.Title).Trim();
        card.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        card.DueAt = request.DueAt;
        card.Color = string.IsNullOrWhiteSpace(request.Color) ? null : request.Color.Trim();
        await _db.SaveChangesAsync(cancellationToken);
        var summary = await BuildSummaryAsync(card, cancellationToken);
        return summary;
    }

    public async Task<bool> ArchiveAsync(Guid cardId, Guid actorUserId, string actorName, CancellationToken cancellationToken = default)
    {
        var card = await _db.TaskCards.FirstOrDefaultAsync(c => c.Id == cardId, cancellationToken);
        if (card is null) { return false; }
        card.IsArchived = true;
        _db.TaskCardActivities.Add(BuildActivity(card.TenantId, card.Id, actorUserId, actorName, TaskActivityType.Action, "archivo esta tarjeta"));
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> RestoreAsync(Guid cardId, Guid actorUserId, string actorName, CancellationToken cancellationToken = default)
    {
        var card = await _db.TaskCards.FirstOrDefaultAsync(c => c.Id == cardId, cancellationToken);
        if (card is null) { return false; }
        card.IsArchived = false;
        _db.TaskCardActivities.Add(BuildActivity(card.TenantId, card.Id, actorUserId, actorName, TaskActivityType.Action, "restauro esta tarjeta"));
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> DeleteAsync(Guid cardId, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var card = await _db.TaskCards.FirstOrDefaultAsync(c => c.Id == cardId, cancellationToken);
        if (card is null) { return false; }
        _db.TaskCards.Remove(card);
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> MoveAsync(Guid cardId, MoveTaskCardRequest request, Guid actorUserId, string actorName, CancellationToken cancellationToken = default)
    {
        var card = await _db.TaskCards.FirstOrDefaultAsync(c => c.Id == cardId, cancellationToken);
        if (card is null) { return false; }
        var targetColumn = await _db.TaskBoardColumns.AsNoTracking().FirstOrDefaultAsync(c => c.Id == request.ColumnId, cancellationToken);
        if (targetColumn is null || targetColumn.BoardId != card.BoardId) { return false; }

        var movedColumns = card.ColumnId != request.ColumnId;
        card.ColumnId = request.ColumnId;
        card.SortOrder = request.SortOrder;

        if (movedColumns)
        {
            _db.TaskCardActivities.Add(BuildActivity(card.TenantId, card.Id, actorUserId, actorName,
                TaskActivityType.Action, $"movio esta tarjeta a {targetColumn.Name}"));
        }
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> AssignMemberAsync(AssignMemberRequest request, Guid actorUserId, string actorName, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId) { return false; }
        if (await _db.TaskCardAssignments.AnyAsync(a => a.TaskCardId == request.TaskCardId && a.TenantUserId == request.TenantUserId, cancellationToken))
        {
            return true;
        }
        _db.TaskCardAssignments.Add(new TaskCardAssignment
        {
            TenantId = tenantId,
            TaskCardId = request.TaskCardId,
            TenantUserId = request.TenantUserId
        });
        var member = await _db.TenantUsers.AsNoTracking()
            .Include(tu => tu.PlatformUser)
            .FirstOrDefaultAsync(tu => tu.Id == request.TenantUserId, cancellationToken);
        var memberName = member?.PlatformUser?.DisplayName ?? member?.PlatformUser?.Email ?? "(miembro)";
        _db.TaskCardActivities.Add(BuildActivity(tenantId, request.TaskCardId, actorUserId, actorName,
            TaskActivityType.Action, $"asigno a {memberName} a esta tarjeta"));
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> UnassignMemberAsync(Guid cardId, Guid tenantUserId, Guid actorUserId, string actorName, CancellationToken cancellationToken = default)
    {
        var assignment = await _db.TaskCardAssignments.FirstOrDefaultAsync(a => a.TaskCardId == cardId && a.TenantUserId == tenantUserId, cancellationToken);
        if (assignment is null) { return false; }
        _db.TaskCardAssignments.Remove(assignment);
        _db.TaskCardActivities.Add(BuildActivity(assignment.TenantId, cardId, actorUserId, actorName,
            TaskActivityType.Action, "removio un miembro de la tarjeta"));
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> AttachTagAsync(Guid cardId, Guid tagId, Guid actorUserId, string actorName, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId) { return false; }
        if (await _db.TaskCardTagAssignments.AnyAsync(t => t.TaskCardId == cardId && t.TagId == tagId, cancellationToken))
        {
            return true;
        }
        _db.TaskCardTagAssignments.Add(new TaskCardTagAssignment
        {
            TenantId = tenantId,
            TaskCardId = cardId,
            TagId = tagId
        });
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> DetachTagAsync(Guid cardId, Guid tagId, Guid actorUserId, string actorName, CancellationToken cancellationToken = default)
    {
        var assignment = await _db.TaskCardTagAssignments.FirstOrDefaultAsync(t => t.TaskCardId == cardId && t.TagId == tagId, cancellationToken);
        if (assignment is null) { return false; }
        _db.TaskCardTagAssignments.Remove(assignment);
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<TaskCardChecklistItemDto?> AddChecklistItemAsync(AddChecklistItemRequest request, Guid actorUserId, string actorName, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId) { return null; }
        var text = (request.Text ?? "").Trim();
        if (text.Length == 0) { return null; }
        var nextOrder = (await _db.TaskCardChecklistItems.Where(i => i.TaskCardId == request.TaskCardId).Select(i => (int?)i.SortOrder).MaxAsync(cancellationToken) ?? -1) + 1;
        var item = new TaskCardChecklistItem
        {
            TenantId = tenantId,
            TaskCardId = request.TaskCardId,
            Text = text,
            SortOrder = nextOrder
        };
        _db.TaskCardChecklistItems.Add(item);
        await _db.SaveChangesAsync(cancellationToken);
        return new TaskCardChecklistItemDto(item.Id, item.Text, item.IsCompleted, item.SortOrder);
    }

    public async Task<TaskCardChecklistItemDto?> UpdateChecklistItemAsync(Guid itemId, UpdateChecklistItemRequest request, Guid actorUserId, string actorName, CancellationToken cancellationToken = default)
    {
        var item = await _db.TaskCardChecklistItems.FirstOrDefaultAsync(i => i.Id == itemId, cancellationToken);
        if (item is null) { return null; }
        var wasCompleted = item.IsCompleted;
        item.Text = (request.Text ?? item.Text).Trim();
        item.IsCompleted = request.IsCompleted;
        if (request.IsCompleted && !wasCompleted)
        {
            item.CompletedAt = DateTimeOffset.UtcNow;
            item.CompletedBy = actorUserId;
            _db.TaskCardActivities.Add(BuildActivity(item.TenantId, item.TaskCardId, actorUserId, actorName,
                TaskActivityType.Action, $"completo \"{item.Text}\""));
        }
        else if (!request.IsCompleted && wasCompleted)
        {
            item.CompletedAt = null;
            item.CompletedBy = null;
        }
        await _db.SaveChangesAsync(cancellationToken);
        return new TaskCardChecklistItemDto(item.Id, item.Text, item.IsCompleted, item.SortOrder);
    }

    public async Task<bool> DeleteChecklistItemAsync(Guid itemId, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var item = await _db.TaskCardChecklistItems.FirstOrDefaultAsync(i => i.Id == itemId, cancellationToken);
        if (item is null) { return false; }
        _db.TaskCardChecklistItems.Remove(item);
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<TaskCardActivityDto?> AddCommentAsync(AddCommentRequest request, Guid actorUserId, string actorName, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId) { return null; }
        var text = (request.Text ?? "").Trim();
        if (text.Length == 0) { return null; }
        var activity = BuildActivity(tenantId, request.TaskCardId, actorUserId, actorName, TaskActivityType.Comment, text);
        _db.TaskCardActivities.Add(activity);
        await _db.SaveChangesAsync(cancellationToken);
        return new TaskCardActivityDto(activity.Id, activity.Type, activity.ActorName, activity.Text, activity.CreatedAt);
    }

    public async Task<TaskCardAttachmentDto?> AddAttachmentAsync(AddAttachmentRequest request, Guid actorUserId, string actorName, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId) { return null; }
        if (string.IsNullOrWhiteSpace(request.FileName) || string.IsNullOrWhiteSpace(request.Url)) { return null; }
        var att = new TaskCardAttachment
        {
            TenantId = tenantId,
            TaskCardId = request.TaskCardId,
            FileName = request.FileName.Trim(),
            Url = request.Url.Trim(),
            MimeType = string.IsNullOrWhiteSpace(request.MimeType) ? null : request.MimeType.Trim(),
            SizeBytes = request.SizeBytes,
            UploadedBy = actorUserId,
            UploadedByName = actorName
        };
        _db.TaskCardAttachments.Add(att);
        _db.TaskCardActivities.Add(BuildActivity(tenantId, request.TaskCardId, actorUserId, actorName,
            TaskActivityType.Action, $"adjunto el archivo {att.FileName}"));
        await _db.SaveChangesAsync(cancellationToken);
        return new TaskCardAttachmentDto(att.Id, att.FileName, att.Url, att.MimeType, att.SizeBytes, att.UploadedByName, att.CreatedAt);
    }

    public async Task<bool> DeleteAttachmentAsync(Guid attachmentId, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var att = await _db.TaskCardAttachments.FirstOrDefaultAsync(a => a.Id == attachmentId, cancellationToken);
        if (att is null) { return false; }
        _db.TaskCardAttachments.Remove(att);
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    // --- helpers ---

    private static TaskCardActivity BuildActivity(Guid tenantId, Guid cardId, Guid actorUserId, string actorName, TaskActivityType type, string text) =>
        new()
        {
            TenantId = tenantId,
            TaskCardId = cardId,
            Type = type,
            ActorUserId = actorUserId == Guid.Empty ? null : actorUserId,
            ActorName = string.IsNullOrWhiteSpace(actorName) ? "(sin nombre)" : actorName,
            Text = text
        };

    private async Task<Dictionary<Guid, List<TaskCardMemberDto>>> LoadMembersAsync(IReadOnlyList<Guid> cardIds, CancellationToken ct)
    {
        if (cardIds.Count == 0) { return new(); }
        var rows = await _db.TaskCardAssignments.AsNoTracking()
            .Where(a => cardIds.Contains(a.TaskCardId))
            .Join(_db.TenantUsers.AsNoTracking().Include(tu => tu.PlatformUser),
                a => a.TenantUserId,
                tu => tu.Id,
                (a, tu) => new { a.TaskCardId, tu.Id, Email = tu.PlatformUser!.Email, tu.PlatformUserId })
            .ToListAsync(ct);
        var platformUserIds = rows.Select(r => r.PlatformUserId).Distinct().ToList();
        var displayNames = await _db.PlatformUsers.AsNoTracking().IgnoreQueryFilters()
            .Where(p => platformUserIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => p.DisplayName ?? p.Email, ct);
        return rows
            .GroupBy(r => r.TaskCardId)
            .ToDictionary(g => g.Key, g => g.Select(r =>
            {
                var name = displayNames.TryGetValue(r.PlatformUserId, out var n) ? n : r.Email;
                var initials = string.Concat(name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(p => p[0])).ToUpper();
                if (string.IsNullOrEmpty(initials)) { initials = name.Length >= 2 ? name[..2].ToUpper() : name.ToUpper(); }
                return new TaskCardMemberDto(r.Id, initials, name);
            }).ToList());
    }

    private async Task<Dictionary<Guid, List<TaskCardTagDto>>> LoadTagsAsync(IReadOnlyList<Guid> cardIds, CancellationToken ct)
    {
        if (cardIds.Count == 0) { return new(); }
        var rows = await _db.TaskCardTagAssignments.AsNoTracking()
            .Where(a => cardIds.Contains(a.TaskCardId))
            .Join(_db.TaskCardTags.AsNoTracking(),
                a => a.TagId,
                t => t.Id,
                (a, t) => new { a.TaskCardId, t.Id, t.Name, t.Color, t.SortOrder })
            .OrderBy(r => r.SortOrder)
            .ToListAsync(ct);
        return rows
            .GroupBy(r => r.TaskCardId)
            .ToDictionary(g => g.Key, g => g.Select(r => new TaskCardTagDto(r.Id, r.Name, r.Color)).ToList());
    }

    private static TaskCardSummaryDto ToSummary(
        TaskCard card,
        Dictionary<Guid, List<TaskCardMemberDto>> members,
        Dictionary<Guid, List<TaskCardTagDto>> tags,
        Dictionary<Guid, (int Total, int Done)> checklistStats,
        Dictionary<Guid, int> commentCounts,
        Dictionary<Guid, int> attachmentCounts)
    {
        members.TryGetValue(card.Id, out var m);
        tags.TryGetValue(card.Id, out var t);
        var totalChecks = 0; var doneChecks = 0;
        if (checklistStats.TryGetValue(card.Id, out var cs)) { totalChecks = cs.Total; doneChecks = cs.Done; }
        commentCounts.TryGetValue(card.Id, out var comments);
        attachmentCounts.TryGetValue(card.Id, out var attachments);
        return new TaskCardSummaryDto(card.Id, card.BoardId, card.ColumnId, card.Title, card.Description,
            card.DueAt, card.SortOrder, card.IsArchived, card.Color,
            m ?? new List<TaskCardMemberDto>(),
            t ?? new List<TaskCardTagDto>(),
            totalChecks, doneChecks, comments, attachments);
    }

    private async Task<TaskCardSummaryDto> BuildSummaryAsync(TaskCard card, CancellationToken ct)
    {
        var members = await LoadMembersAsync(new List<Guid> { card.Id }, ct);
        var tags = await LoadTagsAsync(new List<Guid> { card.Id }, ct);
        var checks = await _db.TaskCardChecklistItems.AsNoTracking()
            .Where(i => i.TaskCardId == card.Id)
            .Select(i => i.IsCompleted)
            .ToListAsync(ct);
        var comments = await _db.TaskCardActivities.AsNoTracking()
            .CountAsync(a => a.TaskCardId == card.Id && a.Type == TaskActivityType.Comment, ct);
        var attachments = await _db.TaskCardAttachments.AsNoTracking()
            .CountAsync(a => a.TaskCardId == card.Id, ct);
        var checklistDict = new Dictionary<Guid, (int Total, int Done)> { { card.Id, (checks.Count, checks.Count(b => b)) } };
        return ToSummary(card, members, tags,
            checklistDict,
            new Dictionary<Guid, int> { { card.Id, comments } },
            new Dictionary<Guid, int> { { card.Id, attachments } });
    }
}
