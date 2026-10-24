using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using StackExchange.Redis;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Infrastructure;
using TickerQ.Utilities.Models;

namespace TickerQ.Caching.StackExchangeRedis.Infrastructure;

internal sealed partial class TickerRedisPersistenceProvider<TTimeTicker, TCronTicker>
    where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
    where TCronTicker : CronTickerEntity, new()
{
    public async Task<IReadOnlyList<SchedulePolicy>> ListSchedulePoliciesAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var ids = await Db.SetMembersAsync(Keys.SchedulePolicyIds).ConfigureAwait(false);
        if (ids.Length == 0) return Array.Empty<SchedulePolicy>();
        var values = await Db.StringGetAsync(ids.Select(x =>
                (RedisKey)Keys.SchedulePolicy(Guid.Parse(x.ToString()))).ToArray())
            .ConfigureAwait(false);
        return values.Where(x => x.HasValue).Select(x => SchedulePolicySerializer.Deserialize(x!))
            .Where(x => x != null).OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public async Task<SchedulePolicy> GetSchedulePolicyAsync(Guid id,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var value = await Db.StringGetAsync(Keys.SchedulePolicy(id)).ConfigureAwait(false);
        return value.HasValue ? SchedulePolicySerializer.Deserialize(value!) : null;
    }

    public async Task<SchedulePolicy> GetSchedulePolicyByNameAsync(string name,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var id = await Db.StringGetAsync(Keys.SchedulePolicyName(SchedulePolicyNames.CreateKey(name)))
            .ConfigureAwait(false);
        return id.HasValue && Guid.TryParse(id.ToString(), out var parsed)
            ? await GetSchedulePolicyAsync(parsed, cancellationToken).ConfigureAwait(false) : null;
    }

    public async Task<SchedulePolicy> CreateSchedulePolicyAsync(SchedulePolicy policy,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(policy);
        cancellationToken.ThrowIfCancellationRequested();
        var row = policy.Copy();
        row.Id = row.Id == Guid.Empty ? Guid.NewGuid() : row.Id;
        row.ApplicationNamespaceKey = Keys.Partition.StorageKey;
        row.NameKey = SchedulePolicyNames.CreateKey(row.Name);
        row.CreatedAtUtc = row.CreatedAtUtc == default ? Clock.UtcNow : row.CreatedAtUtc;
        row.UpdatedAtUtc = Clock.UtcNow;
        row.Version = 1;
        var tx = Db.CreateTransaction();
        tx.AddCondition(Condition.KeyNotExists(Keys.SchedulePolicy(row.Id)));
        tx.AddCondition(Condition.KeyNotExists(Keys.SchedulePolicyName(row.NameKey)));
        _ = tx.StringSetAsync(Keys.SchedulePolicy(row.Id), SchedulePolicySerializer.Serialize(row));
        _ = tx.StringSetAsync(Keys.SchedulePolicyName(row.NameKey), row.Id.ToString());
        _ = tx.SetAddAsync(Keys.SchedulePolicyIds, row.Id.ToString());
        if (!await tx.ExecuteAsync().ConfigureAwait(false))
        {
            if (await Db.KeyExistsAsync(Keys.SchedulePolicyName(row.NameKey)).ConfigureAwait(false))
                throw new DuplicateSchedulePolicyNameException(row.Name);
            throw new InvalidOperationException($"A schedule policy with id '{row.Id}' already exists.");
        }
        return row;
    }

    public async Task<SchedulePolicy> UpdateSchedulePolicyAsync(SchedulePolicy policy, long expectedVersion,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(policy);
        var current = await GetSchedulePolicyAsync(policy.Id, cancellationToken).ConfigureAwait(false);
        if (current == null || current.Version != expectedVersion) return null;
        var next = policy.Copy();
        next.ApplicationNamespaceKey = Keys.Partition.StorageKey;
        next.NameKey = SchedulePolicyNames.CreateKey(next.Name);
        next.CreatedAtUtc = current.CreatedAtUtc;
        next.UpdatedAtUtc = Clock.UtcNow;
        next.Version = expectedVersion + 1;
        var oldJson = SchedulePolicySerializer.Serialize(current);
        var tx = Db.CreateTransaction();
        tx.AddCondition(Condition.StringEqual(Keys.SchedulePolicy(policy.Id), oldJson));
        if (current.NameKey != next.NameKey)
            tx.AddCondition(Condition.KeyNotExists(Keys.SchedulePolicyName(next.NameKey)));
        _ = tx.StringSetAsync(Keys.SchedulePolicy(policy.Id), SchedulePolicySerializer.Serialize(next));
        _ = tx.StringSetAsync(Keys.SchedulePolicyName(next.NameKey), next.Id.ToString());
        if (current.NameKey != next.NameKey) _ = tx.KeyDeleteAsync(Keys.SchedulePolicyName(current.NameKey));
        if (await tx.ExecuteAsync().ConfigureAwait(false)) return next;
        if (await Db.KeyExistsAsync(Keys.SchedulePolicyName(next.NameKey)).ConfigureAwait(false))
            throw new DuplicateSchedulePolicyNameException(next.Name);
        return null;
    }

    public async Task<bool> DeleteSchedulePolicyAsync(Guid id, long expectedVersion,
        CancellationToken cancellationToken = default)
    {
        var current = await GetSchedulePolicyAsync(id, cancellationToken).ConfigureAwait(false);
        if (current == null || current.Version != expectedVersion) return false;
        var tx = Db.CreateTransaction();
        tx.AddCondition(Condition.StringEqual(Keys.SchedulePolicy(id), SchedulePolicySerializer.Serialize(current)));
        _ = tx.KeyDeleteAsync(Keys.SchedulePolicy(id));
        _ = tx.KeyDeleteAsync(Keys.SchedulePolicyName(current.NameKey));
        _ = tx.SetRemoveAsync(Keys.SchedulePolicyIds, id.ToString());
        return await tx.ExecuteAsync().ConfigureAwait(false);
    }

    public async Task<bool> IsSchedulePolicyInUseAsync(string name,
        CancellationToken cancellationToken = default)
    {
        var normalized = SchedulePolicyNames.Normalize(name);
        var times = await Serializer.LoadAllFromSetAsync<TTimeTicker>(
            Keys.TimeTickerIds, Keys.TimeTicker, cancellationToken).ConfigureAwait(false);
        if (times.Any(x => SchedulePolicyNames.Normalize(x.SchedulePolicyName) == normalized)) return true;
        var crons = await Serializer.LoadAllFromSetAsync<TCronTicker>(
            Keys.CronIds, Keys.Cron, cancellationToken).ConfigureAwait(false);
        return crons.Any(x => SchedulePolicyNames.Normalize(x.SchedulePolicyName) == normalized);
    }
}
