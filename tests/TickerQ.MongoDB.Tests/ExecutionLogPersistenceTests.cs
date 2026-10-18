using MongoDB.Bson;
using MongoDB.Driver;
using TickerQ.MongoDB.Infrastructure;
using TickerQ.Tests.Shared;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Models;

namespace TickerQ.MongoDB.Tests;

[Collection("Mongo")]
public sealed class ExecutionLogPersistenceTests(MongoTestFixture fixture) : ExecutionLogPersistenceContract
{
    private readonly string _collection = "ExecutionLogs_" + Guid.NewGuid().ToString("N");
    protected override ITickerExecutionLogPersistence NewStore() => new MongoExecutionLogPersistence(fixture.Database.GetCollection<BsonDocument>(_collection));

    [Fact]
    public async Task Concurrent_first_writes_keep_the_newest_version()
    {
        var store = NewStore(); var id = Guid.NewGuid(); var now = DateTime.UtcNow.Ticks;
        var rows = Enumerable.Range(0, 24).Select(i => new ExecutionLogArchive
        {
            Id = ExecutionLogArchive.StorageId("concurrent", id), PartitionKey = "concurrent", TickerId = id,
            UpdatedAtUtcTicks = now + i, ExpiresAtUtcTicks = now + TimeSpan.TicksPerDay, Payload = [(byte)i]
        }).ToArray();
        await Task.WhenAll(rows.Select(row => store.WriteAsync([row], CancellationToken.None)));
        Assert.Equal(rows[^1].Payload, (await store.ReadAsync("concurrent", id, now, CancellationToken.None)).Payload);
    }
}
