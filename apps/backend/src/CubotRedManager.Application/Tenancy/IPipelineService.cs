namespace CubotRedManager.Application.Tenancy;

/// <summary>
/// Gestion de embudos del tenant activo: contenedor Pipeline + sus Stages + Fields configurables.
/// Multi-pipeline por tenant (diferencia vs CUBOT.travels, que solo tiene uno).
/// </summary>
public interface IPipelineService
{
    /// <summary>Crea los pipelines por defecto ("Productos" + "Negocios") si el tenant todavia
    /// no tiene ningun Pipeline. Idempotente: no hace nada si ya existen pipelines.</summary>
    Task EnsureDefaultsAsync(Guid actorUserId, CancellationToken cancellationToken = default);

    // === Pipeline (contenedor) ===

    Task<IReadOnlyList<PipelineDto>> ListPipelinesAsync(CancellationToken cancellationToken = default);
    Task<PipelineDto?> CreatePipelineAsync(CreatePipelineRequest request, Guid actorUserId, CancellationToken cancellationToken = default);
    Task<PipelineDto?> UpdatePipelineAsync(Guid pipelineId, UpdatePipelineRequest request, Guid actorUserId, CancellationToken cancellationToken = default);
    Task ReorderPipelinesAsync(ReorderPipelinesRequest request, Guid actorUserId, CancellationToken cancellationToken = default);
    /// <summary>Elimina un pipeline no-system y sin leads (cascade sobre Stages + Fields).
    /// Devuelve false si es system, tiene leads o no existe.</summary>
    Task<bool> DeletePipelineAsync(Guid pipelineId, Guid actorUserId, CancellationToken cancellationToken = default);

    // === Stage ===

    /// <summary>Lista todas las stages, opcionalmente filtradas por pipeline (null = todas).</summary>
    Task<IReadOnlyList<PipelineStageDto>> ListStagesAsync(Guid? pipelineId = null, CancellationToken cancellationToken = default);
    Task<PipelineStageDto?> CreateStageAsync(CreatePipelineStageRequest request, Guid actorUserId, CancellationToken cancellationToken = default);
    Task<PipelineStageDto?> UpdateStageAsync(Guid stageId, UpdatePipelineStageRequest request, Guid actorUserId, CancellationToken cancellationToken = default);
    Task ReorderStagesAsync(ReorderStagesRequest request, Guid actorUserId, CancellationToken cancellationToken = default);
    /// <summary>Elimina una stage solo si no tiene leads. Devuelve false si tiene leads o no existe.</summary>
    Task<bool> DeleteStageAsync(Guid stageId, Guid actorUserId, CancellationToken cancellationToken = default);

    // === Fields ===

    Task<IReadOnlyList<PipelineFieldDto>> ListFieldsAsync(Guid? pipelineId = null, CancellationToken cancellationToken = default);
    Task<PipelineFieldDto?> CreateFieldAsync(CreatePipelineFieldRequest request, Guid actorUserId, CancellationToken cancellationToken = default);
    Task<PipelineFieldDto?> UpdateFieldAsync(Guid fieldId, UpdatePipelineFieldRequest request, Guid actorUserId, CancellationToken cancellationToken = default);
    /// <summary>Mueve un field a otra stage del mismo pipeline (lo coloca al final). Devuelve null si
    /// la stage destino es de otro pipeline (no se permite cruzar pipelines).</summary>
    Task<PipelineFieldDto?> MoveFieldToStageAsync(Guid fieldId, Guid targetStageId, Guid actorUserId, CancellationToken cancellationToken = default);
    Task ReorderFieldsAsync(ReorderFieldsRequest request, Guid actorUserId, CancellationToken cancellationToken = default);
    Task<bool> DeleteFieldAsync(Guid fieldId, Guid actorUserId, CancellationToken cancellationToken = default);
}
