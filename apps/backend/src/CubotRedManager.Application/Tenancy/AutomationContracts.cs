using CubotRedManager.Domain.Enums;

namespace CubotRedManager.Application.Tenancy;

public sealed record AutomationRuleDto(
    Guid Id, string Name, AutomationTrigger Trigger, AutomationAction Action,
    bool IsActive, int? NoReplyMinutes, int ExecutionCount, DateTimeOffset? LastExecutedAt);

public sealed record SaveAutomationRuleRequest(
    Guid? Id, string Name, AutomationTrigger Trigger, AutomationAction Action,
    bool IsActive, int? NoReplyMinutes);

public interface IAutomationService
{
    Task<IReadOnlyList<AutomationRuleDto>> ListAsync(CancellationToken ct = default);
    Task<AutomationRuleDto?> SaveAsync(SaveAutomationRuleRequest request, Guid actorUserId, CancellationToken ct = default);
    Task<bool> DeleteAsync(Guid id, Guid actorUserId, CancellationToken ct = default);
    Task<bool> ToggleActiveAsync(Guid id, bool isActive, Guid actorUserId, CancellationToken ct = default);
}
