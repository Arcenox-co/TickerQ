using MongoDB.Driver;
using MongoDB.Bson;
using TickerQ.MongoDB.Infrastructure;
using TickerQ.Utilities;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Models;

namespace TickerQ.MongoDB.Tests;

[Collection("Mongo")]
public sealed class MongoApplicationPartitionTests(MongoTestFixture fixture) : IAsyncLifetime
{
    public Task InitializeAsync() => fixture.DropAllAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Identical_ids_are_isolated_across_time_and_cron_crud_and_acquisition()
    {
        var context = new TickerMongoContext<TimeTickerEntity, CronTickerEntity>(fixture.Database, "ticker_");
        var aOptions = QueueOnly("application-a");
        var bOptions = QueueOnly("application-b");
        var a = new TickerMongoPersistenceProvider<TimeTickerEntity, CronTickerEntity>(context, fixture.Clock, aOptions);
        var b = new TickerMongoPersistenceProvider<TimeTickerEntity, CronTickerEntity>(context, fixture.Clock, bOptions);
        var id = Guid.NewGuid();

        Assert.Equal(1, await a.AddTimeTickers([Time(id, "A")], CancellationToken.None));
        Assert.Equal(1, await b.AddTimeTickers([Time(id, "B")], CancellationToken.None));
        Assert.Equal("A", (await a.GetTimeTickerById(id)).Function);
        Assert.Equal("B", (await b.GetTimeTickerById(id)).Function);

        var aAcquired = Assert.Single(await a.AcquireImmediateTimeTickersAsync([id]));
        var bAcquired = Assert.Single(await b.AcquireImmediateTimeTickersAsync([id]));
        var completion = new InternalFunctionContext
        {
            TickerId = id,
            RuntimePartitionKey = new TickerQRuntimePartition("application-a").StorageKey,
            FunctionName = "A",
            Type = TickerType.TimeTicker,
            AcquisitionToken = aAcquired.AcquisitionToken,
            ChainRootId = id,
            ChainGeneration = aAcquired.ChainGeneration
        }.SetProperty(x => x.Status, TickerStatus.Done)
         .SetProperty(x => x.ReleaseLock, true)
         .SetProperty(x => x.ResultEnvelope,
             new TickerResultEnvelope([1], 1, "application/octet-stream"));
        Assert.True(await a.CommitSuccessfulTickerAsync(completion));
        Assert.Equal(1, (await a.GetTimeTickerResultAsync(id))!.ToPayloadArray()[0]);
        Assert.Null(await b.GetTimeTickerResultAsync(id));
        Assert.Equal(bAcquired.AcquisitionToken, (await b.GetTimeTickerById(id))!.AcquisitionToken);

        Assert.Equal(1, await a.InsertCronTickers([Cron(id, "A")], CancellationToken.None));
        Assert.Equal(1, await b.InsertCronTickers([Cron(id, "B")], CancellationToken.None));
        Assert.Equal("A", (await a.GetCronTickerById(id, CancellationToken.None)).Function);
        Assert.Equal("B", (await b.GetCronTickerById(id, CancellationToken.None)).Function);

        Assert.Equal(1, await a.RemoveCronTickers([id], CancellationToken.None));
        Assert.Null(await a.GetCronTickerById(id, CancellationToken.None));
        Assert.NotNull(await b.GetCronTickerById(id, CancellationToken.None));

        Assert.Equal(1, await a.RemoveTimeTickers([id]));
        Assert.Null(await a.GetTimeTickerById(id));
        Assert.NotNull(await b.GetTimeTickerById(id));
        Assert.Null(await a.GetTimeTickerResultAsync(id));
    }

    [Fact]
    public async Task Explicit_legacy_adoption_is_transactional_idempotent_and_owner_fenced()
    {
        var context = new TickerMongoContext<TimeTickerEntity, CronTickerEntity>(fixture.Database, "adoption_");
        var legacy = new TickerMongoPersistenceProvider<TimeTickerEntity, CronTickerEntity>(
            context, fixture.Clock, QueueOnly(null));
        var target = new TickerMongoPersistenceProvider<TimeTickerEntity, CronTickerEntity>(
            context, fixture.Clock, QueueOnly("mongo-adopter"));
        var id = Guid.NewGuid();
        await legacy.AddTimeTickers([Time(id, "legacy")]);
        await fixture.Database.GetCollection<BsonDocument>("adoption_TickerResults").InsertOneAsync(
            new BsonDocument
            {
                ["_id"] = new BsonBinaryData(id, GuidRepresentation.Standard),
                ["Kind"] = "time",
                ["Payload"] = new BsonBinaryData(new byte[] { 9 }),
                ["Version"] = 1,
                ["MediaType"] = "application/octet-stream"
            });
        var adoption = new LegacyRuntimePartitionAdoption(
            new TickerQRuntimePartition("mongo-adopter"), 41, legacyWritersDrained: true);

        target.AfterLegacyAdoptionLeaseForTestAsync = _ =>
            throw new OperationCanceledException("deterministic interruption");
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            target.AdoptLegacyRuntimePartitionAsync(adoption));
        var lateId = Guid.NewGuid();
        Assert.Equal(0, await legacy.AddTimeTickers([Time(lateId, "late-during-adoption")]));
        target.AfterLegacyAdoptionLeaseForTestAsync = null;
        await target.AdoptLegacyRuntimePartitionAsync(adoption);
        await target.AdoptLegacyRuntimePartitionAsync(adoption);
        Assert.Equal(0, await legacy.AddTimeTickers([Time(Guid.NewGuid(), "late-after-adoption")]));

        Assert.Equal("legacy", (await target.GetTimeTickerById(id))!.Function);
        Assert.Equal(9, (await target.GetTimeTickerResultAsync(id))!.ToPayloadArray()[0]);
        Assert.Null(await legacy.GetTimeTickerById(id));
        Assert.Null(await legacy.GetTimeTickerById(lateId));
        var other = new TickerMongoPersistenceProvider<TimeTickerEntity, CronTickerEntity>(
            context, fixture.Clock, QueueOnly("mongo-other"));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            other.AdoptLegacyRuntimePartitionAsync(
                new LegacyRuntimePartitionAdoption(new TickerQRuntimePartition("mongo-other"), 41,
                    legacyWritersDrained: true)));
    }

    [Fact]
    public async Task Repair_and_activation_fences_are_partition_local()
    {
        var context = new TickerMongoContext<TimeTickerEntity, CronTickerEntity>(fixture.Database, "ticker_");
        var aOptions = Scheduler("repair-a");
        var bOptions = Scheduler("repair-b");
        var a = new TickerMongoPersistenceProvider<TimeTickerEntity, CronTickerEntity>(context, fixture.Clock, aOptions);
        var b = new TickerMongoPersistenceProvider<TimeTickerEntity, CronTickerEntity>(context, fixture.Clock, bOptions);
        await a.BeginReconciliationActivationEpochAsync(1);
        await a.CommitReconciliationActivationEpochAsync(1);
        await b.BeginReconciliationActivationEpochAsync(1);
        await b.CommitReconciliationActivationEpochAsync(1);

        var sharedId = Guid.NewGuid();
        var aRow = Time(sharedId, "A");
        var bRow = Time(sharedId, "B");
        aRow.ApplicationNamespaceKey = aOptions.RuntimePartition!.StorageKey;
        bRow.ApplicationNamespaceKey = bOptions.RuntimePartition!.StorageKey;
        aRow.ChainRootId = Guid.NewGuid();
        bRow.ChainRootId = Guid.NewGuid();
        await fixture.TimeTickers.InsertManyAsync([aRow, bRow]);

        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        a.AfterGraphMutationFenceForTestAsync = async _ =>
        {
            entered.TrySetResult();
            await release.Task;
        };
        try
        {
            var repairA = a.RepairTimeTickerChainsAsync();
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var bInsert = b.AddTimeTickers([Time(Guid.NewGuid(), "B-independent")]);
            Assert.Equal(1, await bInsert.WaitAsync(TimeSpan.FromSeconds(2)));
            release.TrySetResult();
            await repairA;
        }
        finally
        {
            release.TrySetResult();
            a.AfterGraphMutationFenceForTestAsync = null;
        }

        var persistedA = await a.GetTimeTickerById(sharedId);
        var persistedB = await b.GetTimeTickerById(sharedId);
        Assert.Equal(sharedId, persistedA!.ChainRootId);
        Assert.NotEqual(sharedId, persistedB!.ChainRootId);
    }

    [Fact]
    public async Task Every_runtime_secondary_index_begins_with_partition_key()
    {
        await AssertPartitionLeadingIndexes(fixture.TimeTickers);
        await AssertPartitionLeadingIndexes(fixture.CronTickers);
        await AssertPartitionLeadingIndexes(fixture.CronTickerOccurrences);
        await AssertPartitionLeadingIndexes(
            fixture.Database.GetCollection<BsonDocument>("ticker_TickerResults"));
        await AssertPartitionLeadingIndexes(fixture.NodeFinalizations);
    }

    private static async Task AssertPartitionLeadingIndexes<T>(IMongoCollection<T> collection)
    {
        var indexes = await (await collection.Indexes.ListAsync()).ToListAsync();
        foreach (var index in indexes.Where(x => x["name"].AsString != "_id_"))
            Assert.Equal("ApplicationNamespaceKey", index["key"].AsBsonDocument.GetElement(0).Name);
    }

    private static SchedulerOptionsBuilder QueueOnly(string? applicationNamespace)
    {
        var options = new SchedulerOptionsBuilder();
        options.BindRuntimeActivationScope(applicationNamespace, 1, false);
        return options;
    }

    private static SchedulerOptionsBuilder Scheduler(string applicationNamespace)
    {
        var options = new SchedulerOptionsBuilder();
        options.BindRuntimeActivationScope(applicationNamespace, 1, true);
        return options;
    }

    private static TimeTickerEntity Time(Guid id, string function) => new()
    {
        Id = id,
        Function = function,
        Request = [],
        Status = TickerStatus.Idle,
        ExecutionTime = DateTime.UtcNow,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    };

    private static CronTickerEntity Cron(Guid id, string function) => new()
    {
        Id = id,
        Function = function,
        Expression = "* * * * *",
        Request = [],
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    };
}
