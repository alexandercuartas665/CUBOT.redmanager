namespace CubotRedManager.Application.Tenancy;

/// <summary>Marca/empresa gestionada por la agencia (Modulo 2.1).</summary>
public sealed record ClientDto(
    Guid Id,
    string Name,
    string? ContactName,
    string? ContactEmail,
    string? ContactPhone,
    string? Industry,
    string? BrandLogoUrl,
    string? BrandColorsJson,
    string? BrandToneNotes,
    string? TimeZone,
    string? Notes,
    bool IsActive,
    int AssignedOperatorCount);

public sealed record CreateClientRequest(
    string Name,
    string? ContactName = null,
    string? ContactEmail = null,
    string? ContactPhone = null,
    string? Industry = null,
    string? TimeZone = null,
    string? BrandToneNotes = null,
    string? Notes = null);

public sealed record UpdateClientRequest(
    string Name,
    string? ContactName,
    string? ContactEmail,
    string? ContactPhone,
    string? Industry,
    string? TimeZone,
    string? BrandLogoUrl,
    string? BrandColorsJson,
    string? BrandToneNotes,
    string? Notes);

/// <summary>
/// Gestion de clientes (marcas) de la agencia activa. Tenant-scoped. Un Operator solo ve los
/// clientes asignados (UserClientLink); Admin/Owner ven todos los del tenant.
/// </summary>
public interface IClientService
{
    Task<IReadOnlyList<ClientDto>> ListAsync(CancellationToken cancellationToken = default);
    Task<ClientDto?> GetAsync(Guid id, CancellationToken cancellationToken = default);
    Task<ClientDto?> CreateAsync(CreateClientRequest request, Guid actorUserId, CancellationToken cancellationToken = default);
    Task<ClientDto?> UpdateAsync(Guid id, UpdateClientRequest request, Guid actorUserId, CancellationToken cancellationToken = default);
    Task<ClientDto?> SetActiveAsync(Guid id, bool active, Guid actorUserId, CancellationToken cancellationToken = default);
}
