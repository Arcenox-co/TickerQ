using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Models;

namespace TickerQ.Provider;

internal partial class TickerInMemoryPersistenceProvider<TTimeTicker, TCronTicker> : ITickerPromiseStateStore
{
    public Task<IReadOnlyList<TickerPromiseStateRow>> ListPromiseStatesAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_partitionState.PromisesLock)
        {
            IReadOnlyList<TickerPromiseStateRow> rows = _partitionState.Promises.Values
                .OrderBy(x => x.CreatedAtUtc).ThenBy(x => x.Id).Select(x => x.Copy()).ToArray();
            return Task.FromResult(rows);
        }
    }

    public Task<TickerPromiseStateRow> GetPromiseStateAsync(Guid id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_partitionState.PromisesLock)
            return Task.FromResult(_partitionState.Promises.TryGetValue(id, out var row) ? row.Copy() : null);
    }

    public Task<TickerPromiseStateRow> CreatePromiseStateAsync(TickerPromiseStateRow row, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(row);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_partitionState.PromisesLock)
        {
            if (_partitionState.Promises.ContainsKey(row.Id))
                throw new InvalidOperationException($"A promise state row with id '{row.Id}' already exists.");
            var now = _clock.UtcNow;
            var created = row.Copy();
            created.ApplicationNamespaceKey = _runtimePartitionKey;
            created.CreatedAtUtc = created.CreatedAtUtc == default ? now : created.CreatedAtUtc;
            created.UpdatedAtUtc = now;
            created.Version = 1;
            _partitionState.Promises.Add(created.Id, created);
            return Task.FromResult(created.Copy());
        }
    }

    public Task<TickerPromiseStateRow> UpdatePromiseStateAsync(TickerPromiseStateRow row, long expectedVersion,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(row);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_partitionState.PromisesLock)
        {
            if (!_partitionState.Promises.TryGetValue(row.Id, out var current) || current.Version != expectedVersion)
                return Task.FromResult<TickerPromiseStateRow>(null);
            var next = row.Copy();
            next.ApplicationNamespaceKey = _runtimePartitionKey;
            next.CreatedAtUtc = current.CreatedAtUtc;
            next.UpdatedAtUtc = _clock.UtcNow;
            next.Version = expectedVersion + 1;
            _partitionState.Promises[row.Id] = next;
            return Task.FromResult(next.Copy());
        }
    }
}
