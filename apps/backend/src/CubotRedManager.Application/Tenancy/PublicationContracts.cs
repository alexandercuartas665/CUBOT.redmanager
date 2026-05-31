using CubotRedManager.Domain.Enums;

namespace CubotRedManager.Application.Tenancy;

public sealed record PublicationDto(
    Guid Id,
    Guid ClientId,
    string ClientName,
    string Caption,
    DateTimeOffset? ScheduledAt,
    PublicationStatus Status,
    int TargetCount);

public sealed record CreatePublicationRequest(
    Guid ClientId,
    string Caption,
    DateTimeOffset? ScheduledAt,
    IReadOnlyList<Guid> SocialAccountIds);

/// <summary>Calendario editorial y publicaciones (Modulo 2.5). Tenant-scoped.</summary>
public interface IPublicationService
{
    /// <summary>Lista publicaciones por rango (calendario). Si from/to nulos, trae todas.</summary>
    Task<IReadOnlyList<PublicationDto>> ListAsync(DateTimeOffset? from = null, DateTimeOffset? to = null, CancellationToken cancellationToken = default);
    Task<PublicationDto?> CreateAsync(CreatePublicationRequest request, Guid actorUserId, CancellationToken cancellationToken = default);
    /// <summary>Transiciona el estado (Draft->Approved->Scheduled->Published / Failed).</summary>
    Task<PublicationDto?> SetStatusAsync(Guid id, PublicationStatus status, Guid actorUserId, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(Guid id, Guid actorUserId, CancellationToken cancellationToken = default);
}
