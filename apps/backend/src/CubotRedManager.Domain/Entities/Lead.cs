using CubotRedManager.Domain.Common;
using CubotRedManager.Domain.Enums;

namespace CubotRedManager.Domain.Entities;

/// <summary>
/// Oportunidad comercial dentro de un Pipeline. Tenant-scoped. Un lead vive en UN pipeline
/// (via Stage) y UN estado (Open/Won/Lost). PipelineId se desnormaliza aqui para evitar joins
/// en queries frecuentes (listar leads de un pipeline, mover kanban, metricas de embudo). El
/// servicio mantiene PipelineId == Stage.PipelineId.
///
/// Portado de CUBOT.travels sin: DepartureCity (viaje), ni dependencia a Itinerary.
/// </summary>
public class Lead : TenantEntity
{
    public string ContactName { get; set; } = null!;
    public string? ContactPhone { get; set; }

    /// <summary>Descripcion libre del interes del lead (producto, servicio, etc.). Opcional.</summary>
    public string? Topic { get; set; }

    public decimal? EstimatedValue { get; set; }
    public string? Currency { get; set; }

    /// <summary>Pipeline al que pertenece el lead. Desnormalizado desde Stage.PipelineId para
    /// querying rapido (listado por embudo). El servicio garantiza consistencia.</summary>
    public Guid PipelineId { get; set; }
    public Pipeline? Pipeline { get; set; }

    public Guid StageId { get; set; }
    public PipelineStage? Stage { get; set; }

    public Guid? AssignedToTenantUserId { get; set; }
    public LeadStatus Status { get; set; } = LeadStatus.Open;
    public string? LossReason { get; set; }
    public DateTimeOffset StageChangedAt { get; set; }

    /// <summary>Valores de los campos configurables (jsonb), indexados por FieldKey.</summary>
    public string? FieldValuesJson { get; set; }

    /// <summary>Para leads creados por el agente IA al cerrar atencion: segundos desde el primer
    /// mensaje del cliente hasta el cierre. Null en leads creados manualmente.</summary>
    public int? AttentionDurationSeconds { get; set; }

    // ===== Historial (archivado) =====
    public DateTimeOffset? ArchivedAt { get; set; }
    public string? ArchiveReason { get; set; }
    public string? ArchiveNote { get; set; }
    public string? ArchivedByName { get; set; }
}
