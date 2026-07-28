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

[Collection("RedisRealScript")]
public sealed class RedisParentResultPublicationTests
{
    private static readonly DateTime Now = new(2025, 6, 15, 12, 0, 0, DateTimeKind.Utc);
    private readonly IDatabase _db;
    private readonly TickerRedisPersistenceProvider<TimeTickerEntity, CronTickerEntity> _provider;

    public RedisParentResultPublicationTests(RedisRealScriptFixture fixture)
    {
        _db = fixture.Db;
        _db.Execute("FLUSHALL");
        var clock = Substitute.For<ITickerClock>();
        clock.UtcNow.Returns(Now);
        var options = new SchedulerOptionsBuilder { NodeIdentifier = "result-node" };
        _provider = new TickerRedisPersistenceProvider<TimeTickerEntity, CronTickerEntity>(
            _db, clock, options,
            new TickerQRedisOptionBuilder { JsonSerializerContext = TestJsonSerializerContext.Default },
            NullLogger<TickerRedisPersistenceProvider<TimeTickerEntity, CronTickerEntity>>.Instance);
    }

    [Fact]
    public async Task RootSuccess_RoundTripsBinaryPayloadAndAllMetadata()
    {
        Assert.True(_provider.SupportsResultPublication);
        var ticker = await AcquireAsync(NewTicker());
        var envelope = new TickerResultEnvelope([0, 255, 1, 2, 128], 1,
            "application/octet-stream", "sha256:result", "Example.BinaryResult");

        Assert.True(await _provider.CommitSuccessfulTickerAsync(Success(ticker, envelope)));

        var stored = await _provider.GetTimeTickerResultAsync(ticker.Id);
        Assert.NotNull(stored);
        Assert.Equal(envelope.ToPayloadArray(), stored!.ToPayloadArray());
        Assert.Equal(1, stored.Version);
        Assert.Equal("application/octet-stream", stored.MediaType);
        Assert.Equal("sha256:result", stored.ContractId);
        Assert.Equal("Example.BinaryResult", stored.ContractType);
    }

    [Fact]
    public async Task ExplicitJsonNull_IsPresentWhileAbsentKeyReturnsNull()
    {
        var ticker = await AcquireAsync(NewTicker());
        var envelope = new TickerResultEnvelope("null"u8.ToArray(), 1, "application/json");
        Assert.True(await _provider.CommitSuccessfulTickerAsync(Success(ticker, envelope)));

        Assert.Equal("null"u8.ToArray(), (await _provider.GetTimeTickerResultAsync(ticker.Id))!.ToPayloadArray());
        Assert.Null(await _provider.GetTimeTickerResultAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task StaleTokenCannotPublishOrAcknowledgeTerminalState()
    {
        var ticker = await AcquireAsync(NewTicker());
        var stale = Success(ticker, new TickerResultEnvelope([1], 1, "application/octet-stream"));
        stale.AcquisitionToken = Guid.NewGuid();

        Assert.False(await _provider.CommitSuccessfulTickerAsync(stale));
        Assert.Null(await _provider.GetTimeTickerResultAsync(ticker.Id));
        Assert.Equal(TickerStatus.InProgress, (await _provider.GetTimeTickerById(ticker.Id))!.Status);
    }

    [Fact]
    public async Task AtomicCommitRequiresSuccessfulTerminalAndExplicitOptionalResultMutation()
    {
        var ticker = await AcquireAsync(NewTicker());
        var missingResultMutation = new InternalFunctionContext
        {
            TickerId = ticker.Id, Type = TickerType.TimeTicker,
            AcquisitionToken = ticker.AcquisitionToken
        }.SetProperty(x => x.Status, TickerStatus.Done);
        var failed = new InternalFunctionContext
        {
            TickerId = ticker.Id, Type = TickerType.TimeTicker,
            AcquisitionToken = ticker.AcquisitionToken
        }.SetProperty(x => x.Status, TickerStatus.Failed)
         .SetProperty(x => x.ResultEnvelope, null);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _provider.CommitSuccessfulTickerAsync(missingResultMutation));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _provider.CommitSuccessfulTickerAsync(failed));
        Assert.Equal(TickerStatus.InProgress, (await _provider.GetTimeTickerById(ticker.Id))!.Status);
    }

    [Fact]
    public async Task SuccessfulRerunWithoutResultClearsPriorResult()
    {
        var ticker = await AcquireAsync(NewTicker());
        Assert.True(await _provider.CommitSuccessfulTickerAsync(Success(ticker,
            new TickerResultEnvelope([9], 1, "application/octet-stream"))));
        Assert.NotNull(await _provider.GetTimeTickerResultAsync(ticker.Id));

        var rerun = await _provider.AcquireTimeTickerOnDemandAsync(ticker.Id, Now.AddMinutes(1));
        Assert.NotNull(rerun);
        Assert.Null(await _provider.GetTimeTickerResultAsync(ticker.Id));
        Assert.True(await _provider.CommitSuccessfulTickerAsync(Success(rerun!, null)));
        Assert.Null(await _provider.GetTimeTickerResultAsync(ticker.Id));
    }

    [Fact]
    public async Task FailedCancelledSkippedAndRetryWritesNeverPublish()
    {
        foreach (var status in new[] { TickerStatus.Failed, TickerStatus.Cancelled, TickerStatus.Skipped })
        {
            var ticker = await AcquireAsync(NewTicker());
            var update = Success(ticker, new TickerResultEnvelope([1], 1, "application/octet-stream"));
            update.Status = status;
            Assert.Equal(1, await _provider.UpdateTimeTicker(update));
            Assert.Null(await _provider.GetTimeTickerResultAsync(ticker.Id));
        }

        var retryTicker = await AcquireAsync(NewTicker());
        var retry = new InternalFunctionContext()
            .SetProperty(x => x.TickerId, retryTicker.Id)
            .SetProperty(x => x.RetryCount, 1)
            .SetProperty(x => x.ResultEnvelope, new TickerResultEnvelope([2], 1, "application/octet-stream"));
        Assert.Equal(1, await _provider.UpdateTimeTicker(retry));
        Assert.Null(await _provider.GetTimeTickerResultAsync(retryTicker.Id));
    }

    [Fact]
    public async Task CorruptUnknownVersionAndOversizedStoredValuesFailClosed()
    {
        var corrupt = Guid.NewGuid();
        await _db.StringSetAsync(RedisKeyBuilder.TimeTickerResultKey(corrupt), new byte[] { 1, 2, 3 });
        Assert.Null(await _provider.GetTimeTickerResultAsync(corrupt));

        var unknown = Guid.NewGuid();
        var valid = RedisResultEnvelopeCodec.Serialize(
            new TickerResultEnvelope([1], 1, "application/octet-stream"));
        valid[4] = 99;
        await _db.StringSetAsync(RedisKeyBuilder.TimeTickerResultKey(unknown), valid);
        Assert.Null(await _provider.GetTimeTickerResultAsync(unknown));

        var oversized = Guid.NewGuid();
        var bytes = new byte[(1024 * 1024) + 100];
        await _db.StringSetAsync(RedisKeyBuilder.TimeTickerResultKey(oversized), bytes);
        Assert.Null(await _provider.GetTimeTickerResultAsync(oversized));
    }

    [Fact]
    public async Task PayloadOverOneMiBIsRejectedBeforeTerminalMutation()
    {
        var ticker = await AcquireAsync(NewTicker());
        var update = Success(ticker,
            new TickerResultEnvelope(new byte[(1024 * 1024) + 1], 1, "application/octet-stream"));

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => _provider.CommitSuccessfulTickerAsync(update));
        Assert.Equal(TickerStatus.InProgress, (await _provider.GetTimeTickerById(ticker.Id))!.Status);
        Assert.Null(await _provider.GetTimeTickerResultAsync(ticker.Id));
    }

    [Fact]
    public async Task EmbeddedChildPublishesOnlyItsOwnDirectResultKey()
    {
        var grandchild = NewTicker();
        var child = NewTicker();
        child.Children.Add(grandchild);
        var root = NewTicker();
        root.Children.Add(child);
        child.ParentId = root.Id;
        grandchild.ParentId = child.Id;
        var acquiredRoot = await AcquireAsync(root);

        var update = new InternalFunctionContext
        {
            TickerId = child.Id,
            ParentId = root.Id,
            ChainRootId = root.Id,
            AcquisitionToken = acquiredRoot.AcquisitionToken,
            Type = TickerType.TimeTicker
        }.SetProperty(x => x.Status, TickerStatus.Done)
         .SetProperty(x => x.ResultEnvelope,
             new TickerResultEnvelope("child"u8.ToArray(), 1, "application/octet-stream"));

        Assert.True(await _provider.CommitSuccessfulTickerAsync(update));
        Assert.Equal("child"u8.ToArray(), (await _provider.GetTimeTickerResultAsync(child.Id))!.ToPayloadArray());
        Assert.Null(await _provider.GetTimeTickerResultAsync(root.Id));
        Assert.Null(await _provider.GetTimeTickerResultAsync(grandchild.Id));
    }

    [Fact]
    public async Task EmbeddedChildStaleTokenCannotPublishOrMutateAggregate()
    {
        var child = NewTicker();
        var root = NewTicker();
        root.Children.Add(child);
        child.ParentId = root.Id;
        await AcquireAsync(root);
        var update = new InternalFunctionContext
        {
            TickerId = child.Id, ParentId = root.Id, ChainRootId = root.Id,
            AcquisitionToken = Guid.NewGuid(), Type = TickerType.TimeTicker
        }.SetProperty(x => x.Status, TickerStatus.Done)
         .SetProperty(x => x.ResultEnvelope,
             new TickerResultEnvelope([7], 1, "application/octet-stream"));

        Assert.False(await _provider.CommitSuccessfulTickerAsync(update));
        Assert.Null(await _provider.GetTimeTickerResultAsync(child.Id));
        Assert.Equal(TickerStatus.Idle,
            (await _provider.GetTimeTickerById(root.Id))!.Children.Single().Status);
    }

    [Fact]
    public async Task DeleteAndRetentionRemoveResultSideKeys()
    {
        var normal = await AcquireAsync(NewTicker());
        Assert.True(await _provider.CommitSuccessfulTickerAsync(Success(normal,
            new TickerResultEnvelope([1], 1, "application/octet-stream"))));
        Assert.Equal(1, await _provider.RemoveTimeTickers([normal.Id]));
        Assert.Null(await _provider.GetTimeTickerResultAsync(normal.Id));

        var chainChild = NewTicker();
        var chainRoot = NewTicker();
        chainRoot.Children.Add(chainChild);
        chainChild.ParentId = chainRoot.Id;
        await _provider.AddTimeTickers([chainRoot]);
        await _db.StringSetAsync(RedisKeyBuilder.TimeTickerResultKey(chainRoot.Id),
            RedisResultEnvelopeCodec.Serialize(new TickerResultEnvelope([3], 1, "application/octet-stream")));
        await _db.StringSetAsync(RedisKeyBuilder.TimeTickerResultKey(chainChild.Id),
            RedisResultEnvelopeCodec.Serialize(new TickerResultEnvelope([4], 1, "application/octet-stream")));
        Assert.Equal(1, await _provider.RemoveTimeTickers([chainRoot.Id]));
        Assert.Null(await _provider.GetTimeTickerResultAsync(chainRoot.Id));
        Assert.Null(await _provider.GetTimeTickerResultAsync(chainChild.Id));

        var retained = NewTicker();
        retained.Status = TickerStatus.Done;
        retained.ExecutedAt = Now.AddDays(-10);
        await _provider.AddTimeTickers([retained]);
        await _db.StringSetAsync(RedisKeyBuilder.TimeTickerResultKey(retained.Id),
            RedisResultEnvelopeCodec.Serialize(new TickerResultEnvelope([2], 1, "application/octet-stream")));
        Assert.Equal(1, (await _provider.DeleteEligibleTimeTickerChainsAsync(
            new RetentionCutoffs(Now.AddDays(-7), null, null, null), 10, RetentionCursor.Start)).Deleted);
        Assert.Null(await _provider.GetTimeTickerResultAsync(retained.Id));
    }

    [Fact]
    public async Task RecreatingAggregateClearsResultsFromRemovedDescendants()
    {
        var oldChild = NewTicker();
        var oldRoot = NewTicker();
        oldRoot.Children.Add(oldChild);
        await _provider.AddTimeTickers([oldRoot]);
        await StoreAggregateResultsAsync(oldRoot);

        var replacement = NewTicker();
        replacement.Id = oldRoot.Id;
        await _provider.AddTimeTickers([replacement]);

        Assert.Null(await _provider.GetTimeTickerResultAsync(oldRoot.Id));
        Assert.Null(await _provider.GetTimeTickerResultAsync(oldChild.Id));
    }

    [Fact]
    public async Task CronOccurrenceSuccessPublishesAndRecreationClearsResult()
    {
        var occurrence = new CronTickerOccurrenceEntity<CronTickerEntity>
        {
            Id = Guid.NewGuid(), CronTickerId = Guid.NewGuid(),
            ExecutionTime = Now.AddMinutes(-1), Status = TickerStatus.Idle,
            CreatedAt = Now.AddHours(-1), UpdatedAt = Now.AddHours(-1)
        };
        await _provider.InsertCronTickerOccurrences([occurrence], CancellationToken.None);
        var acquired = Assert.Single(await _provider.AcquireImmediateCronOccurrencesAsync([occurrence.Id]));
        var envelope = new TickerResultEnvelope("cron"u8.ToArray(), 1, "application/octet-stream");
        var update = new InternalFunctionContext
        {
            TickerId = acquired.Id,
            Type = TickerType.CronTickerOccurrence,
            AcquisitionToken = acquired.AcquisitionToken
        }.SetProperty(x => x.Status, TickerStatus.Done)
         .SetProperty(x => x.ResultEnvelope, envelope);

        Assert.True(await _provider.CommitSuccessfulTickerAsync(update));
        Assert.Equal("cron"u8.ToArray(),
            (await _provider.GetCronTickerOccurrenceResultAsync(occurrence.Id))!.ToPayloadArray());

        occurrence.Status = TickerStatus.Idle;
        occurrence.LockHolder = null;
        occurrence.LockedAt = null;
        occurrence.LeaseUntil = null;
        occurrence.AcquisitionToken = null;
        await _provider.InsertCronTickerOccurrences([occurrence], CancellationToken.None);
        Assert.Null(await _provider.GetCronTickerOccurrenceResultAsync(occurrence.Id));
    }

    [Fact]
    public async Task CronOccurrenceCommitReturnsFalseWhenAcquisitionFenceRejectsMutation()
    {
        var occurrence = new CronTickerOccurrenceEntity<CronTickerEntity>
        {
            Id = Guid.NewGuid(), CronTickerId = Guid.NewGuid(),
            ExecutionTime = Now.AddMinutes(-1), Status = TickerStatus.Idle,
            CreatedAt = Now.AddHours(-1), UpdatedAt = Now.AddHours(-1)
        };
        await _provider.InsertCronTickerOccurrences([occurrence], CancellationToken.None);
        var acquired = Assert.Single(await _provider.AcquireImmediateCronOccurrencesAsync([occurrence.Id]));
        var stale = new InternalFunctionContext
        {
            TickerId = acquired.Id,
            Type = TickerType.CronTickerOccurrence,
            AcquisitionToken = Guid.NewGuid()
        }.SetProperty(x => x.Status, TickerStatus.Done)
         .SetProperty(x => x.ResultEnvelope,
             new TickerResultEnvelope([1], 1, "application/octet-stream"));

        Assert.False(await _provider.CommitSuccessfulTickerAsync(stale));
        Assert.Null(await _provider.GetCronTickerOccurrenceResultAsync(occurrence.Id));
        Assert.Equal(TickerStatus.InProgress,
            Assert.Single(await _provider.GetAllCronTickerOccurrences(x => x.Id == occurrence.Id)).Status);
    }

    [Fact]
    public async Task AcquireAndOnDemandRecursivelyClearNestedResultsAndRefreshGeneration()
    {
        var grandchild = NewTicker();
        var child = NewTicker();
        child.Children.Add(grandchild);
        var root = NewTicker();
        root.Children.Add(child);
        child.ParentId = root.Id;
        grandchild.ParentId = child.Id;
        await _provider.AddTimeTickers([root]);
        await StoreAggregateResultsAsync(root);

        var acquired = Assert.Single(await _provider.AcquireImmediateTimeTickersAsync([root.Id]));
        await AssertAggregateResultsAbsentAsync(root);
        var acquiredChild = Assert.Single(acquired.Children);
        Assert.Equal(acquired.AcquisitionToken, acquiredChild.AcquisitionToken);
        Assert.Equal(acquired.AcquisitionToken, Assert.Single(acquiredChild.Children).AcquisitionToken);

        Assert.True(await _provider.CommitSuccessfulTickerAsync(Success(acquired, null)));
        await StoreAggregateResultsAsync(root);
        var onDemand = await _provider.AcquireTimeTickerOnDemandAsync(root.Id, Now.AddMinutes(1));
        Assert.NotNull(onDemand);
        await AssertAggregateResultsAbsentAsync(root);
        var onDemandChild = Assert.Single(onDemand!.Children);
        Assert.Equal(onDemand.AcquisitionToken, onDemandChild.AcquisitionToken);
        Assert.Equal(onDemand.AcquisitionToken, Assert.Single(onDemandChild.Children).AcquisitionToken);
    }

    [Fact]
    public async Task ReleaseRecursivelyClearsNestedResultsAndOwnership()
    {
        var grandchild = NewTicker();
        var child = NewTicker();
        child.Children.Add(grandchild);
        var root = NewTicker();
        root.Children.Add(child);
        await _provider.AddTimeTickers([root]);
        await foreach (var _ in _provider.QueueTimeTickers([root])) { }
        await StoreAggregateResultsAsync(root);

        await _provider.ReleaseAcquiredTimeTickers([root.Id]);

        await AssertAggregateResultsAbsentAsync(root);
        var persisted = await _provider.GetTimeTickerById(root.Id);
        Assert.Equal(TickerStatus.Idle, persisted!.Status);
        Assert.All(EnumerateAggregate(persisted), item =>
        {
            Assert.Null(item.LockHolder);
            Assert.Null(item.LockedAt);
            Assert.Null(item.AcquisitionToken);
        });
    }

    [Fact]
    public async Task RecoverStaleRestartAndCancelRecursivelyClearNestedResults()
    {
        var restarted = NewStaleAggregate();
        await _provider.AddTimeTickers([restarted]);
        await StoreAggregateResultsAsync(restarted);

        var restartResult = await _provider.RecoverStaleTickers(3);
        Assert.Equal(1, restartResult.RestartedTimeTickers);
        await AssertAggregateResultsAbsentAsync(restarted);
        Assert.All(EnumerateAggregate((await _provider.GetTimeTickerById(restarted.Id))!),
            item => Assert.Null(item.AcquisitionToken));

        var cancelled = NewStaleAggregate();
        await _provider.AddTimeTickers([cancelled]);
        await StoreAggregateResultsAsync(cancelled);

        var cancelResult = await _provider.RecoverStaleTickers(0);
        Assert.Equal(1, cancelResult.CancelledTimeTickers);
        await AssertAggregateResultsAbsentAsync(cancelled);
        Assert.Equal(TickerStatus.Cancelled, (await _provider.GetTimeTickerById(cancelled.Id))!.Status);
    }

    [Fact]
    public async Task DeadNodeRecoveryRecursivelyClearsNestedResultsAndOwnership()
    {
        var root = NewStaleAggregate();
        foreach (var item in EnumerateAggregate(root))
        {
            item.Status = TickerStatus.Queued;
            item.LockHolder = "dead-node";
            item.LeaseUntil = null;
        }
        await _provider.AddTimeTickers([root]);
        await StoreAggregateResultsAsync(root);

        await _provider.ReleaseDeadNodeTimeTickerResources("dead-node");

        await AssertAggregateResultsAbsentAsync(root);
        var persisted = await _provider.GetTimeTickerById(root.Id);
        Assert.Equal(TickerStatus.Idle, persisted!.Status);
        Assert.All(EnumerateAggregate(persisted), item =>
        {
            Assert.Null(item.LockHolder);
            Assert.Null(item.AcquisitionToken);
        });
    }

    private TimeTickerEntity NewStaleAggregate()
    {
        var grandchild = NewTicker();
        var child = NewTicker();
        child.Children.Add(grandchild);
        var root = NewTicker();
        root.Children.Add(child);
        var token = Guid.NewGuid();
        foreach (var item in EnumerateAggregate(root))
        {
            item.Status = TickerStatus.InProgress;
            item.LockHolder = "stale-node";
            item.LockedAt = Now.AddMinutes(-10);
            item.LeaseUntil = Now.AddMinutes(-1);
            item.AcquisitionToken = token;
            item.OnStale = StaleAction.Restart;
        }
        return root;
    }

    private async Task StoreAggregateResultsAsync(TimeTickerEntity root)
    {
        var encoded = RedisResultEnvelopeCodec.Serialize(
            new TickerResultEnvelope([42], 1, "application/octet-stream"));
        foreach (var item in EnumerateAggregate(root))
            await _db.StringSetAsync(RedisKeyBuilder.TimeTickerResultKey(item.Id), encoded);
    }

    private async Task AssertAggregateResultsAbsentAsync(TimeTickerEntity root)
    {
        foreach (var item in EnumerateAggregate(root))
            Assert.Null(await _provider.GetTimeTickerResultAsync(item.Id));
    }

    private static IEnumerable<TimeTickerEntity> EnumerateAggregate(TimeTickerEntity root)
    {
        var pending = new Stack<TimeTickerEntity>();
        pending.Push(root);
        while (pending.TryPop(out var current))
        {
            yield return current;
            foreach (var child in current.Children ?? [])
                pending.Push(child);
        }
    }

    private TimeTickerEntity NewTicker() => new()
    {
        Id = Guid.NewGuid(), Function = "ResultFn", ExecutionTime = Now.AddMinutes(-1),
        Status = TickerStatus.Idle, CreatedAt = Now.AddHours(-1), UpdatedAt = Now.AddHours(-1), Request = []
    };

    private async Task<TimeTickerEntity> AcquireAsync(TimeTickerEntity ticker)
    {
        await _provider.AddTimeTickers([ticker]);
        return Assert.Single(await _provider.AcquireImmediateTimeTickersAsync([ticker.Id]));
    }

    private static InternalFunctionContext Success(TimeTickerEntity ticker, TickerResultEnvelope? envelope)
    {
        var context = new InternalFunctionContext
        {
            TickerId = ticker.Id, Type = TickerType.TimeTicker,
            AcquisitionToken = ticker.AcquisitionToken
        }.SetProperty(x => x.Status, TickerStatus.Done)
         .SetProperty(x => x.ResultEnvelope, envelope);
        return context;
    }
}
