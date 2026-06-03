namespace CubotRedManager.Application.Tenancy;

/// <summary>
/// Gestion de tableros Kanban del tenant activo (modulo Tableros). Cada agencia (incluyendo el
/// tenant interno "Plataforma CUBOT" del Super Admin) tiene sus propios tableros aislados.
/// </summary>
public interface ITaskBoardService
{
    // Tableros
    Task<IReadOnlyList<TaskBoardDto>> ListBoardsAsync(bool includeArchived = false, CancellationToken cancellationToken = default);
    Task<TaskBoardDto?> CreateBoardAsync(CreateTaskBoardRequest request, Guid actorUserId, CancellationToken cancellationToken = default);
    Task<TaskBoardDto?> UpdateBoardAsync(Guid boardId, UpdateTaskBoardRequest request, Guid actorUserId, CancellationToken cancellationToken = default);
    Task<bool> DeleteBoardAsync(Guid boardId, Guid actorUserId, CancellationToken cancellationToken = default);

    // Columnas
    Task<IReadOnlyList<TaskBoardColumnDto>> ListColumnsAsync(Guid boardId, CancellationToken cancellationToken = default);
    Task<TaskBoardColumnDto?> CreateColumnAsync(CreateTaskColumnRequest request, Guid actorUserId, CancellationToken cancellationToken = default);
    Task<TaskBoardColumnDto?> UpdateColumnAsync(Guid columnId, UpdateTaskColumnRequest request, Guid actorUserId, CancellationToken cancellationToken = default);
    Task<bool> DeleteColumnAsync(Guid columnId, Guid actorUserId, CancellationToken cancellationToken = default);
    Task<bool> ReorderColumnsAsync(Guid boardId, IReadOnlyList<Guid> orderedColumnIds, Guid actorUserId, CancellationToken cancellationToken = default);

    // Etiquetas catalogo
    Task<IReadOnlyList<TaskCardTagDto>> ListBoardTagsAsync(Guid boardId, CancellationToken cancellationToken = default);
    Task<TaskCardTagDto?> CreateBoardTagAsync(CreateBoardTagRequest request, Guid actorUserId, CancellationToken cancellationToken = default);
    Task<TaskCardTagDto?> UpdateBoardTagAsync(Guid tagId, UpdateBoardTagRequest request, Guid actorUserId, CancellationToken cancellationToken = default);
    Task<bool> DeleteBoardTagAsync(Guid tagId, Guid actorUserId, CancellationToken cancellationToken = default);
}
