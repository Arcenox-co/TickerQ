using System.Text;
using System.Text.Json;
using System.Net;
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
public sealed class RedisNodeFinalizationOutboxTests
{
    private static readonly DateTime Now = new(2025, 6, 15, 12, 0, 0, DateTimeKind.Utc);
    private readonly IDatabase _db;
    private readonly TickerRedisPersistenceProvider<TimeTickerEntity, CronTickerEntity> _provider;

    public RedisNodeFinalizationOutboxTests(RedisRealScriptFixture fixture)
    {
        _db = fixture.Db;
        _db.Execute("FLUSHALL");
        var clock = Substitute.For<ITickerClock>();
        clock.UtcNow.Returns(Now);
        _provider = new TickerRedisPersistenceProvider<TimeTickerEntity, CronTickerEntity>(
            _db, clock, new SchedulerOptionsBuilder { NodeIdentifier = "outbox-node" },
            new TickerQRedisOptionBuilder { JsonSerializerContext = TestJsonSerializerContext.Default },
            NullLogger<TickerRedisPersistenceProvider<TimeTickerEntity, CronTickerEntity>>.Instance);
    }

    [Fact]
    public async Task AcceptedGeneration_CommitsTerminalResultAndIntentAtomically()
    {
        Assert.True(_provider.SupportsDurableNodeFinalizationOutbox);
        var acquired = await AcquireTimeAsync();
        var intent = Intent(TickerType.TimeTicker, acquired.Id, acquired.AcquisitionToken!.Value);
        var context = Terminal(TickerType.TimeTicker, acquired.Id, acquired.AcquisitionToken.Value,
            new TickerResultEnvelope([0, 255, 17], 1, "application/octet-stream"));

        Assert.True(await _provider.CommitTerminalTickerAndEnqueueNodeFinalizationAsync(context, intent));

        Assert.Equal(TickerStatus.Done, (await _provider.GetTimeTickerById(acquired.Id))!.Status);
        Assert.Equal(new byte[] { 0, 255, 17 },
            (await _provider.GetTimeTickerResultAsync(acquired.Id))!.ToPayloadArray());
        Assert.True(await _db.HashExistsAsync(RedisKeyBuilder.NodeFinalizationRecordsKey, intent.OutboxId.ToString("D")));
        Assert.NotNull(await _db.SortedSetScoreAsync(RedisKeyBuilder.NodeFinalizationDueKey, intent.OutboxId.ToString("D")));
    }

    [Fact]
    public async Task StaleGeneration_ChangesNothing()
    {
        var acquired = await AcquireTimeAsync();
        var staleToken = Guid.NewGuid();
        var intent = Intent(TickerType.TimeTicker, acquired.Id, staleToken);

        Assert.False(await _provider.CommitTerminalTickerAndEnqueueNodeFinalizationAsync(
            Terminal(TickerType.TimeTicker, acquired.Id, staleToken, new TickerResultEnvelope([3], 1, "application/octet-stream")), intent));

        Assert.Equal(TickerStatus.InProgress, (await _provider.GetTimeTickerById(acquired.Id))!.Status);
        Assert.Null(await _provider.GetTimeTickerResultAsync(acquired.Id));
        Assert.Equal(0, await _db.HashLengthAsync(RedisKeyBuilder.NodeFinalizationRecordsKey));
        Assert.Equal(0, await _db.SortedSetLengthAsync(RedisKeyBuilder.NodeFinalizationDueKey));
    }

    [Fact]
    public async Task ExactDuplicateIsAcknowledgedButMismatchedOutboxIdFailsClosed()
    {
        var acquired = await AcquireTimeAsync();
        var intent = Intent(TickerType.TimeTicker, acquired.Id, acquired.AcquisitionToken!.Value);
        var context = Terminal(TickerType.TimeTicker, acquired.Id, acquired.AcquisitionToken.Value, null);
        Assert.True(await _provider.CommitTerminalTickerAndEnqueueNodeFinalizationAsync(context, intent));
        Assert.True(await _provider.CommitTerminalTickerAndEnqueueNodeFinalizationAsync(context, intent));
        Assert.Equal(1, await _db.HashLengthAsync(RedisKeyBuilder.NodeFinalizationRecordsKey));

        var differentBody = Intent(TickerType.TimeTicker, acquired.Id, acquired.AcquisitionToken.Value,
            intent.OutboxId, Guid.NewGuid());
        await Assert.ThrowsAsync<RedisServerException>(() =>
            _provider.CommitTerminalTickerAndEnqueueNodeFinalizationAsync(context, differentBody));
        Assert.Equal(1, await _db.HashLengthAsync(RedisKeyBuilder.NodeFinalizationRecordsKey));
    }

    [Fact]
    public async Task WrongTypeScriptFailureLeavesTerminalResultAndOutboxAbsent()
    {
        var acquired = await AcquireTimeAsync();
        var intent = Intent(TickerType.TimeTicker, acquired.Id, acquired.AcquisitionToken!.Value);
        await _db.StringSetAsync(RedisKeyBuilder.NodeFinalizationRecordsKey, "wrong-type");

        await Assert.ThrowsAsync<RedisServerException>(() =>
            _provider.CommitTerminalTickerAndEnqueueNodeFinalizationAsync(
                Terminal(TickerType.TimeTicker, acquired.Id, acquired.AcquisitionToken.Value,
                    new TickerResultEnvelope([4], 1, "application/octet-stream")), intent));

        Assert.Equal(TickerStatus.InProgress, (await _provider.GetTimeTickerById(acquired.Id))!.Status);
        Assert.Null(await _provider.GetTimeTickerResultAsync(acquired.Id));
        Assert.Equal("wrong-type", (string?)await _db.StringGetAsync(RedisKeyBuilder.NodeFinalizationRecordsKey));
        Assert.Equal(0, await _db.SortedSetLengthAsync(RedisKeyBuilder.NodeFinalizationDueKey));
    }

    [Fact]
    public async Task ConcurrentClaimsHaveOneWinner_LeaseExpires_AndStaleClaimIsFenced()
    {
        var acquired = await AcquireTimeAsync();
        var intent = Intent(TickerType.TimeTicker, acquired.Id, acquired.AcquisitionToken!.Value);
        Assert.True(await _provider.CommitTerminalTickerAndEnqueueNodeFinalizationAsync(
            Terminal(TickerType.TimeTicker, acquired.Id, acquired.AcquisitionToken.Value, null), intent));

        var calls = await Task.WhenAll(
            _provider.ClaimDueNodeFinalizationsAsync("worker-a", 1, Now, Now.AddMinutes(1)),
            _provider.ClaimDueNodeFinalizationsAsync("worker-b", 1, Now, Now.AddMinutes(1)));
        var first = Assert.Single(calls.SelectMany(x => x));
        Assert.Equal(1, first.AttemptCount);

        var second = Assert.Single(await _provider.ClaimDueNodeFinalizationsAsync(
            "worker-c", 1, Now.AddMinutes(2), Now.AddMinutes(3)));
        Assert.Equal(2, second.AttemptCount);
        Assert.False(await _provider.CompleteNodeFinalizationAsync(first));
        Assert.False(await _provider.RescheduleNodeFinalizationAsync(first, Now.AddMinutes(4), "stale"));
        Assert.True(await _provider.RescheduleNodeFinalizationAsync(second, Now.AddMinutes(4), "retry"));
        Assert.Empty(await _provider.ClaimDueNodeFinalizationsAsync("early", 1, Now.AddMinutes(3), Now.AddMinutes(5)));
        var third = Assert.Single(await _provider.ClaimDueNodeFinalizationsAsync(
            "worker-d", 1, Now.AddMinutes(5), Now.AddMinutes(6)));
        Assert.True(await _provider.CompleteNodeFinalizationAsync(third));
        Assert.Equal(0, await _db.HashLengthAsync(RedisKeyBuilder.NodeFinalizationRecordsKey));
        Assert.Equal(0, await _db.SortedSetLengthAsync(RedisKeyBuilder.NodeFinalizationDueKey));
    }

    [Fact]
    public async Task ClaimCleansOnlyBoundedMissingAndCorruptDueEntries()
    {
        for (var i = 0; i < 3; i++)
        {
            var id = Guid.NewGuid().ToString("D");
            await _db.SortedSetAddAsync(RedisKeyBuilder.NodeFinalizationDueKey, id, RedisKeyBuilder.ToScore(Now));
            if (i == 1)
                await _db.HashSetAsync(RedisKeyBuilder.NodeFinalizationRecordsKey, id, "not-json");
        }

        Assert.Empty(await _provider.ClaimDueNodeFinalizationsAsync("worker", 2, Now, Now.AddMinutes(1)));
        Assert.Equal(1, await _db.SortedSetLengthAsync(RedisKeyBuilder.NodeFinalizationDueKey));
    }

    [Fact]
    public async Task CronOccurrenceAndEmbeddedChildUseExactGenerationFence()
    {
        var occurrence = new CronTickerOccurrenceEntity<CronTickerEntity>
        {
            Id = Guid.NewGuid(), CronTickerId = Guid.NewGuid(), ExecutionTime = Now.AddMinutes(-1),
            Status = TickerStatus.Idle, CreatedAt = Now.AddHours(-1), UpdatedAt = Now.AddHours(-1)
        };
        await _provider.InsertCronTickerOccurrences([occurrence], CancellationToken.None);
        var acquiredOccurrence = Assert.Single(await _provider.AcquireImmediateCronOccurrencesAsync([occurrence.Id]));
        var cronIntent = Intent(TickerType.CronTickerOccurrence, occurrence.Id, acquiredOccurrence.AcquisitionToken!.Value);
        Assert.True(await _provider.CommitTerminalTickerAndEnqueueNodeFinalizationAsync(
            Terminal(TickerType.CronTickerOccurrence, occurrence.Id, acquiredOccurrence.AcquisitionToken.Value, null), cronIntent));

        var child = NewTicker();
        child.ExecutionTime = null;
        var root = NewTicker();
        root.Children.Add(child);
        await _provider.AddTimeTickers([root]);
        var acquiredRoot = Assert.Single(await _provider.AcquireImmediateTimeTickersAsync([root.Id]));
        var generation = acquiredRoot.ChainGeneration!.Value;
        var childContext = Terminal(TickerType.TimeTicker, child.Id, generation, null);
        childContext.ParentId = root.Id;
        childContext.ChainRootId = root.Id;
        childContext.ChainGeneration = generation;
        var childIntent = Intent(TickerType.TimeTicker, child.Id, generation);
        Assert.True(await _provider.CommitTerminalTickerAndEnqueueNodeFinalizationAsync(childContext, childIntent));
    }

    [Fact]
    public async Task RetentionDoesNotDeleteOutbox_AndExactBodyRoundTripsWithoutSecret()
    {
        var acquired = await AcquireTimeAsync();
        var intent = Intent(TickerType.TimeTicker, acquired.Id, acquired.AcquisitionToken!.Value);
        Assert.True(await _provider.CommitTerminalTickerAndEnqueueNodeFinalizationAsync(
            Terminal(TickerType.TimeTicker, acquired.Id, acquired.AcquisitionToken.Value, null), intent));
        Assert.Equal(1, await _provider.RemoveTimeTickers([acquired.Id]));

        var raw = (string)(await _db.HashGetAsync(RedisKeyBuilder.NodeFinalizationRecordsKey, intent.OutboxId.ToString("D")))!;
        Assert.DoesNotContain("secret", raw, StringComparison.OrdinalIgnoreCase);
        var claim = Assert.Single(await _provider.ClaimDueNodeFinalizationsAsync("worker", 1, Now, Now.AddMinutes(1)));
        Assert.Equal(intent.ExactBody, claim.Intent.ExactBody);
        Assert.Equal(intent.FinalizeUri, claim.Intent.FinalizeUri);
    }

    private async Task<TimeTickerEntity> AcquireTimeAsync()
    {
        var ticker = NewTicker();
        await _provider.AddTimeTickers([ticker]);
        return Assert.Single(await _provider.AcquireImmediateTimeTickersAsync([ticker.Id]));
    }

    private static TimeTickerEntity NewTicker() => new()
    {
        Id = Guid.NewGuid(), Function = "OutboxFn", ExecutionTime = Now.AddMinutes(-1),
        Status = TickerStatus.Idle, CreatedAt = Now.AddHours(-1), UpdatedAt = Now.AddHours(-1), Request = []
    };

    private static InternalFunctionContext Terminal(TickerType type, Guid id, Guid token, TickerResultEnvelope? result) =>
        new InternalFunctionContext { TickerId = id, Type = type, AcquisitionToken = token }
            .SetProperty(x => x.Status, TickerStatus.Done)
            .SetProperty(x => x.ResultEnvelope, result)
            .SetProperty(x => x.ReleaseLock, true);

    private static NodeFinalizationIntent Intent(TickerType type, Guid tickerId, Guid token,
        Guid? dispatchId = null, Guid? controlNonce = null)
    {
        var dispatch = dispatchId ?? Guid.NewGuid();
        var epoch = Guid.NewGuid();
        var control = controlNonce ?? Guid.NewGuid();
        var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        {
            tickerType = (int)type,
            tickerId,
            acquisitionToken = token,
            dispatchId = dispatch,
            nodeEpoch = epoch,
            controlNonce = control
        }));
        return new NodeFinalizationIntent(1, dispatch, type, tickerId, token, dispatch, epoch,
            "https://node.example/finalize", "/finalize", false, Guid.NewGuid(), control, body, Now);
    }
}

public sealed class RedisNodeFinalizationTopologyTests
{
    [Fact]
    public void ClusterTopologyFailsClosedBeforeAnyCrossSlotScriptCanRun()
    {
        var db = Substitute.For<IDatabase>();
        var multiplexer = Substitute.For<IConnectionMultiplexer>();
        var server = Substitute.For<IServer>();
        var endpoint = new DnsEndPoint("cluster.example", 6379);
        db.Multiplexer.Returns(multiplexer);
        multiplexer.GetEndPoints(Arg.Any<bool>()).Returns([endpoint]);
        multiplexer.GetServer(endpoint, Arg.Any<object>()).Returns(server);
        server.IsConnected.Returns(true);
        server.ServerType.Returns(ServerType.Cluster);
        var clock = Substitute.For<ITickerClock>();
        clock.UtcNow.Returns(new DateTime(2025, 6, 15, 12, 0, 0, DateTimeKind.Utc));

        var provider = new TickerRedisPersistenceProvider<TimeTickerEntity, CronTickerEntity>(
            db, clock, new SchedulerOptionsBuilder { NodeIdentifier = "cluster-node" },
            new TickerQRedisOptionBuilder { JsonSerializerContext = TestJsonSerializerContext.Default },
            NullLogger<TickerRedisPersistenceProvider<TimeTickerEntity, CronTickerEntity>>.Instance);

        Assert.False(provider.SupportsDurableNodeFinalizationOutbox);
    }
}
