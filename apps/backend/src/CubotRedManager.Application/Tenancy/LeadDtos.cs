using CubotRedManager.Domain.Enums;

namespace CubotRedManager.Application.Tenancy;

/// <summary>Vista de un lead para el kanban (sin actividades/notas/archivos — se cargan aparte).</summary>
public sealed record LeadDto(
    Guid Id,
    string ContactName,
    string? ContactPhone,
    string? Topic,
    decimal? EstimatedValue,
    string? Currency,
    Guid PipelineId,
    Guid StageId,
    LeadStatus Status,
    Guid? AssignedToTenantUserId,
    DateTimeOffset StageChangedAt,
    IReadOnlyDictionary<string, string?> FieldValues,
    int? AttentionDurationSeconds = null,
    DateTimeOffset CreatedAt = default);

/// <summary>Resultado de vaciar el historial.</summary>
public sealed record PurgeArchivedResult(int DeletedLeads);

public sealed record LeadActivityDto(Guid Id, string ActivityType, string? Description, DateTimeOffset CreatedAt, string? ActorName);

public sealed record LeadNoteDto(Guid Id, string Content, string Color, DateTimeOffset CreatedAt, string? ActorName);

/// <summary>Metadata de un archivo adjunto. Url apunta a /api/leads/files/{id} (binario en BD).</summary>
public sealed record LeadFileDto(Guid Id, string FileName, string Url, string ContentType, long SizeBytes, DateTimeOffset CreatedAt, string? ActorName);

public sealed record LeadDetailDto(LeadDto Lead, IReadOnlyList<LeadActivityDto> Activities);

public sealed record ArchivedLeadDto(
    Guid Id,
    string ContactName,
    string? ContactPhone,
    string? Topic,
    decimal? EstimatedValue,
    string? Currency,
    string? ArchiveReason,
    string? ArchiveNote,
    DateTimeOffset? ArchivedAt,
    string? ArchivedByName,
    Guid? AssignedToTenantUserId,
    Guid PipelineId,
    Guid StageId,
    LeadStatus Status,
    DateTimeOffset StageChangedAt,
    IReadOnlyDictionary<string, string?> FieldValues);

public sealed record CreateLeadRequest(
    /// <summary>Pipeline donde crear el lead. Requerido (multi-pipeline). Si el PipelineId no
    /// existe o no es del tenant, CreateAsync devuelve null.</summary>
    Guid PipelineId,
    string ContactName,
    string? ContactPhone = null,
    string? Topic = null,
    decimal? EstimatedValue = null,
    string? Currency = null,
    /// <summary>StageId opcional. Si es null se usa el stage con menor SortOrder del pipeline.
    /// Si se indica, DEBE pertenecer al mismo pipeline (sino devuelve null).</summary>
    Guid? StageId = null,
    /// <summary>Valores de campos configurables (indexados por FieldKey de PipelineFieldDefinition).</summary>
    Dictionary<string, string?>? FieldValues = null,
    /// <summary>Duracion de atencion (seg) — se usa cuando crea el agente IA al cerrar la conversacion.</summary>
    int? AttentionDurationSeconds = null);

public sealed record UpdateLeadRequest(
    string ContactName,
    string? ContactPhone,
    string? Topic,
    decimal? EstimatedValue,
    string? Currency,
    Dictionary<string, string?>? FieldValues);

/// <summary>Movimiento entre stages. StageId DEBE pertenecer al mismo PipelineId del lead (no se
/// permite reasignar entre pipelines). LossReason solo aplica si el stage destino es IsClosedLost.</summary>
public sealed record MoveLeadRequest(Guid StageId, string? LossReason = null);

public sealed record AssignLeadRequest(Guid? TenantUserId);
