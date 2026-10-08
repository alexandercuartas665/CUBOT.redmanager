using CubotRedManager.Domain.Enums;

namespace CubotRedManager.Application.Tenancy;

// === Pipeline (contenedor) ===========================================================

public sealed record PipelineDto(
    Guid Id,
    string Name,
    int SortOrder,
    bool IsSystem,
    int StageCount,
    int LeadCount);

public sealed record CreatePipelineRequest(string Name);

public sealed record UpdatePipelineRequest(string Name);

public sealed record ReorderPipelinesRequest(IReadOnlyList<Guid> OrderedPipelineIds);

// === Stage ===========================================================================

public sealed record PipelineStageDto(
    Guid Id,
    Guid PipelineId,
    string Name,
    int SortOrder,
    bool IsClosedWon,
    bool IsClosedLost);

public sealed record CreatePipelineStageRequest(
    Guid PipelineId,
    string Name,
    int SortOrder,
    bool IsClosedWon = false,
    bool IsClosedLost = false);

public sealed record UpdatePipelineStageRequest(
    string Name,
    bool IsClosedWon,
    bool IsClosedLost);

/// <summary>Nuevo orden de las etapas DENTRO de un pipeline.</summary>
public sealed record ReorderStagesRequest(Guid PipelineId, IReadOnlyList<Guid> OrderedStageIds);
public sealed record ReorderFieldsRequest(IReadOnlyList<Guid> OrderedFieldIds);

// === Campos configurables ===========================================================

public sealed record PipelineFieldDto(
    Guid Id,
    Guid StageId,
    string FieldKey,
    string Label,
    PipelineFieldType FieldType,
    int Column,
    int SortOrder,
    string? Options,
    string? Description = null,
    bool AllowMultiple = false,
    string? RepeatWithFieldKey = null,
    bool MultiWithDetail = false,
    string? TotalSourceKeys = null,
    bool ShowInFilter = false);

public sealed record CreatePipelineFieldRequest(
    Guid StageId,
    string Label,
    PipelineFieldType FieldType,
    int Column = 1,
    string? Options = null,
    string? FieldKey = null,
    string? Description = null,
    bool AllowMultiple = false,
    string? RepeatWithFieldKey = null,
    bool MultiWithDetail = false,
    string? TotalSourceKeys = null,
    bool ShowInFilter = false);

public sealed record UpdatePipelineFieldRequest(
    string Label,
    PipelineFieldType FieldType,
    int Column,
    string? Options,
    string? Description = null,
    bool AllowMultiple = false,
    string? RepeatWithFieldKey = null,
    bool MultiWithDetail = false,
    string? TotalSourceKeys = null,
    bool ShowInFilter = false);
