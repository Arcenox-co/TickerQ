using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MongoDB.Bson;
using MongoDB.Driver;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Models;

namespace TickerQ.MongoDB.Infrastructure;

internal sealed class MongoExecutionLogPersistence(IMongoCollection<BsonDocument> collection) : ITickerExecutionLogPersistence
{
    private readonly SemaphoreSlim _indexes = new(1, 1);
    private bool _indexed;
    private async Task EnsureIndexes(CancellationToken ct)
    {
        if (_indexed) return;
        await _indexes.WaitAsync(ct);
        try
        {
            if (_indexed) return;
            await collection.Indexes.CreateManyAsync(new[]
            {
                new CreateIndexModel<BsonDocument>(Builders<BsonDocument>.IndexKeys.Ascending("PartitionKey").Ascending("ExpiresAtUtcTicks")),
                new CreateIndexModel<BsonDocument>(Builders<BsonDocument>.IndexKeys.Ascending("PartitionKey").Descending("UpdatedAtUtcTicks").Descending("_id")),
                new CreateIndexModel<BsonDocument>(Builders<BsonDocument>.IndexKeys.Ascending("ExpiresAt"), new CreateIndexOptions { ExpireAfter = TimeSpan.Zero })
            }, ct);
            _indexed = true;
        }
        finally { _indexes.Release(); }
    }

    public async Task WriteAsync(IReadOnlyList<ExecutionLogArchive> records, CancellationToken cancellationToken)
    {
        if (records.Count == 0) return;
        await EnsureIndexes(cancellationToken);
        var writes = records.Select(row => new ReplaceOneModel<BsonDocument>(
            new BsonDocument { { "_id", row.Id }, { "UpdatedAtUtcTicks", new BsonDocument("$lte", row.UpdatedAtUtcTicks) } },
            new BsonDocument
            {
                { "_id", row.Id }, { "PartitionKey", row.PartitionKey }, { "TickerId", row.TickerId.ToString("N") },
                { "UpdatedAtUtcTicks", row.UpdatedAtUtcTicks }, { "ExpiresAtUtcTicks", row.ExpiresAtUtcTicks },
                { "ExpiresAt", new DateTime(row.ExpiresAtUtcTicks, DateTimeKind.Utc) },
                { "Payload", new BsonBinaryData(row.Payload) }, { "Truncated", row.Truncated }
            }) { IsUpsert = true }).ToArray();
        try { await collection.BulkWriteAsync(writes, new BulkWriteOptions { IsOrdered = false }, cancellationToken); }
        catch (MongoBulkWriteException<BsonDocument> ex) when (ex.WriteConcernError == null && ex.WriteErrors.Count > 0 && ex.WriteErrors.All(e => e.Code == 11000))
        {
            // A duplicate can be either an older retry or a concurrent first insert.
            // Reapply the version predicate without upsert so the newest snapshot wins in both cases.
            foreach (var error in ex.WriteErrors)
            {
                var write = writes[error.Index];
                await collection.ReplaceOneAsync(write.Filter, write.Replacement,
                    new ReplaceOptions { IsUpsert = false }, cancellationToken);
            }
        }
    }

    public async Task<ExecutionLogArchive> ReadAsync(string partitionKey, Guid tickerId, long nowUtcTicks, CancellationToken cancellationToken)
    {
        var doc = await collection.Find(new BsonDocument
        {
            { "_id", ExecutionLogArchive.StorageId(partitionKey, tickerId) }, { "PartitionKey", partitionKey },
            { "ExpiresAtUtcTicks", new BsonDocument("$gt", nowUtcTicks) }
        }).FirstOrDefaultAsync(cancellationToken);
        return doc == null ? null : new ExecutionLogArchive
        {
            Id = doc["_id"].AsString, PartitionKey = partitionKey, TickerId = tickerId,
            UpdatedAtUtcTicks = doc["UpdatedAtUtcTicks"].AsInt64, ExpiresAtUtcTicks = doc["ExpiresAtUtcTicks"].AsInt64,
            Payload = doc["Payload"].AsBsonBinaryData.Bytes, Truncated = doc["Truncated"].AsBoolean
        };
    }

    public async Task<IReadOnlyList<ExecutionLogArchive>> ReadBatchAsync(string partitionKey, IReadOnlyList<Guid> tickerIds,
        long nowUtcTicks, CancellationToken cancellationToken)
    {
        var docs = await collection.Find(new BsonDocument
        {
            { "_id", new BsonDocument("$in", new BsonArray(tickerIds.Select(id => ExecutionLogArchive.StorageId(partitionKey, id)))) },
            { "PartitionKey", partitionKey }, { "ExpiresAtUtcTicks", new BsonDocument("$gt", nowUtcTicks) }
        }).ToListAsync(cancellationToken);
        return docs.Select(doc => new ExecutionLogArchive
        {
            Id = doc["_id"].AsString, PartitionKey = partitionKey, TickerId = Guid.Parse(doc["TickerId"].AsString),
            UpdatedAtUtcTicks = doc["UpdatedAtUtcTicks"].AsInt64, ExpiresAtUtcTicks = doc["ExpiresAtUtcTicks"].AsInt64,
            Payload = doc["Payload"].AsBsonBinaryData.Bytes, Truncated = doc["Truncated"].AsBoolean
        }).ToArray();
    }

    public async Task PruneAsync(string partitionKey, long nowUtcTicks, int maxExecutions, int batchSize, CancellationToken cancellationToken)
    {
        await EnsureIndexes(cancellationToken);
        var expiredFilter = new BsonDocument { { "PartitionKey", partitionKey }, { "ExpiresAtUtcTicks", new BsonDocument("$lte", nowUtcTicks) } };
        var expired = await collection.Find(expiredFilter).Sort(new BsonDocument("ExpiresAtUtcTicks", 1))
            .Limit(batchSize).Project(new BsonDocument("_id", 1)).ToListAsync(cancellationToken);
        if (expired.Count > 0)
            await collection.DeleteManyAsync(Builders<BsonDocument>.Filter.And(expiredFilter,
                Builders<BsonDocument>.Filter.In("_id", expired.Select(x => x["_id"]))), cancellationToken);
        var excess = await collection.Find(new BsonDocument("PartitionKey", partitionKey))
            .Sort(new BsonDocument { { "UpdatedAtUtcTicks", -1 }, { "_id", -1 } })
            .Skip(maxExecutions).Limit(batchSize).Project(new BsonDocument { { "_id", 1 }, { "UpdatedAtUtcTicks", 1 } }).ToListAsync(cancellationToken);
        if (excess.Count > 0)
            await collection.DeleteManyAsync(Builders<BsonDocument>.Filter.Or(excess.Select(x =>
                (FilterDefinition<BsonDocument>)new BsonDocument { { "_id", x["_id"] }, { "PartitionKey", partitionKey }, { "UpdatedAtUtcTicks", x["UpdatedAtUtcTicks"] } })), cancellationToken);
    }
}
