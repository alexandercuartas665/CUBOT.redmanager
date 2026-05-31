using CubotRedManager.Domain.Enums;

namespace CubotRedManager.Application.Admin;

public sealed record CreateTenantRequest(
    string Name,
    string? LegalName = null,
    string? TaxId = null,
    string? Country = null,
    string? Currency = null,
    TenantKind Kind = TenantKind.Standard);

public sealed record ChangeTenantStatusRequest(TenantStatus Status, string? Reason = null);

public sealed record TenantListItem(
    Guid Id,
    string Name,
    TenantStatus Status,
    TenantKind Kind,
    string? Country,
    string? Currency,
    DateTimeOffset CreatedAt);

public sealed record TenantDetail(
    Guid Id,
    string Name,
    string? LegalName,
    string? TaxId,
    string? Country,
    string? Currency,
    TenantStatus Status,
    TenantKind Kind,
    DateTimeOffset CreatedAt,
    string? LogoUrl = null);

/// <summary>Actualizacion del perfil de la agencia por su propio administrador.</summary>
public sealed record UpdateTenantProfileRequest(
    string Name,
    string? LegalName,
    string? TaxId,
    string? Country,
    string? Currency,
    string? LogoUrl);

public interface ITenantAdminService
{
    Task<TenantDetail> CreateAsync(CreateTenantRequest request, Guid actorUserId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TenantListItem>> ListAsync(TenantStatus? status = null, string? search = null, CancellationToken cancellationToken = default);
    Task<TenantDetail?> GetAsync(Guid id, CancellationToken cancellationToken = default);
    Task<TenantDetail?> ChangeStatusAsync(Guid id, ChangeTenantStatusRequest request, Guid actorUserId, CancellationToken cancellationToken = default);
    Task<TenantDetail?> UpdateProfileAsync(Guid id, UpdateTenantProfileRequest request, Guid actorUserId, CancellationToken cancellationToken = default);
}
