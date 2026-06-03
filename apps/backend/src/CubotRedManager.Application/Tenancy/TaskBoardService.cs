using CubotRedManager.Application.Abstractions;
using CubotRedManager.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CubotRedManager.Application.Tenancy;

public sealed class TaskBoardService : ITaskBoardService
{
    /// <summary>Columnas por defecto al crear un tablero nuevo (replica el prototipo).</summary>
    private static readonly (string Name, string Color, bool IsDone)[] DefaultColumns =
    {
        ("Por hacer",   "#e2e8f0", false),
        ("En progreso", "#bfdbfe", false),
        ("En revision", "#fed7aa", false),
        ("Completado",  "#bbf7d0", true)
    };

    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly IAuditWriter _audit;

    public TaskBoardService(IApplicationDbContext db, ITenantContext tenantContext, IAuditWriter audit)
    {
        _db = db;
        _tenantContext = tenantContext;
        _audit = audit;
    }

    public async Task<IReadOnlyList<TaskBoardDto>> ListBoardsAsync(bool includeArchived = false, CancellationToken cancellationToken = default)
    {
        var boards = await _db.TaskBoards.AsNoTracking()
            .Where(b => includeArchived || !b.IsArchived)
            .OrderBy(b => b.SortOrder).ThenBy(b => b.Name)
            .ToListAsync(cancellationToken);
        if (boards.Count == 0) { return Array.Empty<TaskBoardDto>(); }

        var boardIds = boards.Select(b => b.Id).ToList();
        var doneColumnIds = await _db.TaskBoardColumns.AsNoTracking()
            .Where(c => boardIds.Contains(c.BoardId) && c.IsDone)
            .Select(c => c.Id)
            .ToListAsync(cancellationToken);

        var counts = await _db.TaskCards.AsNoTracking()
            .Where(c => boardIds.Contains(c.BoardId) && !c.IsArchived)
            .GroupBy(c => c.BoardId)
            .Select(g => new
            {
                BoardId = g.Key,
                Total = g.Count(),
                Done = g.Count(c => doneColumnIds.Contains(c.ColumnId))
            })
            .ToDictionaryAsync(x => x.BoardId, cancellationToken);

        return boards.Select(b =>
        {
            counts.TryGetValue(b.Id, out var c);
            return new TaskBoardDto(b.Id, b.Name, b.Description, b.Color, b.SortOrder, b.IsArchived,
                c?.Total ?? 0, c?.Done ?? 0);
        }).ToList();
    }

    public async Task<TaskBoardDto?> CreateBoardAsync(CreateTaskBoardRequest request, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId) { return null; }
        var name = (request.Name ?? "Tablero").Trim();
        if (name.Length == 0) { return null; }

        var nextOrder = (await _db.TaskBoards.Select(b => (int?)b.SortOrder).MaxAsync(cancellationToken) ?? -1) + 1;
        var board = new TaskBoard
        {
            TenantId = tenantId,
            Name = name,
            Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
            Color = string.IsNullOrWhiteSpace(request.Color) ? null : request.Color.Trim(),
            SortOrder = nextOrder
        };
        _db.TaskBoards.Add(board);

        // Sembramos columnas por defecto para que el tablero sea usable desde el primer click.
        for (int i = 0; i < DefaultColumns.Length; i++)
        {
            var (cname, ccolor, isDone) = DefaultColumns[i];
            _db.TaskBoardColumns.Add(new TaskBoardColumn
            {
                TenantId = tenantId,
                BoardId = board.Id,
                Name = cname,
                Color = ccolor,
                SortOrder = i,
                IsDone = isDone
            });
        }

        _audit.Write(actorUserId, "task-board.create", nameof(TaskBoard), board.Id,
            previousValue: null, newValue: new { board.Name }, tenantId: tenantId);
        await _db.SaveChangesAsync(cancellationToken);
        return new TaskBoardDto(board.Id, board.Name, board.Description, board.Color, board.SortOrder, board.IsArchived, 0, 0);
    }

    public async Task<TaskBoardDto?> UpdateBoardAsync(Guid boardId, UpdateTaskBoardRequest request, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var board = await _db.TaskBoards.FirstOrDefaultAsync(b => b.Id == boardId, cancellationToken);
        if (board is null) { return null; }
        board.Name = (request.Name ?? board.Name).Trim();
        board.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        board.Color = string.IsNullOrWhiteSpace(request.Color) ? null : request.Color.Trim();
        board.IsArchived = request.IsArchived;
        await _db.SaveChangesAsync(cancellationToken);
        return new TaskBoardDto(board.Id, board.Name, board.Description, board.Color, board.SortOrder, board.IsArchived, 0, 0);
    }

    public async Task<bool> DeleteBoardAsync(Guid boardId, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var board = await _db.TaskBoards.FirstOrDefaultAsync(b => b.Id == boardId, cancellationToken);
        if (board is null) { return false; }
        _db.TaskBoards.Remove(board);
        _audit.Write(actorUserId, "task-board.delete", nameof(TaskBoard), board.Id,
            previousValue: new { board.Name }, newValue: null, tenantId: board.TenantId);
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<IReadOnlyList<TaskBoardColumnDto>> ListColumnsAsync(Guid boardId, CancellationToken cancellationToken = default)
    {
        var columns = await _db.TaskBoardColumns.AsNoTracking()
            .Where(c => c.BoardId == boardId)
            .OrderBy(c => c.SortOrder).ThenBy(c => c.Name)
            .ToListAsync(cancellationToken);
        if (columns.Count == 0) { return Array.Empty<TaskBoardColumnDto>(); }

        var counts = await _db.TaskCards.AsNoTracking()
            .Where(c => c.BoardId == boardId && !c.IsArchived)
            .GroupBy(c => c.ColumnId)
            .Select(g => new { ColumnId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.ColumnId, x => x.Count, cancellationToken);

        return columns.Select(c => new TaskBoardColumnDto(c.Id, c.BoardId, c.Name, c.Color, c.SortOrder, c.IsDone,
            counts.TryGetValue(c.Id, out var n) ? n : 0)).ToList();
    }

    public async Task<TaskBoardColumnDto?> CreateColumnAsync(CreateTaskColumnRequest request, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId) { return null; }
        var board = await _db.TaskBoards.FirstOrDefaultAsync(b => b.Id == request.BoardId, cancellationToken);
        if (board is null) { return null; }
        var name = (request.Name ?? "Columna").Trim();
        if (name.Length == 0) { return null; }

        var nextOrder = (await _db.TaskBoardColumns.Where(c => c.BoardId == request.BoardId).Select(c => (int?)c.SortOrder).MaxAsync(cancellationToken) ?? -1) + 1;
        var col = new TaskBoardColumn
        {
            TenantId = tenantId,
            BoardId = request.BoardId,
            Name = name,
            Color = string.IsNullOrWhiteSpace(request.Color) ? null : request.Color.Trim(),
            IsDone = request.IsDone,
            SortOrder = nextOrder
        };
        _db.TaskBoardColumns.Add(col);
        await _db.SaveChangesAsync(cancellationToken);
        return new TaskBoardColumnDto(col.Id, col.BoardId, col.Name, col.Color, col.SortOrder, col.IsDone, 0);
    }

    public async Task<TaskBoardColumnDto?> UpdateColumnAsync(Guid columnId, UpdateTaskColumnRequest request, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var col = await _db.TaskBoardColumns.FirstOrDefaultAsync(c => c.Id == columnId, cancellationToken);
        if (col is null) { return null; }
        col.Name = (request.Name ?? col.Name).Trim();
        col.Color = string.IsNullOrWhiteSpace(request.Color) ? null : request.Color.Trim();
        col.IsDone = request.IsDone;
        await _db.SaveChangesAsync(cancellationToken);
        var count = await _db.TaskCards.Where(c => c.ColumnId == col.Id && !c.IsArchived).CountAsync(cancellationToken);
        return new TaskBoardColumnDto(col.Id, col.BoardId, col.Name, col.Color, col.SortOrder, col.IsDone, count);
    }

    public async Task<bool> DeleteColumnAsync(Guid columnId, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var col = await _db.TaskBoardColumns.FirstOrDefaultAsync(c => c.Id == columnId, cancellationToken);
        if (col is null) { return false; }
        // No permitimos borrar columnas que tengan tarjetas: el usuario debe moverlas primero.
        var hasCards = await _db.TaskCards.AnyAsync(c => c.ColumnId == columnId && !c.IsArchived, cancellationToken);
        if (hasCards) { return false; }
        _db.TaskBoardColumns.Remove(col);
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> ReorderColumnsAsync(Guid boardId, IReadOnlyList<Guid> orderedColumnIds, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var columns = await _db.TaskBoardColumns.Where(c => c.BoardId == boardId).ToListAsync(cancellationToken);
        for (int i = 0; i < orderedColumnIds.Count; i++)
        {
            var col = columns.FirstOrDefault(c => c.Id == orderedColumnIds[i]);
            if (col is not null) { col.SortOrder = i; }
        }
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<IReadOnlyList<TaskCardTagDto>> ListBoardTagsAsync(Guid boardId, CancellationToken cancellationToken = default)
    {
        return await _db.TaskCardTags.AsNoTracking()
            .Where(t => t.BoardId == boardId)
            .OrderBy(t => t.SortOrder).ThenBy(t => t.Name)
            .Select(t => new TaskCardTagDto(t.Id, t.Name, t.Color))
            .ToListAsync(cancellationToken);
    }

    public async Task<TaskCardTagDto?> CreateBoardTagAsync(CreateBoardTagRequest request, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId) { return null; }
        var name = (request.Name ?? "").Trim();
        if (name.Length == 0) { return null; }
        // El indice unico (boardId, name) impide duplicados; verificamos antes para devolver un null amigable.
        if (await _db.TaskCardTags.AnyAsync(t => t.BoardId == request.BoardId && t.Name == name, cancellationToken))
        {
            return null;
        }
        var nextOrder = (await _db.TaskCardTags.Where(t => t.BoardId == request.BoardId).Select(t => (int?)t.SortOrder).MaxAsync(cancellationToken) ?? -1) + 1;
        var tag = new TaskCardTag
        {
            TenantId = tenantId,
            BoardId = request.BoardId,
            Name = name,
            Color = string.IsNullOrWhiteSpace(request.Color) ? null : request.Color.Trim(),
            SortOrder = nextOrder
        };
        _db.TaskCardTags.Add(tag);
        await _db.SaveChangesAsync(cancellationToken);
        return new TaskCardTagDto(tag.Id, tag.Name, tag.Color);
    }

    public async Task<TaskCardTagDto?> UpdateBoardTagAsync(Guid tagId, UpdateBoardTagRequest request, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var tag = await _db.TaskCardTags.FirstOrDefaultAsync(t => t.Id == tagId, cancellationToken);
        if (tag is null) { return null; }
        tag.Name = (request.Name ?? tag.Name).Trim();
        tag.Color = string.IsNullOrWhiteSpace(request.Color) ? null : request.Color.Trim();
        await _db.SaveChangesAsync(cancellationToken);
        return new TaskCardTagDto(tag.Id, tag.Name, tag.Color);
    }

    public async Task<bool> DeleteBoardTagAsync(Guid tagId, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var tag = await _db.TaskCardTags.FirstOrDefaultAsync(t => t.Id == tagId, cancellationToken);
        if (tag is null) { return false; }
        _db.TaskCardTags.Remove(tag);
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }
}
