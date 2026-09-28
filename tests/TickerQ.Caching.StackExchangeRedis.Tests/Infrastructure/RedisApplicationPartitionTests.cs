using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using StackExchange.Redis;
using TickerQ.Caching.StackExchangeRedis.Helpers;
using TickerQ.Caching.StackExchangeRedis.Infrastructure;
using TickerQ.Utilities;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Models;
using static TickerQ.Caching.StackExchangeRedis.DependencyInjection.ServiceExtension;

namespace TickerQ.Caching.StackExchangeRedis.Tests.Infrastructure;

public sealed class RedisKeyBuilderPartitionTests
{
    [Fact]
    public void Every_runtime_key_has_one_shared_partition_tag_and_partitions_are_distinct()
    {
        var id = Guid.NewGuid();
        var a = new RedisKeyBuilder(new TickerQRuntimePartition("redis-a"));
        var b = new RedisKeyBuilder(new TickerQRuntimePartition("redis-b"));
        var keys = new[]
        {
            a.TimeTickerIds, a.TimeTickerPending, a.CronIds, a.CronOccurrenceIds,
            a.CronOccurrencePending, a.TimeTicker(id), a.TimeTickerResult(id), a.Cron(id),
            a.CronOccurrence(id), a.CronOccurrenceResult(id), a.CronOccurrencesByCron(id),
            a.CronOccurrenceSlot(id, DateTime.UtcNow), a.ReconciliationActivationMetadata,
            a.CronRepairQuarantine, a.TerminalMutationEvidence, a.NodeFinalizationRecords,
            a.NodeFinalizationDue, a.NodesRegistry, a.Heartbeat("node")
        };

        Assert.All(keys, key =>
        {
            Assert.Equal(1, key.Count(c => c == '{'));
            Assert.Equal(1, key.Count(c => c == '}'));
            Assert.Contains(a.HashTag, key, StringComparison.Ordinal);
            Assert.DoesNotContain(b.HashTag, key, StringComparison.Ordinal);
        });
        Assert.NotEqual(a.TimeTicker(id), b.TimeTicker(id));
        Assert.NotEqual(a.ReconciliationActivationMetadata, b.ReconciliationActivationMetadata);
        Assert.NotEqual(a.TerminalMutationEvidence, b.TerminalMutationEvidence);
        Assert.NotEqual(a.NodeFinalizationRecords, b.NodeFinalizationRecords);
        Assert.NotEqual(a.NodesRegistry, b.NodesRegistry);
    }
}

[Collection("RedisRealScript")]
public sealed class RedisApplicationPartitionTests(RedisRealScriptFixture fixture) : IAsyncLifetime
{
    private static readonly DateTime Now = new(2026, 9, 27, 10, 0, 0, DateTimeKind.Utc);

    public Task InitializeAsync() => fixture.Db.ExecuteAsync("FLUSHALL");
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Explicit_legacy_adoption_is_resumable_idempotent_and_owner_fenced()
    {
        var id = Guid.NewGuid();
        var legacy = Provider(null);
        var target = Provider("redis-adopter");
        await legacy.AddTimeTickers([Ticker(id, "legacy")]);
        var adoption = new LegacyRuntimePartitionAdoption(
            new TickerQRuntimePartition("redis-adopter"), 22, legacyWritersDrained: true);
        var lateId = Guid.NewGuid();
        target.AfterLegacyAdoptionFenceForTestAsync = async _ =>
        {
            Assert.Equal(0, await legacy.AddTimeTickers([Ticker(lateId, "late-during-adoption")]));
            throw new OperationCanceledException("deterministic interruption after legacy write fence");
        };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            target.AdoptLegacyRuntimePartitionAsync(adoption));
        Assert.Equal(0, await legacy.AddTimeTickers([Ticker(Guid.NewGuid(), "late-after-interruption")]));

        target.AfterLegacyAdoptionFenceForTestAsync = null;
        await target.AdoptLegacyRuntimePartitionAsync(adoption);
        await target.AdoptLegacyRuntimePartitionAsync(adoption);

        Assert.Equal("legacy", (await target.GetTimeTickerById(id))!.Function);
        Assert.Null(await legacy.GetTimeTickerById(id));
        Assert.Null(await legacy.GetTimeTickerById(lateId));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Provider("redis-other").AdoptLegacyRuntimePartitionAsync(
                new LegacyRuntimePartitionAdoption(new TickerQRuntimePartition("redis-other"), 22,
                    legacyWritersDrained: true)));
    }

    [Fact]
    public async Task Adoption_fences_delayed_post_primary_indexes_and_rebuilds_target_from_documents()
    {
        var id = Guid.NewGuid();
        var legacy = Provider(null);
        var target = Provider("redis-race-target");
        await legacy.AddTimeTickers([Ticker(id, "legacy-race")]);
        var cron = new CronTickerEntity
        {
            Id = Guid.NewGuid(), Function = "legacy-cron", Expression = "* * * * *", Request = []
        };
        Assert.Equal(1, await legacy.InsertCronTickers([cron], CancellationToken.None));
        var occurrence = new CronTickerOccurrenceEntity<CronTickerEntity>
        {
            Id = Guid.NewGuid(), CronTickerId = cron.Id, DefinitionRevision = cron.DefinitionRevision,
            Status = TickerStatus.Idle, ExecutionTime = Now.AddMinutes(5),
            CreatedAt = Now, UpdatedAt = Now
        };
        Assert.Equal(1, await legacy.InsertCronTickerOccurrences([occurrence], CancellationToken.None));
        var acquired = Assert.Single(await legacy.AcquireImmediateTimeTickersAsync([id]));
        var primaryCommitted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resumeIndexPublication = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        legacy.AfterTerminalMutationScriptEvaluatedAsync = async () =>
        {
            primaryCommitted.TrySetResult();
            await resumeIndexPublication.Task;
        };
        var terminal = new InternalFunctionContext
        {
            TickerId = id,
            Type = TickerType.TimeTicker,
            AcquisitionToken = acquired.AcquisitionToken
        }.SetProperty(x => x.Status, TickerStatus.Failed)
         .SetProperty(x => x.ExecutedAt, Now)
         .SetProperty(x => x.ReleaseLock, true);

        var delayedMutation = legacy.CommitTerminalTickerAsync(terminal);
        await primaryCommitted.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await target.AdoptLegacyRuntimePartitionAsync(new LegacyRuntimePartitionAdoption(
            new TickerQRuntimePartition("redis-race-target"), 23, legacyWritersDrained: true));
        resumeIndexPublication.TrySetResult();
        Assert.True(await delayedMutation);

        var legacyKeys = new RedisKeyBuilder(TickerQRuntimePartition.LegacyGlobal);
        var targetKeys = new RedisKeyBuilder(new TickerQRuntimePartition("redis-race-target"));
        var remainingLegacyKeys = new List<string>();
        foreach (var endpoint in fixture.Db.Multiplexer.GetEndPoints())
        {
            var server = fixture.Db.Multiplexer.GetServer(endpoint);
            if (!server.IsConnected || server.IsReplica) continue;
            await foreach (var key in server.KeysAsync(
                               fixture.Db.Database, pattern: legacyKeys.PartitionPrefix + ":*"))
                remainingLegacyKeys.Add(key.ToString());
        }

        Assert.Equal([legacyKeys.ReconciliationActivationMetadata],
            remainingLegacyKeys.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray());
        Assert.Equal("completed", (string?)await fixture.Db.HashGetAsync(
            legacyKeys.ReconciliationActivationMetadata, "legacyAdoptionState"));
        Assert.Equal("legacy-race", Assert.Single(await target.GetTimeTickers(
            x => x.Id == id, CancellationToken.None)).Function);
        Assert.True(await fixture.Db.SetContainsAsync(targetKeys.TimeTickerIds, id.ToString()));
        Assert.Null(await fixture.Db.SortedSetScoreAsync(targetKeys.TimeTickerPending, id.ToString()));
        Assert.NotNull(await fixture.Db.SortedSetScoreAsync(
            targetKeys.TimeTickerRetentionFailed, id.ToString()));
        Assert.Equal("legacy-cron", Assert.Single(await target.GetCronTickers(
            x => x.Id == cron.Id, CancellationToken.None)).Function);
        Assert.Equal(occurrence.Id, Assert.Single(await target.GetAllCronTickerOccurrences(
            x => x.Id == occurrence.Id, CancellationToken.None)).Id);
        Assert.True(await fixture.Db.SetContainsAsync(targetKeys.CronIds, cron.Id.ToString()));
        Assert.True(await fixture.Db.SetContainsAsync(targetKeys.CronOccurrenceIds, occurrence.Id.ToString()));
        Assert.True(await fixture.Db.SetContainsAsync(
            targetKeys.CronOccurrencesByCron(cron.Id), occurrence.Id.ToString()));
        Assert.NotNull(await fixture.Db.SortedSetScoreAsync(
            targetKeys.CronOccurrencePending, occurrence.Id.ToString()));
    }

    [Fact]
    public async Task Adoption_fences_delayed_unified_context_document_and_index_write_atomically()
    {
        var id = Guid.NewGuid();
        var legacy = Provider(null);
        var target = Provider("redis-unified-race-target");
        await legacy.AddTimeTickers([Ticker(id, "legacy-unified-race")]);
        var documentsLoaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resumeWriter = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        legacy.AfterUnifiedContextDocumentsLoadedAsync = async () =>
        {
            documentsLoaded.TrySetResult();
            await resumeWriter.Task;
        };
        var skipped = new InternalFunctionContext
        {
            TickerId = id,
            Type = TickerType.TimeTicker,
            ExceptionDetails = "unified skip"
        }.SetProperty(x => x.Status, TickerStatus.Skipped)
         .SetProperty(x => x.ExecutedAt, Now);

        var delayedMutation = legacy.UpdateTimeTickersWithUnifiedContext([id], skipped, CancellationToken.None);
        await documentsLoaded.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await target.AdoptLegacyRuntimePartitionAsync(new LegacyRuntimePartitionAdoption(
            new TickerQRuntimePartition("redis-unified-race-target"), 24, legacyWritersDrained: true));
        resumeWriter.TrySetResult();
        await delayedMutation;

        var legacyKeys = new RedisKeyBuilder(TickerQRuntimePartition.LegacyGlobal);
        var targetKeys = new RedisKeyBuilder(new TickerQRuntimePartition("redis-unified-race-target"));
        var remainingLegacyKeys = new List<string>();
        foreach (var endpoint in fixture.Db.Multiplexer.GetEndPoints())
        {
            var server = fixture.Db.Multiplexer.GetServer(endpoint);
            if (!server.IsConnected || server.IsReplica) continue;
            await foreach (var key in server.KeysAsync(
                               fixture.Db.Database, pattern: legacyKeys.PartitionPrefix + ":*"))
                remainingLegacyKeys.Add(key.ToString());
        }

        Assert.Equal([legacyKeys.ReconciliationActivationMetadata],
            remainingLegacyKeys.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray());
        var adopted = Assert.Single(await target.GetTimeTickers(
            x => x.Id == id, CancellationToken.None));
        Assert.Equal("legacy-unified-race", adopted.Function);
        Assert.Equal(TickerStatus.Idle, adopted.Status);
        Assert.True(await fixture.Db.SetContainsAsync(targetKeys.TimeTickerIds, id.ToString()));
        Assert.NotNull(await fixture.Db.SortedSetScoreAsync(targetKeys.TimeTickerPending, id.ToString()));
        Assert.Null(await fixture.Db.SortedSetScoreAsync(targetKeys.TimeTickerRetentionSkipped, id.ToString()));
    }

    [Fact]
    public async Task Unified_context_update_retries_CAS_and_atomically_refreshes_indexes()
    {
        var id = Guid.NewGuid();
        var provider = Provider("redis-unified-cas");
        var keys = new RedisKeyBuilder(new TickerQRuntimePartition("redis-unified-cas"));
        await provider.AddTimeTickers([Ticker(id, "unified-cas")]);
        var documentsLoaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resumeWriter = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        provider.AfterUnifiedContextDocumentsLoadedAsync = async () =>
        {
            documentsLoaded.TrySetResult();
            await resumeWriter.Task;
        };
        var skipped = new InternalFunctionContext
        {
            TickerId = id,
            Type = TickerType.TimeTicker,
            ExceptionDetails = "unified skip"
        }.SetProperty(x => x.Status, TickerStatus.Skipped)
         .SetProperty(x => x.ExecutedAt, Now);

        var delayedMutation = provider.UpdateTimeTickersWithUnifiedContext([id], skipped, CancellationToken.None);
        await documentsLoaded.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(1, await provider.UpdateTimeTicker(
            new InternalFunctionContext { TickerId = id, Type = TickerType.TimeTicker }
                .SetProperty(x => x.RetryCount, 3), CancellationToken.None));
        resumeWriter.TrySetResult();
        await delayedMutation;

        var persisted = Assert.Single(await provider.GetTimeTickers(
            x => x.Id == id, CancellationToken.None));
        Assert.Equal(3, persisted.RetryCount);
        Assert.Equal(TickerStatus.Skipped, persisted.Status);
        Assert.True(await fixture.Db.SetContainsAsync(keys.TimeTickerIds, id.ToString()));
        Assert.Null(await fixture.Db.SortedSetScoreAsync(keys.TimeTickerPending, id.ToString()));
        Assert.NotNull(await fixture.Db.SortedSetScoreAsync(keys.TimeTickerRetentionSkipped, id.ToString()));
        Assert.Null(await fixture.Db.SortedSetScoreAsync(keys.TimeTickerRetentionSucceeded, id.ToString()));
        Assert.Null(await fixture.Db.SortedSetScoreAsync(keys.TimeTickerRetentionFailed, id.ToString()));
        Assert.Null(await fixture.Db.SortedSetScoreAsync(keys.TimeTickerRetentionCancelled, id.ToString()));
    }

    [Fact]
    public async Task Identical_ids_are_isolated_across_crud_acquisition_results_and_removal()
    {
        var a = Provider("redis-a");
        var b = Provider("redis-b");
        var id = Guid.NewGuid();
        await a.AddTimeTickers([Ticker(id, "A")]);
        await b.AddTimeTickers([Ticker(id, "B")]);

        Assert.Equal("A", (await a.GetTimeTickerById(id))!.Function);
        Assert.Equal("B", (await b.GetTimeTickerById(id))!.Function);
        var acquiredA = Assert.Single(await a.AcquireImmediateTimeTickersAsync([id]));
        var acquiredB = Assert.Single(await b.AcquireImmediateTimeTickersAsync([id]));
        var terminal = new InternalFunctionContext
        {
            TickerId = id, Type = TickerType.TimeTicker, FunctionName = "A",
            RuntimePartitionKey = new TickerQRuntimePartition("redis-a").StorageKey,
            AcquisitionToken = acquiredA.AcquisitionToken,
            ChainRootId = id, ChainGeneration = acquiredA.ChainGeneration
        }.SetProperty(x => x.Status, TickerStatus.Done)
         .SetProperty(x => x.ReleaseLock, true)
         .SetProperty(x => x.ResultEnvelope,
             new TickerResultEnvelope([7], 1, "application/octet-stream"));
        Assert.True(await a.CommitSuccessfulTickerAsync(terminal));
        Assert.Equal(7, (await a.GetTimeTickerResultAsync(id))!.ToPayloadArray()[0]);
        Assert.Null(await b.GetTimeTickerResultAsync(id));
        Assert.Equal(acquiredB.AcquisitionToken, (await b.GetTimeTickerById(id))!.AcquisitionToken);

        Assert.Equal(1, await a.RemoveTimeTickers([id]));
        Assert.Null(await a.GetTimeTickerById(id));
        Assert.NotNull(await b.GetTimeTickerById(id));
    }

    private TickerRedisPersistenceProvider<TimeTickerEntity, CronTickerEntity> Provider(string ns)
    {
        var options = new SchedulerOptionsBuilder { NodeIdentifier = ns };
        options.BindRuntimeActivationScope(ns, 1, false);
        var clock = Substitute.For<ITickerClock>();
        clock.UtcNow.Returns(Now);
        return new TickerRedisPersistenceProvider<TimeTickerEntity, CronTickerEntity>(
            fixture.Db, clock, options,
            new TickerQRedisOptionBuilder { JsonSerializerContext = TestJsonSerializerContext.Default },
            NullLogger<TickerRedisPersistenceProvider<TimeTickerEntity, CronTickerEntity>>.Instance);
    }

    private static TimeTickerEntity Ticker(Guid id, string function) => new()
    {
        Id = id, Function = function, Request = [], Status = TickerStatus.Idle,
        ExecutionTime = Now, CreatedAt = Now, UpdatedAt = Now
    };
}
