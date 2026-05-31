using CubotRedManager.Application.Abstractions;
using CubotRedManager.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CubotRedManager.Application.Tenancy;

public sealed class TaskBoardService : ITaskBoardService
{
    private static readonly (string Name, string Color)[] DefaultColumns =
    {
        ("Por Hacer", "#6b7280"),
        ("En Progreso", "#A03DC9"),
        ("En Revision", "#C7398B"),
        ("Completado", "#16a34a")
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

    public async Task<IReadOnlyList<TaskBoardListItem>> ListBoardsAsync(CancellationToken cancellationToken = default)
    {
        var boards = await _db.TaskBoards.AsNoTracking().OrderBy(b => b.Name).ToListAsync(cancellationToken);
        var colCounts = await _db.TaskColumns.AsNoTracking().GroupBy(c => c.BoardId).Select(g => new { g.Key, C = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.C, cancellationToken);
        var cardCounts = await _db.TaskCards.AsNoTracking().GroupBy(c => c.BoardId).Select(g => new { g.Key, C = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.C, cancellationToken);
        return boards.Select(b => new TaskBoardListItem(b.Id, b.Name, b.Description,
            colCounts.TryGetValue(b.Id, out var cc) ? cc : 0,
            cardCounts.TryGetValue(b.Id, out var kc) ? kc : 0)).ToList();
    }

    public async Task<TaskBoardDetail?> GetBoardAsync(Guid boardId, CancellationToken cancellationToken = default)
    {
        var board = await _db.TaskBoards.AsNoTracking().FirstOrDefaultAsync(b => b.Id == boardId, cancellationToken);
        if (board is null) { return null; }
        var columns = await _db.TaskColumns.AsNoTracking().Where(c => c.BoardId == boardId).OrderBy(c => c.SortOrder).ToListAsync(cancellationToken);
        var cards = await _db.TaskCards.AsNoTracking().Where(c => c.BoardId == boardId).OrderBy(c => c.SortOrder).ToListAsync(cancellationToken);
        var colDtos = columns.Select(col => new TaskColumnDto(col.Id, col.Name, col.SortOrder, col.ColorHex,
            cards.Where(k => k.ColumnId == col.Id)
                 .Select(k => new TaskCardDto(k.Id, k.ColumnId, k.Title, k.DescriptionMd, k.Priority, k.DueDate, k.SortOrder)).ToList())).ToList();
        return new TaskBoardDetail(board.Id, board.Name, board.Description, colDtos);
    }

    public async Task<TaskBoardListItem?> CreateBoardAsync(CreateTaskBoardRequest request, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId) { return null; }
        var board = new TaskBoard
        {
            TenantId = tenantId,
            Name = request.Name.Trim(),
            Description = request.Description?.Trim(),
            ClientId = request.ClientId
        };
        _db.TaskBoards.Add(board);

        var order = 0;
        foreach (var (name, color) in DefaultColumns)
        {
            _db.TaskColumns.Add(new TaskColumn { TenantId = tenantId, BoardId = board.Id, Name = name, ColorHex = color, SortOrder = order++ });
        }
        _audit.Write(actorUserId, "taskboard.create", nameof(TaskBoard), board.Id, previousValue: null, newValue: new { board.Name }, tenantId: tenantId);
        await _db.SaveChangesAsync(cancellationToken);
        return new TaskBoardListItem(board.Id, board.Name, board.Description, DefaultColumns.Length, 0);
    }

    public async Task<TaskCardDto?> AddCardAsync(CreateTaskCardRequest request, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId) { return null; }
        var column = await _db.TaskColumns.FirstOrDefaultAsync(c => c.Id == request.ColumnId && c.BoardId == request.BoardId, cancellationToken);
        if (column is null) { return null; }
        var nextOrder = (await _db.TaskCards.Where(c => c.ColumnId == request.ColumnId).Select(c => (int?)c.SortOrder).MaxAsync(cancellationToken) ?? -1) + 1;
        var card = new TaskCard
        {
            TenantId = tenantId,
            BoardId = request.BoardId,
            ColumnId = request.ColumnId,
            Title = request.Title.Trim(),
            DescriptionMd = request.DescriptionMd?.Trim(),
            Priority = request.Priority,
            DueDate = request.DueDate,
            SortOrder = nextOrder
        };
        _db.TaskCards.Add(card);
        await _db.SaveChangesAsync(cancellationToken);
        return new TaskCardDto(card.Id, card.ColumnId, card.Title, card.DescriptionMd, card.Priority, card.DueDate, card.SortOrder);
    }

    public async Task<bool> MoveCardAsync(Guid cardId, Guid toColumnId, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var card = await _db.TaskCards.FirstOrDefaultAsync(c => c.Id == cardId, cancellationToken);
        if (card is null) { return false; }
        var column = await _db.TaskColumns.FirstOrDefaultAsync(c => c.Id == toColumnId && c.BoardId == card.BoardId, cancellationToken);
        if (column is null) { return false; }
        if (card.ColumnId == toColumnId) { return true; }
        var nextOrder = (await _db.TaskCards.Where(c => c.ColumnId == toColumnId).Select(c => (int?)c.SortOrder).MaxAsync(cancellationToken) ?? -1) + 1;
        card.ColumnId = toColumnId;
        card.SortOrder = nextOrder;
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> DeleteCardAsync(Guid cardId, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var card = await _db.TaskCards.FirstOrDefaultAsync(c => c.Id == cardId, cancellationToken);
        if (card is null) { return false; }
        _db.TaskCards.Remove(card);
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }
}
