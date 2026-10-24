using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TickerQ.Utilities.Exceptions;
using TickerQ.Utilities.Infrastructure;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Models;

namespace TickerQ.Utilities.Managers;

internal sealed class SchedulePolicyManager(
    ISchedulePolicyStore store,
    SchedulePolicyEvaluator evaluator,
    ITickerQHostScheduler scheduler) : ISchedulePolicyManager
{
    public Task<IReadOnlyList<SchedulePolicy>> ListAsync(CancellationToken cancellationToken = default)
        => store.ListSchedulePoliciesAsync(cancellationToken);

    public Task<SchedulePolicy> GetAsync(Guid id, CancellationToken cancellationToken = default)
        => store.GetSchedulePolicyAsync(id, cancellationToken);

    public async Task<SchedulePolicy> CreateAsync(SchedulePolicy policy,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(policy);
        var all = await store.ListSchedulePoliciesAsync(cancellationToken).ConfigureAwait(false);
        Validate(policy, all, null);
        var created = await store.CreateSchedulePolicyAsync(policy, cancellationToken).ConfigureAwait(false);
        scheduler.Restart();
        return created;
    }

    public async Task<SchedulePolicy> UpsertAsync(SchedulePolicy policy,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(policy);
        var existing = await store.GetSchedulePolicyByNameAsync(policy.Name, cancellationToken)
            .ConfigureAwait(false);
        if (existing == null)
        {
            try { return await CreateAsync(policy, cancellationToken).ConfigureAwait(false); }
            catch (DuplicateSchedulePolicyNameException)
            {
                existing = await store.GetSchedulePolicyByNameAsync(policy.Name, cancellationToken)
                    .ConfigureAwait(false);
                if (existing == null) throw;
            }
        }
        var replacement = policy.Copy();
        replacement.Id = existing.Id;
        return await UpdateAsync(replacement, existing.Version, cancellationToken).ConfigureAwait(false)
               ?? throw new InvalidOperationException(
                   $"Schedule policy '{existing.Name}' changed while it was being updated.");
    }

    public async Task<SchedulePolicy> UpdateAsync(SchedulePolicy policy, long expectedVersion,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(policy);
        var current = await store.GetSchedulePolicyAsync(policy.Id, cancellationToken).ConfigureAwait(false);
        if (current == null || current.Version != expectedVersion) return null;
        if (SchedulePolicyNames.Normalize(current.Name) != SchedulePolicyNames.Normalize(policy.Name))
            throw new TickerValidatorException(
                "Schedule policy names are stable identifiers and cannot be changed. Create a new policy instead.");
        var all = await store.ListSchedulePoliciesAsync(cancellationToken).ConfigureAwait(false);
        Validate(policy, all, policy.Id);
        var updated = await store.UpdateSchedulePolicyAsync(policy, expectedVersion, cancellationToken)
            .ConfigureAwait(false);
        if (updated != null) scheduler.Restart();
        return updated;
    }

    public async Task<bool> DeleteAsync(Guid id, long expectedVersion,
        CancellationToken cancellationToken = default)
    {
        var policy = await store.GetSchedulePolicyAsync(id, cancellationToken).ConfigureAwait(false);
        if (policy == null) return false;
        if (await store.IsSchedulePolicyInUseAsync(policy.Name, cancellationToken).ConfigureAwait(false))
            throw new TickerValidatorException(
                $"Schedule policy '{policy.Name}' cannot be deleted while a ticker references it.");
        var all = await store.ListSchedulePoliciesAsync(cancellationToken).ConfigureAwait(false);
        if (all.Any(x => x.Id != id && References(x, policy.Name)))
            throw new TickerValidatorException(
                $"Schedule policy '{policy.Name}' cannot be deleted while another policy references it.");
        var deleted = await store.DeleteSchedulePolicyAsync(id, expectedVersion, cancellationToken)
            .ConfigureAwait(false);
        if (deleted) scheduler.Restart();
        return deleted;
    }

    public async Task<ScheduleDecision> EvaluateAsync(string policyName, DateTime requestedAtUtc,
        CancellationToken cancellationToken = default)
    {
        var all = await store.ListSchedulePoliciesAsync(cancellationToken).ConfigureAwait(false);
        return evaluator.Evaluate(policyName, requestedAtUtc, all);
    }

    public async Task<ScheduleDecision> FindNextAllowedAsync(string policyName, DateTime requestedAtUtc,
        CancellationToken cancellationToken = default)
    {
        var all = await store.ListSchedulePoliciesAsync(cancellationToken).ConfigureAwait(false);
        return evaluator.FindNextAllowed(policyName, requestedAtUtc, all);
    }

    public async Task<IReadOnlyList<SchedulePolicyPreviewItem>> PreviewCronAsync(string policyName,
        string expression, DateTime fromUtc, int count = 20, string timeZoneId = null,
        CancellationToken cancellationToken = default)
    {
        if (count is < 1 or > 200) throw new ArgumentOutOfRangeException(nameof(count));
        var policies = await store.ListSchedulePoliciesAsync(cancellationToken).ConfigureAwait(false);
        var items = new List<SchedulePolicyPreviewItem>(count);
        var cursor = fromUtc;
        for (var i = 0; i < count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var next = CronScheduleCache.GetNextOccurrenceOrDefault(expression, cursor, timeZoneId);
            if (!next.HasValue) break;
            var decision = evaluator.Evaluate(policyName, next.Value, policies);
            items.Add(new SchedulePolicyPreviewItem
            {
                RequestedAtUtc = next.Value,
                EffectiveAtUtc = decision.IsAllowed ? next.Value : null,
                IsAllowed = decision.IsAllowed,
                Rule = decision.Rule,
                Reason = decision.Reason,
            });
            cursor = next.Value;
        }
        return items;
    }

    private void Validate(SchedulePolicy policy, IReadOnlyCollection<SchedulePolicy> existing, Guid? currentId)
    {
        policy.Name = policy.Name?.Trim();
        if (string.IsNullOrWhiteSpace(policy.Name) || policy.Name.Length > 200)
            throw new TickerValidatorException("Schedule policy name must contain 1 to 200 characters.");
        policy.TimeZoneId = string.IsNullOrWhiteSpace(policy.TimeZoneId) ? "UTC" : policy.TimeZoneId.Trim();
        if (policy.TimeZoneId.Length > 128)
            throw new TickerValidatorException("Schedule policy time zone must contain at most 128 characters.");
        if (policy.Description?.Length > 2000)
            throw new TickerValidatorException("Schedule policy description must contain at most 2,000 characters.");
        if (existing.Any(x => x.Id != currentId &&
            SchedulePolicyNames.Normalize(x.Name) == SchedulePolicyNames.Normalize(policy.Name)))
            throw new DuplicateSchedulePolicyNameException(policy.Name);
        try { _ = SchedulePolicyEvaluator.ResolveTimeZone(policy.TimeZoneId); }
        catch (Exception exception) when (exception is TimeZoneNotFoundException or InvalidTimeZoneException)
        { throw new TickerValidatorException($"Unknown or invalid time zone '{policy.TimeZoneId}'."); }
        if (policy.AllowedWeekdays == ScheduleWeekdays.None)
            throw new TickerValidatorException("A schedule policy must allow at least one weekday.");
        foreach (var window in policy.AllowedTimeWindows ?? new())
            if (window.Start < TimeSpan.Zero || window.Start >= TimeSpan.FromDays(1) ||
                window.End < TimeSpan.Zero || window.End >= TimeSpan.FromDays(1))
                throw new TickerValidatorException("Daily time windows must stay within a local calendar day.");
        foreach (var annual in policy.ExcludedAnnualDates ?? new())
            try { _ = new DateOnly(2000, annual.Month, annual.Day); }
            catch (ArgumentOutOfRangeException)
            { throw new TickerValidatorException($"Invalid annual date {annual.Month}/{annual.Day}."); }
        foreach (var provider in policy.DateProviderNames ?? new())
            if (!evaluator.DateProviderNames.Contains(SchedulePolicyNames.Normalize(provider)))
                throw new TickerValidatorException($"Schedule date provider '{provider}' is not registered.");

        var candidate = policy.Copy();
        var all = existing.Where(x => x.Id != currentId).Append(candidate).ToArray();
        var required = (candidate.RequiredPolicyNames ?? new())
            .Select(SchedulePolicyNames.Normalize).ToHashSet(StringComparer.Ordinal);
        if ((candidate.ExcludedWhenPolicyAllowsNames ?? new())
            .Any(name => required.Contains(SchedulePolicyNames.Normalize(name))))
            throw new TickerValidatorException(
                "A schedule policy cannot both require and exclude the same referenced policy.");
        foreach (var reference in (candidate.RequiredPolicyNames ?? new())
                     .Concat(candidate.ExcludedWhenPolicyAllowsNames ?? new()))
            if (!all.Any(x => SchedulePolicyNames.Normalize(x.Name) == SchedulePolicyNames.Normalize(reference)))
                throw new TickerValidatorException($"Referenced schedule policy '{reference}' does not exist.");
        ValidateCompositionGraph(candidate.Name, all);
    }

    private static void ValidateCompositionGraph(string rootName, IReadOnlyCollection<SchedulePolicy> policies)
    {
        var map = policies.ToDictionary(x => SchedulePolicyNames.Normalize(x.Name), StringComparer.Ordinal);
        var visiting = new HashSet<string>(StringComparer.Ordinal);
        var visited = new HashSet<string>(StringComparer.Ordinal);

        void Visit(string name)
        {
            var key = SchedulePolicyNames.Normalize(name);
            if (visited.Contains(key) || !map.TryGetValue(key, out var policy)) return;
            if (!visiting.Add(key))
                throw new TickerValidatorException(
                    $"Schedule policy composition contains a cycle at '{policy.Name}'.");
            foreach (var reference in (policy.RequiredPolicyNames ?? new())
                         .Concat(policy.ExcludedWhenPolicyAllowsNames ?? new()))
                Visit(reference);
            visiting.Remove(key);
            visited.Add(key);
        }

        Visit(rootName);
    }

    private static bool References(SchedulePolicy policy, string name)
        => (policy.RequiredPolicyNames ?? new()).Concat(policy.ExcludedWhenPolicyAllowsNames ?? new())
            .Any(x => SchedulePolicyNames.Normalize(x) == SchedulePolicyNames.Normalize(name));
}

internal sealed class UnsupportedSchedulePolicyStore(string providerName) : ISchedulePolicyStore
{
    private NotSupportedException Error() => new(
        $"Persistence provider '{providerName}' does not support schedule policies. " +
        "Use a built-in TickerQ provider or update the provider implementation.");
    public Task<IReadOnlyList<SchedulePolicy>> ListSchedulePoliciesAsync(CancellationToken cancellationToken = default) => Task.FromException<IReadOnlyList<SchedulePolicy>>(Error());
    public Task<SchedulePolicy> GetSchedulePolicyAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromException<SchedulePolicy>(Error());
    public Task<SchedulePolicy> GetSchedulePolicyByNameAsync(string name, CancellationToken cancellationToken = default) => Task.FromException<SchedulePolicy>(Error());
    public Task<SchedulePolicy> CreateSchedulePolicyAsync(SchedulePolicy policy, CancellationToken cancellationToken = default) => Task.FromException<SchedulePolicy>(Error());
    public Task<SchedulePolicy> UpdateSchedulePolicyAsync(SchedulePolicy policy, long expectedVersion, CancellationToken cancellationToken = default) => Task.FromException<SchedulePolicy>(Error());
    public Task<bool> DeleteSchedulePolicyAsync(Guid id, long expectedVersion, CancellationToken cancellationToken = default) => Task.FromException<bool>(Error());
    public Task<bool> IsSchedulePolicyInUseAsync(string name, CancellationToken cancellationToken = default) => Task.FromException<bool>(Error());
}
