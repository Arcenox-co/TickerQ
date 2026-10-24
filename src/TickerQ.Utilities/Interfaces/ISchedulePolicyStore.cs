using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TickerQ.Utilities.Models;

namespace TickerQ.Utilities.Interfaces;

/// <summary>Optional provider capability for durable, namespace-scoped schedule policies.</summary>
public interface ISchedulePolicyStore
{
    Task<IReadOnlyList<SchedulePolicy>> ListSchedulePoliciesAsync(CancellationToken cancellationToken = default);
    Task<SchedulePolicy> GetSchedulePolicyAsync(Guid id, CancellationToken cancellationToken = default);
    Task<SchedulePolicy> GetSchedulePolicyByNameAsync(string name, CancellationToken cancellationToken = default);
    Task<SchedulePolicy> CreateSchedulePolicyAsync(SchedulePolicy policy, CancellationToken cancellationToken = default);
    Task<SchedulePolicy> UpdateSchedulePolicyAsync(SchedulePolicy policy, long expectedVersion,
        CancellationToken cancellationToken = default);
    Task<bool> DeleteSchedulePolicyAsync(Guid id, long expectedVersion, CancellationToken cancellationToken = default);
    Task<bool> IsSchedulePolicyInUseAsync(string name, CancellationToken cancellationToken = default);
}

public interface IScheduleDateProvider
{
    string Name { get; }
    bool IsExcluded(DateOnly localDate);
    string GetReason(DateOnly localDate) => $"Excluded by {Name}.";
}

public interface ISchedulePolicyManager
{
    Task<IReadOnlyList<SchedulePolicy>> ListAsync(CancellationToken cancellationToken = default);
    Task<SchedulePolicy> GetAsync(Guid id, CancellationToken cancellationToken = default);
    Task<SchedulePolicy> CreateAsync(SchedulePolicy policy, CancellationToken cancellationToken = default);
    /// <summary>Creates the policy or updates the existing policy with the same normalized name.</summary>
    Task<SchedulePolicy> UpsertAsync(SchedulePolicy policy, CancellationToken cancellationToken = default);
    Task<SchedulePolicy> UpdateAsync(SchedulePolicy policy, long expectedVersion,
        CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(Guid id, long expectedVersion, CancellationToken cancellationToken = default);
    Task<ScheduleDecision> EvaluateAsync(string policyName, DateTime requestedAtUtc,
        CancellationToken cancellationToken = default);
    Task<ScheduleDecision> FindNextAllowedAsync(string policyName, DateTime requestedAtUtc,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SchedulePolicyPreviewItem>> PreviewCronAsync(string policyName, string expression,
        DateTime fromUtc, int count = 20, string timeZoneId = null, CancellationToken cancellationToken = default);
}
