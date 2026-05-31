using CubotRedManager.Domain.Common;
using CubotRedManager.Domain.Enums;

namespace CubotRedManager.Domain.Entities;

/// <summary>Cuenta destino de una publicacion (Modulo 2.5). Fallo aislado por red: TikTok puede fallar y FB/IG salir bien.</summary>
public class PublicationTarget : TenantEntity
{
    public Guid PublicationId { get; set; }
    public Publication? Publication { get; set; }

    public Guid SocialAccountId { get; set; }
    public string? ExternalId { get; set; }
    public PublicationTargetStatus Status { get; set; } = PublicationTargetStatus.Pending;
    public string? FailureReason { get; set; }
}
