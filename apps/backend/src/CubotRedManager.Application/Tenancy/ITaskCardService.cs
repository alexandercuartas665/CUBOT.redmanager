namespace CubotRedManager.Application.Tenancy;

/// <summary>
/// Operaciones sobre las tarjetas (tareas) del tablero del tenant activo: CRUD, movimiento,
/// miembros, etiquetas, checklist, comentarios y adjuntos. Todas las llamadas registran
/// actividad automatica en el log de la tarjeta cuando aplica.
/// </summary>
public interface ITaskCardService
{
    // Listado y detalle
    Task<IReadOnlyList<TaskCardSummaryDto>> ListByBoardAsync(Guid boardId, bool includeArchived = false, CancellationToken cancellationToken = default);
    Task<TaskCardDetailDto?> GetDetailAsync(Guid cardId, CancellationToken cancellationToken = default);

    // CRUD
    Task<TaskCardSummaryDto?> CreateAsync(CreateTaskCardRequest request, Guid actorUserId, string actorName, CancellationToken cancellationToken = default);
    Task<TaskCardSummaryDto?> UpdateAsync(Guid cardId, UpdateTaskCardRequest request, Guid actorUserId, string actorName, CancellationToken cancellationToken = default);
    Task<bool> ArchiveAsync(Guid cardId, Guid actorUserId, string actorName, CancellationToken cancellationToken = default);
    Task<bool> RestoreAsync(Guid cardId, Guid actorUserId, string actorName, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(Guid cardId, Guid actorUserId, CancellationToken cancellationToken = default);

    // Movimiento entre columnas / reordenamiento
    Task<bool> MoveAsync(Guid cardId, MoveTaskCardRequest request, Guid actorUserId, string actorName, CancellationToken cancellationToken = default);

    // Miembros
    Task<bool> AssignMemberAsync(AssignMemberRequest request, Guid actorUserId, string actorName, CancellationToken cancellationToken = default);
    Task<bool> UnassignMemberAsync(Guid cardId, Guid tenantUserId, Guid actorUserId, string actorName, CancellationToken cancellationToken = default);

    // Etiquetas (asignacion de etiquetas del catalogo del board a la tarjeta)
    Task<bool> AttachTagAsync(Guid cardId, Guid tagId, Guid actorUserId, string actorName, CancellationToken cancellationToken = default);
    Task<bool> DetachTagAsync(Guid cardId, Guid tagId, Guid actorUserId, string actorName, CancellationToken cancellationToken = default);

    // Checklist
    Task<TaskCardChecklistItemDto?> AddChecklistItemAsync(AddChecklistItemRequest request, Guid actorUserId, string actorName, CancellationToken cancellationToken = default);
    Task<TaskCardChecklistItemDto?> UpdateChecklistItemAsync(Guid itemId, UpdateChecklistItemRequest request, Guid actorUserId, string actorName, CancellationToken cancellationToken = default);
    Task<bool> DeleteChecklistItemAsync(Guid itemId, Guid actorUserId, CancellationToken cancellationToken = default);

    // Comentarios
    Task<TaskCardActivityDto?> AddCommentAsync(AddCommentRequest request, Guid actorUserId, string actorName, CancellationToken cancellationToken = default);

    // Adjuntos
    Task<TaskCardAttachmentDto?> AddAttachmentAsync(AddAttachmentRequest request, Guid actorUserId, string actorName, CancellationToken cancellationToken = default);
    Task<bool> DeleteAttachmentAsync(Guid attachmentId, Guid actorUserId, CancellationToken cancellationToken = default);
}
