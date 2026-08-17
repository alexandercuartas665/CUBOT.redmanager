using CubotRedManager.Domain.Enums;

namespace CubotRedManager.Application.Tenancy;

/// <summary>Metadata mostrada al front por cada media adjunta. La URL apunta a
/// /api/publications/media/{id} — el binario vive en BD, no en disco (ver PublicationMedia).</summary>
public sealed record PublicationMediaDto(Guid Id, string FileName, string MimeType, long FileSize);

public sealed record PublicationDto(
    Guid Id,
    Guid ClientId,
    string ClientName,
    string Caption,
    DateTimeOffset? ScheduledAt,
    PublicationStatus Status,
    int TargetCount,
    IReadOnlyList<PublicationMediaDto> Media);

/// <summary>Blob a persistir. Content es el bytea; MimeType lo resolvio el uploader (Calendario.razor).</summary>
public sealed record PublicationMediaBlob(string FileName, string MimeType, byte[] Content);

public sealed record CreatePublicationRequest(
    Guid ClientId,
    string Caption,
    DateTimeOffset? ScheduledAt,
    IReadOnlyList<Guid> SocialAccountIds,
    /// <summary>Adjuntos con contenido binario. Vacio si solo es texto.</summary>
    IReadOnlyList<PublicationMediaBlob> Media);

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
