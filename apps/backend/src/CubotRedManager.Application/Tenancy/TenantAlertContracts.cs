using CubotRedManager.Domain.Enums;

namespace CubotRedManager.Application.Tenancy;

/// <summary>Configuracion de alertas del tenant (una por tenant). Reusa el enum
/// AutoReplySummaryTargetType para el tipo de destinatario (Phone / Group).</summary>
public sealed record TenantAlertConfigDto(
    bool IsActive,
    Guid? WhatsAppLineId,
    AutoReplySummaryTargetType TargetType,
    string? Target);

public sealed record SaveTenantAlertConfigRequest(
    bool IsActive,
    Guid? WhatsAppLineId,
    AutoReplySummaryTargetType TargetType,
    string? Target);

/// <summary>Servicio de la configuracion de alertas por WhatsApp del tenant. Alcance actual:
/// avisar al admin cuando el refresh de un token TikTok falla. Extendible sin cambios de contrato.</summary>
public interface ITenantAlertService
{
    Task<TenantAlertConfigDto> GetOrDefaultAsync(CancellationToken cancellationToken = default);
    Task<TenantAlertConfigDto?> SaveAsync(SaveTenantAlertConfigRequest request, Guid actorUserId, CancellationToken cancellationToken = default);
}
