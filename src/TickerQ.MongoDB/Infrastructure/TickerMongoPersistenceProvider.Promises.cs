using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MongoDB.Driver;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Models;

namespace TickerQ.MongoDB.Infrastructure;

internal sealed partial class TickerMongoPersistenceProvider<TTimeTicker, TCronTicker> : ITickerPromiseStateStore
    where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
    where TCronTicker : CronTickerEntity, new()
{
    private FilterDefinition<TickerPromiseStateRow> PromiseInPartition(Guid id)
        => Builders<TickerPromiseStateRow>.Filter.And(
            Builders<TickerPromiseStateRow>.Filter.Eq(x => x.ApplicationNamespaceKey, _runtimePartitionKey),
            Builders<TickerPromiseStateRow>.Filter.Eq(x => x.Id, id));

    public async Task<IReadOnlyList<TickerPromiseStateRow>> ListPromiseStatesAsync(CancellationToken cancellationToken = default)
        => await _context.Promises
            .Find(Builders<TickerPromiseStateRow>.Filter.Eq(x => x.ApplicationNamespaceKey, _runtimePartitionKey))
            .SortBy(x => x.CreatedAtUtc).ThenBy(x => x.Id)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

    public Task<TickerPromiseStateRow> GetPromiseStateAsync(Guid id, CancellationToken cancellationToken = default)
        => _context.Promises.Find(PromiseInPartition(id)).FirstOrDefaultAsync(cancellationToken);

    public async Task<TickerPromiseStateRow> CreatePromiseStateAsync(TickerPromiseStateRow row, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(row);
        var now = _clock.UtcNow;
        var created = row.Copy();
        created.ApplicationNamespaceKey = _runtimePartitionKey;
        created.CreatedAtUtc = created.CreatedAtUtc == default ? now : created.CreatedAtUtc;
        created.UpdatedAtUtc = now;
        created.Version = 1;
        try
        {
            await _context.Promises.InsertOneAsync(created, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (MongoWriteException exception) when (exception.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            throw new InvalidOperationException($"A promise state row with id '{row.Id}' already exists.", exception);
        }
        return created;
    }

    public Task<TickerPromiseStateRow> UpdatePromiseStateAsync(TickerPromiseStateRow row, long expectedVersion,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(row);
        var filter = PromiseInPartition(row.Id) & Builders<TickerPromiseStateRow>.Filter.Eq(x => x.Version, expectedVersion);
        var update = Builders<TickerPromiseStateRow>.Update
            .Set(x => x.Name, row.Name).Set(x => x.StateJson, row.StateJson)
            .Set(x => x.UpdatedAtUtc, _clock.UtcNow).Set(x => x.Version, expectedVersion + 1);
        return _context.Promises.FindOneAndUpdateAsync(filter, update,
            new FindOneAndUpdateOptions<TickerPromiseStateRow> { ReturnDocument = ReturnDocument.After }, cancellationToken);
    }
}
