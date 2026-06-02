using CubotRedManager.Domain.Enums;

namespace CubotRedManager.Application.Tenancy;

public sealed record PublicationExecTargetResult(
    Guid TargetId,
    Guid SocialAccountId,
    string NetworkCode,
    string? Handle,
    bool Success,
    string? PublishId,
    string? Error);

public sealed record PublicationExecResult(
    bool Success,
    PublicationStatus FinalStatus,
    string Trace,
    IReadOnlyList<PublicationExecTargetResult> Targets);

/// <summary>
/// Ejecuta (publica) una Publication: por cada PublicationTarget toma su SocialAccount, sube el
/// video a la red correspondiente y actualiza el estado. Implementa el flujo Content Posting
/// API para TikTok (init -> upload PUT -> status polling). Auto-refresh de token en 40105.
/// </summary>
public interface IPublicationExecutorService
{
    Task<PublicationExecResult> ExecuteAsync(Guid publicationId, Guid actorUserId, CancellationToken cancellationToken = default);
}
