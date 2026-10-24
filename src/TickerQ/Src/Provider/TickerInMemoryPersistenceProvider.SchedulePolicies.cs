using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Models;

namespace TickerQ.Provider;

internal partial class TickerInMemoryPersistenceProvider<TTimeTicker, TCronTicker>
    where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
    where TCronTicker : CronTickerEntity, new()
{
    public Task<IReadOnlyList<SchedulePolicy>> ListSchedulePoliciesAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_partitionState.SchedulePoliciesLock)
            return Task.FromResult<IReadOnlyList<SchedulePolicy>>(_partitionState.SchedulePolicies.Values
                .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).Select(x => x.Copy()).ToArray());
    }

    public Task<SchedulePolicy> GetSchedulePolicyAsync(Guid id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_partitionState.SchedulePoliciesLock)
            return Task.FromResult(_partitionState.SchedulePolicies.TryGetValue(id, out var row) ? row.Copy() : null);
    }

    public Task<SchedulePolicy> GetSchedulePolicyByNameAsync(string name,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var key = SchedulePolicyNames.CreateKey(name);
        lock (_partitionState.SchedulePoliciesLock)
            return Task.FromResult(_partitionState.SchedulePolicies.Values.FirstOrDefault(x => x.NameKey == key)?.Copy());
    }

    public Task<SchedulePolicy> CreateSchedulePolicyAsync(SchedulePolicy policy,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(policy);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_partitionState.SchedulePoliciesLock)
        {
            var row = policy.Copy();
            row.Id = row.Id == Guid.Empty ? Guid.NewGuid() : row.Id;
            row.ApplicationNamespaceKey = _runtimePartitionKey;
            row.NameKey = SchedulePolicyNames.CreateKey(row.Name);
            if (_partitionState.SchedulePolicies.Values.Any(x => x.NameKey == row.NameKey))
                throw new DuplicateSchedulePolicyNameException(row.Name);
            row.CreatedAtUtc = row.CreatedAtUtc == default ? _clock.UtcNow : row.CreatedAtUtc;
            row.UpdatedAtUtc = _clock.UtcNow;
            row.Version = 1;
            if (!_partitionState.SchedulePolicies.TryAdd(row.Id, row))
                throw new InvalidOperationException($"A schedule policy with id '{row.Id}' already exists.");
            return Task.FromResult(row.Copy());
        }
    }

    public Task<SchedulePolicy> UpdateSchedulePolicyAsync(SchedulePolicy policy, long expectedVersion,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(policy);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_partitionState.SchedulePoliciesLock)
        {
            if (!_partitionState.SchedulePolicies.TryGetValue(policy.Id, out var current) ||
                current.Version != expectedVersion) return Task.FromResult<SchedulePolicy>(null);
            var nameKey = SchedulePolicyNames.CreateKey(policy.Name);
            if (_partitionState.SchedulePolicies.Values.Any(x => x.Id != policy.Id && x.NameKey == nameKey))
                throw new DuplicateSchedulePolicyNameException(policy.Name);
            var next = policy.Copy();
            next.ApplicationNamespaceKey = _runtimePartitionKey;
            next.NameKey = nameKey;
            next.CreatedAtUtc = current.CreatedAtUtc;
            next.UpdatedAtUtc = _clock.UtcNow;
            next.Version = expectedVersion + 1;
            _partitionState.SchedulePolicies[next.Id] = next;
            return Task.FromResult(next.Copy());
        }
    }

    public Task<bool> DeleteSchedulePolicyAsync(Guid id, long expectedVersion,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_partitionState.SchedulePoliciesLock)
        {
            if (!_partitionState.SchedulePolicies.TryGetValue(id, out var current) || current.Version != expectedVersion)
                return Task.FromResult(false);
            return Task.FromResult(_partitionState.SchedulePolicies.Remove(id));
        }
    }

    public Task<bool> IsSchedulePolicyInUseAsync(string name, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var normalized = SchedulePolicyNames.Normalize(name);
        return Task.FromResult(TimeTickers.Values.Any(x =>
                SchedulePolicyNames.Normalize(x.SchedulePolicyName) == normalized) ||
            CronTickers.Values.Any(x => SchedulePolicyNames.Normalize(x.SchedulePolicyName) == normalized));
    }
}
