namespace CubotRedManager.Application.Tenancy;

/// <summary>Leads del embudo del tenant activo. Multi-pipeline: un lead vive en UN pipeline.</summary>
public interface ILeadService
{
    /// <summary>Lista leads activos (no archivados). Filtros opcionales.</summary>
    Task<IReadOnlyList<LeadDto>> ListAsync(Guid? pipelineId = null, Guid? stageId = null, CancellationToken cancellationToken = default);
    Task<LeadDetailDto?> GetAsync(Guid leadId, CancellationToken cancellationToken = default);

    /// <summary>Devuelve null si no hay tenant activo, pipeline invalido, o stage que no pertenece al pipeline.</summary>
    Task<LeadDto?> CreateAsync(CreateLeadRequest request, Guid actorUserId, CancellationToken cancellationToken = default);
    Task<LeadDto?> UpdateAsync(Guid leadId, UpdateLeadRequest request, Guid actorUserId, CancellationToken cancellationToken = default);

    /// <summary>Mueve el lead a otra stage DEL MISMO pipeline. Si la stage es terminal (IsClosedWon/Lost)
    /// actualiza Status. Devuelve null si lead/stage invalidos o si la stage es de otro pipeline.</summary>
    Task<LeadDto?> MoveAsync(Guid leadId, MoveLeadRequest request, Guid actorUserId, CancellationToken cancellationToken = default);

    Task<LeadDto?> AssignAsync(Guid leadId, Guid? tenantUserId, Guid actorUserId, CancellationToken cancellationToken = default);

    /// <summary>Archiva el lead con motivo + nota + nombre snapshot del actor. Suelta el link
    /// de la conversacion WhatsApp (Conversation.LeadId=null) y resetea el contexto del agente
    /// para que un nuevo mensaje del mismo numero arranque de cero.</summary>
    Task<bool> ArchiveAsync(Guid leadId, string reason, string? note, Guid actorUserId, CancellationToken cancellationToken = default);

    Task<LeadDto?> UnarchiveAsync(Guid leadId, Guid actorUserId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ArchivedLeadDto>> ListArchivedAsync(Guid? pipelineId = null, CancellationToken cancellationToken = default);

    /// <summary>Vacia TODO el historial del tenant (destructivo). Solo Owner/Admin. Devuelve null
    /// si el actor no tiene rol suficiente.</summary>
    Task<PurgeArchivedResult?> PurgeArchivedHistoryAsync(CancellationToken cancellationToken = default);

    // === Notas ===
    Task<IReadOnlyList<LeadNoteDto>> ListNotesAsync(Guid leadId, CancellationToken cancellationToken = default);
    Task<LeadNoteDto?> AddNoteAsync(Guid leadId, string content, string color, Guid actorUserId, CancellationToken cancellationToken = default);
    Task<bool> DeleteNoteAsync(Guid noteId, CancellationToken cancellationToken = default);

    // === Files ===
    Task<IReadOnlyList<LeadFileDto>> ListFilesAsync(Guid leadId, CancellationToken cancellationToken = default);
    /// <summary>Guarda el binario del archivo como bytea en BD (patron Railway-safe).</summary>
    Task<LeadFileDto?> AddFileAsync(Guid leadId, string fileName, byte[] content, string contentType, Guid actorUserId, CancellationToken cancellationToken = default);
    Task<bool> DeleteFileAsync(Guid fileId, CancellationToken cancellationToken = default);
}
