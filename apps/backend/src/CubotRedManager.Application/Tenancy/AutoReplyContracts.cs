using CubotRedManager.Domain.Enums;

namespace CubotRedManager.Application.Tenancy;

public sealed record AutoReplyTemplateDto(Guid Id, string Keywords, string Body, int SortOrder);

public sealed record AutoReplyConfigDto(
    Guid? Id,
    Guid SocialAccountId,
    bool IsActive,
    AutoReplyMode Mode,
    int MaxRepliesPerRun,
    int DelayMinSeconds,
    int DelayMaxSeconds,
    string? BlacklistKeywords,
    AutoReplyFrequency Frequency,
    string? CronCustom,
    int ActiveHoursMask,
    byte ActiveDaysOfWeekMask,
    string? DefaultTemplate,
    Guid? AiAgentId,
    IReadOnlyList<AutoReplyTemplateDto> Templates);

/// <summary>Datos para guardar (upsert) la config + plantillas de una cuenta.</summary>
public sealed record SaveAutoReplyConfigRequest(
    Guid SocialAccountId,
    bool IsActive,
    AutoReplyMode Mode,
    int MaxRepliesPerRun,
    int DelayMinSeconds,
    int DelayMaxSeconds,
    string? BlacklistKeywords,
    AutoReplyFrequency Frequency,
    string? CronCustom,
    int ActiveHoursMask,
    byte ActiveDaysOfWeekMask,
    string? DefaultTemplate,
    Guid? AiAgentId,
    IReadOnlyList<AutoReplyTemplateInput> Templates);

/// <summary>Resumen breve de un agente IA del tenant para llenar el dropdown del modal.</summary>
public sealed record AutoReplyAgentOptionDto(Guid Id, string Name, AiProvider Provider, bool IsActive);

public sealed record AutoReplyTemplateInput(Guid? Id, string Keywords, string Body, int SortOrder);

/// <summary>Resumen del log de una ejecucion (para lista).</summary>
public sealed record AutoReplyJobLogDto(
    Guid Id,
    Guid SocialAccountId,
    string? AccountHandle,
    string? ClientName,
    AutoReplyJobStatus Status,
    DateTimeOffset StartedAt,
    int DurationMs,
    int Processed,
    int Replied,
    int Errors,
    int Omitted);

/// <summary>Log con detalle (para modal).</summary>
public sealed record AutoReplyJobLogDetailDto(AutoReplyJobLogDto Summary, string? Trace);

/// <summary>
/// Modulo 2.11 — Autorespuesta de comentarios. Una configuracion por SocialAccount.
/// El worker que realmente ejecuta queda fuera de este servicio (vive en Workers / BackgroundService).
/// </summary>
public interface IAutoReplyConfigService
{
    /// <summary>Devuelve la config de una cuenta (o defaults si nunca se ha guardado).</summary>
    Task<AutoReplyConfigDto> GetOrDefaultAsync(Guid socialAccountId, CancellationToken cancellationToken = default);

    /// <summary>Upsert atomico de la config + lista completa de plantillas (reemplaza las existentes).</summary>
    Task<AutoReplyConfigDto?> SaveAsync(SaveAutoReplyConfigRequest request, Guid actorUserId, CancellationToken cancellationToken = default);

    /// <summary>Cambia solo el flag IsActive (toggle desde la card de la cuenta).</summary>
    Task<bool> ToggleActiveAsync(Guid socialAccountId, bool isActive, Guid actorUserId, CancellationToken cancellationToken = default);

    /// <summary>Lista logs de las ultimas N ejecuciones, opcionalmente filtrando por cuenta y/o estado.</summary>
    Task<IReadOnlyList<AutoReplyJobLogDto>> ListLogsAsync(Guid? socialAccountId = null, AutoReplyJobStatus? status = null, int take = 100, CancellationToken cancellationToken = default);

    /// <summary>Detalle de un log (incluye Trace).</summary>
    Task<AutoReplyJobLogDetailDto?> GetLogDetailAsync(Guid logId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Borra TODOS los logs del tenant actual. Lo dispara el boton manual de la UI; el worker
    /// hace su propia purga por retencion (5 dias por defecto). Devuelve cuantas filas borro.
    /// </summary>
    Task<int> DeleteAllLogsAsync(Guid actorUserId, CancellationToken cancellationToken = default);

    /// <summary>Lista de agentes IA del tenant para llenar el dropdown del modal de Autorespuesta.</summary>
    Task<IReadOnlyList<AutoReplyAgentOptionDto>> ListAgentOptionsAsync(CancellationToken cancellationToken = default);
}
