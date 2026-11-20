using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using StackExchange.Redis;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Models;

namespace TickerQ.Caching.StackExchangeRedis.Infrastructure;

internal sealed partial class TickerRedisPersistenceProvider<TTimeTicker, TCronTicker> : ITickerPromiseStateStore
    where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
    where TCronTicker : CronTickerEntity, new()
{
    public async Task<IReadOnlyList<TickerPromiseStateRow>> ListPromiseStatesAsync(CancellationToken cancellationToken = default)
    {
        var rows = await Serializer.LoadAllFromSetAsync<TickerPromiseStateRow>(Keys.PromiseStateIds, Keys.PromiseState, cancellationToken)
            .ConfigureAwait(false);
        return rows.OrderBy(x => x.CreatedAtUtc).ThenBy(x => x.Id).ToArray();
    }

    public Task<TickerPromiseStateRow> GetPromiseStateAsync(Guid id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Serializer.GetAsync<TickerPromiseStateRow>(Keys.PromiseState(id));
    }

    public async Task<TickerPromiseStateRow> CreatePromiseStateAsync(TickerPromiseStateRow row, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(row);
        cancellationToken.ThrowIfCancellationRequested();
        var now = Clock.UtcNow;
        var created = row.Copy();
        created.ApplicationNamespaceKey = Keys.Partition.StorageKey;
        created.CreatedAtUtc = created.CreatedAtUtc == default ? now : created.CreatedAtUtc;
        created.UpdatedAtUtc = now;
        created.Version = 1;
        var transaction = Db.CreateTransaction();
        transaction.AddCondition(Condition.KeyNotExists(Keys.PromiseState(created.Id)));
        _ = transaction.StringSetAsync(Keys.PromiseState(created.Id), Serializer.Serialize(created));
        _ = transaction.SetAddAsync(Keys.PromiseStateIds, created.Id.ToString());
        if (!await transaction.ExecuteAsync().ConfigureAwait(false))
            throw new InvalidOperationException($"A promise state row with id '{row.Id}' already exists.");
        return created;
    }

    public async Task<TickerPromiseStateRow> UpdatePromiseStateAsync(TickerPromiseStateRow row, long expectedVersion,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(row);
        var current = await GetPromiseStateAsync(row.Id, cancellationToken).ConfigureAwait(false);
        if (current == null || current.Version != expectedVersion) return null;
        var next = row.Copy();
        next.ApplicationNamespaceKey = Keys.Partition.StorageKey;
        next.CreatedAtUtc = current.CreatedAtUtc;
        next.UpdatedAtUtc = Clock.UtcNow;
        next.Version = expectedVersion + 1;
        var key = Keys.PromiseState(row.Id);
        var transaction = Db.CreateTransaction();
        transaction.AddCondition(Condition.StringEqual(key, Serializer.Serialize(current)));
        _ = transaction.StringSetAsync(key, Serializer.Serialize(next));
        _ = transaction.SetAddAsync(Keys.PromiseStateIds, next.Id.ToString());
        return await transaction.ExecuteAsync().ConfigureAwait(false) ? next : null;
    }
}
