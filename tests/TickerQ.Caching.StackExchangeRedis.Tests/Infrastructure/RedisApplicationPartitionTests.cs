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
            new TickerQRuntimePartition("redis-adopter"), 22);
        using (var cancelled = new CancellationTokenSource())
        {
            cancelled.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                target.AdoptLegacyRuntimePartitionAsync(adoption, cancelled.Token));
        }

        await target.AdoptLegacyRuntimePartitionAsync(adoption);
        await target.AdoptLegacyRuntimePartitionAsync(adoption);

        Assert.Equal("legacy", (await target.GetTimeTickerById(id))!.Function);
        Assert.Null(await legacy.GetTimeTickerById(id));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Provider("redis-other").AdoptLegacyRuntimePartitionAsync(
                new LegacyRuntimePartitionAdoption(new TickerQRuntimePartition("redis-other"), 22)));
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
