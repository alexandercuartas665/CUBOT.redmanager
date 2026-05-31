using CubotRedManager.Domain.Enums;

namespace CubotRedManager.Application.Admin;

public sealed record PlanLimitInput(
    string LimitKey,
    long LimitValue,
    string? LimitUnit = null,
    LimitEnforcementMode EnforcementMode = LimitEnforcementMode.Hard);

public sealed record CreatePlanRequest(
    string Name,
    string? Description,
    decimal? MonthlyPrice,
    decimal? YearlyPrice,
    string? Currency,
    IReadOnlyList<PlanLimitInput> Limits);

public sealed record PlanLimitDto(string LimitKey, long LimitValue, string? LimitUnit, LimitEnforcementMode EnforcementMode);

public sealed record PlanDetail(
    Guid Id,
    string Name,
    string? Description,
    decimal? MonthlyPrice,
    decimal? YearlyPrice,
    string? Currency,
    bool IsActive,
    IReadOnlyList<PlanLimitDto> Limits);

public interface IPlanAdminService
{
    Task<PlanDetail> CreateAsync(CreatePlanRequest request, Guid actorUserId, CancellationToken cancellationToken = default);
    Task<PlanDetail?> UpdateAsync(Guid id, CreatePlanRequest request, Guid actorUserId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PlanDetail>> ListAsync(CancellationToken cancellationToken = default);
    Task<PlanDetail?> GetAsync(Guid id, CancellationToken cancellationToken = default);
    Task<PlanDetail?> SetActiveAsync(Guid id, bool isActive, Guid actorUserId, CancellationToken cancellationToken = default);
}
