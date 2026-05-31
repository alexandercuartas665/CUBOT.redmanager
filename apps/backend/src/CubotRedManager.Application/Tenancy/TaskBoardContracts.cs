using CubotRedManager.Domain.Enums;

namespace CubotRedManager.Application.Tenancy;

public sealed record TaskBoardListItem(Guid Id, string Name, string? Description, int ColumnCount, int CardCount);

public sealed record TaskColumnDto(Guid Id, string Name, int SortOrder, string ColorHex, IReadOnlyList<TaskCardDto> Cards);
public sealed record TaskCardDto(Guid Id, Guid ColumnId, string Title, string? DescriptionMd, TaskPriority Priority, DateOnly? DueDate, int SortOrder);
public sealed record TaskBoardDetail(Guid Id, string Name, string? Description, IReadOnlyList<TaskColumnDto> Columns);

public sealed record CreateTaskBoardRequest(string Name, string? Description = null, Guid? ClientId = null);
public sealed record CreateTaskCardRequest(Guid BoardId, Guid ColumnId, string Title, string? DescriptionMd = null, TaskPriority Priority = TaskPriority.Normal, DateOnly? DueDate = null);

/// <summary>Tablero Kanban del equipo (Modulo 2.7). Tenant-scoped.</summary>
public interface ITaskBoardService
{
    Task<IReadOnlyList<TaskBoardListItem>> ListBoardsAsync(CancellationToken cancellationToken = default);
    Task<TaskBoardDetail?> GetBoardAsync(Guid boardId, CancellationToken cancellationToken = default);
    Task<TaskBoardListItem?> CreateBoardAsync(CreateTaskBoardRequest request, Guid actorUserId, CancellationToken cancellationToken = default);
    Task<TaskCardDto?> AddCardAsync(CreateTaskCardRequest request, Guid actorUserId, CancellationToken cancellationToken = default);
    /// <summary>Mueve una tarjeta a otra columna (al final). Persiste el cambio (drag&drop se cablea en UI).</summary>
    Task<bool> MoveCardAsync(Guid cardId, Guid toColumnId, Guid actorUserId, CancellationToken cancellationToken = default);
    Task<bool> DeleteCardAsync(Guid cardId, Guid actorUserId, CancellationToken cancellationToken = default);
}
