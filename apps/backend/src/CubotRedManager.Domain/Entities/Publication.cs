using CubotRedManager.Domain.Common;
using CubotRedManager.Domain.Enums;

namespace CubotRedManager.Domain.Entities;

/// <summary>Publicacion del calendario editorial (Modulo 2.5). Tenant-scoped. Multi-red via PublicationTarget.</summary>
public class Publication : TenantEntity
{
    public Guid ClientId { get; set; }
    public string Caption { get; set; } = "";
    public string? MediaUrlsJson { get; set; }
    public DateTimeOffset? ScheduledAt { get; set; }
    public PublicationStatus Status { get; set; } = PublicationStatus.Draft;
    public Guid? AuthorTenantUserId { get; set; }
    public Guid? ApprovedByTenantUserId { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
    public string? ExternalRefsJson { get; set; }
    public string? ResultsJson { get; set; }
    public string? FailureReason { get; set; }

    public ICollection<PublicationTarget> Targets { get; set; } = new List<PublicationTarget>();

    /// <summary>Archivos adjuntos (bytea en BD). Reemplaza el flow legacy MediaUrlsJson que
    /// escribia a disco (fallaba en Railway por FS read-only).</summary>
    public ICollection<PublicationMedia> Media { get; set; } = new List<PublicationMedia>();
}
