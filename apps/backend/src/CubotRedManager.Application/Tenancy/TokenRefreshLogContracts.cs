namespace CubotRedManager.Application.Tenancy;

/// <summary>Historial de intentos de refresh/exchange OAuth por cuenta. Diagnostico persistente.</summary>
public sealed record TokenRefreshLogDto(
    Guid Id,
    DateTimeOffset AttemptedAt,
    string Operation,
    string Endpoint,
    string Flavor,
    bool Success,
    int? HttpStatus,
    string? ResponseCode,
    string? ErrorMessage,
    int DurationMs,
    int FailureCountAfter,
    string? RawResponse);

public interface ITokenRefreshLogService
{
    /// <summary>Ultimos N intentos para una cuenta social (por AttemptedAt DESC).</summary>
    Task<IReadOnlyList<TokenRefreshLogDto>> ListForAccountAsync(Guid socialAccountId, int take = 30, CancellationToken cancellationToken = default);

    /// <summary>Borra todo el historial de una cuenta (util cuando se ha resuelto un problema y
    /// el operador quiere empezar limpio para diagnosticar el proximo). Devuelve numero de filas
    /// eliminadas. Tenant-scoped implicito.</summary>
    Task<int> ClearForAccountAsync(Guid socialAccountId, Guid actorUserId, CancellationToken cancellationToken = default);
}
