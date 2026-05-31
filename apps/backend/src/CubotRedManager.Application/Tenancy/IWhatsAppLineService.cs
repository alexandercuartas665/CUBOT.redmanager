using CubotRedManager.Domain.Enums;

namespace CubotRedManager.Application.Tenancy;

/// <summary>
/// Gestion de lineas WhatsApp de la agencia activa. Tenant-scoped. La conexion real con Evolution
/// (QR/sesion) se integra via el Evolution Connector.
/// </summary>
public interface IWhatsAppLineService
{
    Task<IReadOnlyList<WhatsAppLineDto>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>Devuelve null si no hay agencia activa.</summary>
    Task<WhatsAppLineDto?> CreateAsync(CreateWhatsAppLineRequest request, Guid actorUserId, CancellationToken cancellationToken = default);

    Task<WhatsAppLineDto?> ChangeStatusAsync(Guid lineId, WhatsAppLineStatus status, Guid actorUserId, CancellationToken cancellationToken = default);

    /// <summary>Asigna (o desasigna con null) la linea a un usuario de la agencia.</summary>
    Task<WhatsAppLineDto?> AssignAsync(Guid lineId, Guid? tenantUserId, Guid actorUserId, CancellationToken cancellationToken = default);
}
