using System.Text.Json;
using StackExchange.Redis;
using TickerQ.Caching.StackExchangeRedis.Helpers;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Models;

namespace TickerQ.Caching.StackExchangeRedis.Tests.Infrastructure;

public sealed partial class RedisRealScriptTests
{
    [Fact]
    public async Task ConfigurationEdit_AllowsIdleAndRejectsStaleOwnershipAndDeletedSnapshot_RealLua()
    {
        var ticker = NewIdleTicker();
        await _provider.AddTimeTickers([ticker]);
        ticker.Function = "edited";
        Assert.Equal(1, await _provider.UpdateTimeTickers([ticker]));
        var acquired = Assert.Single(await _provider.AcquireImmediateTimeTickersAsync([ticker.Id]));
        ticker.Function = "stale";
        Assert.Equal(0, await _provider.UpdateTimeTickers([ticker]));
        var persisted = await _provider.GetTimeTickerById(ticker.Id);
        Assert.Equal("edited", persisted.Function);
        Assert.Equal(acquired.AcquisitionToken, persisted.AcquisitionToken);
        await _db.KeyDeleteAsync(RedisKeyBuilder.TimeTickerKey(ticker.Id));
        Assert.Equal(0, await _provider.UpdateTimeTickers([ticker]));
        Assert.Null(await _provider.GetTimeTickerById(ticker.Id));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConfigurationEdit_CasRejectsConcurrentOwnershipOrDeletion_RealLua(bool delete)
    {
        var ticker = NewIdleTicker();
        await _provider.AddTimeTickers([ticker]);
        var originalFunction = ticker.Function;
        _provider.BeforeTimeTickerConfigurationWriteForTestAsync = async (id, ct) =>
        {
            if (delete) await _db.KeyDeleteAsync(RedisKeyBuilder.TimeTickerKey(id));
            else await _provider.AcquireImmediateTimeTickersAsync([id], ct);
        };
        ticker.Function = "must-not-write";
        Assert.Equal(0, await _provider.UpdateTimeTickers([ticker]));
        var persisted = await _provider.GetTimeTickerById(ticker.Id);
        if (delete) Assert.Null(persisted);
        else
        {
            Assert.Equal(originalFunction, persisted.Function);
            Assert.Equal(TickerStatus.InProgress, persisted.Status);
            Assert.NotNull(persisted.AcquisitionToken);
        }
    }

    [Fact]
    public async Task ConfigurationEdit_RejectsStaleNestedChildAndPreservesResults_RealLua()
    {
        var root = NewIdleTicker();
        root.Children = [NewIdleTicker()];
        await _provider.AddTimeTickers([root]);
        var stale = await _provider.GetTimeTickerById(root.Id);
        var current = await _provider.GetTimeTickerById(root.Id);
        var child = Assert.Single(current.Children);
        child.Status = TickerStatus.InProgress;
        child.AcquisitionToken = Guid.NewGuid();
        await _db.StringSetAsync(RedisKeyBuilder.TimeTickerKey(root.Id),
            JsonSerializer.Serialize(current, TestJsonSerializerContext.Default.TimeTickerEntity));
        stale.Function = "must-not-write";
        Assert.Equal(0, await _provider.UpdateTimeTickers([stale]));
        Assert.Equal(root.Function, (await _provider.GetTimeTickerById(root.Id)).Function);
        await _db.StringSetAsync(RedisKeyBuilder.TimeTickerResultKey(child.Id), "committed-result");
        child.Function = "edited-child";
        Assert.Equal(1, await _provider.UpdateTimeTickers([child]));
        var persisted = Assert.Single((await _provider.GetTimeTickerById(root.Id)).Children);
        Assert.Equal("edited-child", persisted.Function);
        Assert.Equal(child.AcquisitionToken, persisted.AcquisitionToken);
        Assert.Equal("committed-result", (string?)await _db.StringGetAsync(RedisKeyBuilder.TimeTickerResultKey(child.Id)));
    }

    [Fact]
    public async Task Retention_DeletesAllNestedResultsAndEvidence_RealLua()
    {
        var root = CompleteRetentionChain();
        await _provider.AddTimeTickers([root]);
        var nodes = new[] { root, root.Children.Single(), root.Children.Single().Children.Single() };
        foreach (var node in nodes)
        {
            await _db.StringSetAsync(RedisKeyBuilder.TimeTickerResultKey(node.Id), "result");
            await _db.HashSetAsync(RedisKeyBuilder.TerminalMutationEvidenceKey, $"1:{node.Id:D}", "evidence");
        }
        var batch = await _provider.DeleteEligibleTimeTickerChainsAsync(
            new RetentionCutoffs(BaseNow.AddDays(-7), BaseNow.AddDays(-7), null, null), 10, RetentionCursor.Start);
        Assert.Equal(3, batch.Deleted);
        foreach (var node in nodes)
        {
            Assert.False(await _db.KeyExistsAsync(RedisKeyBuilder.TimeTickerResultKey(node.Id)));
            Assert.False(await _db.HashExistsAsync(RedisKeyBuilder.TerminalMutationEvidenceKey, $"1:{node.Id:D}"));
        }
        Assert.Null(await _provider.GetTimeTickerById(root.Id));
    }

    [Theory]
    [InlineData(0)] // disabled status window
    [InlineData(1)] // child is too recent
    [InlineData(2)] // child has an acquisition
    [InlineData(3)] // child has a live lease
    [InlineData(4)] // aggregate exceeds the configured node bound
    public async Task Retention_PreservesWholeUnsafeAggregate_RealLua(int reason)
    {
        var root = CompleteRetentionChain();
        var leaf = root.Children.Single().Children.Single();
        if (reason == 1) leaf.ExecutedAt = BaseNow;
        if (reason == 2) leaf.AcquisitionToken = Guid.NewGuid();
        if (reason == 3) leaf.LeaseUntil = BaseNow.AddMinutes(1);
        await _provider.AddTimeTickers([root]);
        await _db.StringSetAsync(RedisKeyBuilder.TimeTickerResultKey(leaf.Id), "result");
        var before = await _db.StringGetAsync(RedisKeyBuilder.TimeTickerKey(root.Id));
        var batch = await _provider.DeleteEligibleTimeTickerChainsAsync(
            new RetentionCutoffs(BaseNow.AddDays(-7), reason == 0 ? null : BaseNow.AddDays(-7), null, null,
                maxNodesPerChain: reason == 4 ? 2 : 1000), 10, RetentionCursor.Start);
        Assert.Equal(0, batch.Deleted);
        Assert.Equal(before, await _db.StringGetAsync(RedisKeyBuilder.TimeTickerKey(root.Id)));
        Assert.Equal("result", (string?)await _db.StringGetAsync(RedisKeyBuilder.TimeTickerResultKey(leaf.Id)));
    }

    [Fact]
    public async Task Retention_CasPreservesConcurrentReactivationAndResults_RealLua()
    {
        var root = CompleteRetentionChain();
        await _provider.AddTimeTickers([root]);
        await _db.StringSetAsync(RedisKeyBuilder.TimeTickerResultKey(root.Id), "result");
        _provider.BeforeTimeTickerRetentionDeleteForTestAsync = async (id, _) =>
        {
            var changed = await _provider.GetTimeTickerById(id);
            changed.Status = TickerStatus.InProgress;
            changed.AcquisitionToken = Guid.NewGuid();
            await _db.StringSetAsync(RedisKeyBuilder.TimeTickerKey(id),
                JsonSerializer.Serialize(changed, TestJsonSerializerContext.Default.TimeTickerEntity));
        };
        var batch = await _provider.DeleteEligibleTimeTickerChainsAsync(
            new RetentionCutoffs(BaseNow.AddDays(-7), BaseNow.AddDays(-7), null, null), 10, RetentionCursor.Start);
        Assert.Equal(0, batch.Deleted);
        Assert.Equal(TickerStatus.InProgress, (await _provider.GetTimeTickerById(root.Id)).Status);
        Assert.Equal("result", (string?)await _db.StringGetAsync(RedisKeyBuilder.TimeTickerResultKey(root.Id)));
    }

    [Fact]
    public async Task Retention_CursorSkipsBlockedEqualTimestampChains_RealLua()
    {
        var roots = Enumerable.Range(1, 4).Select(index =>
        {
            var root = CompleteRetentionChain();
            root.Id = Guid.Parse($"00000000-0000-0000-0000-{index:000000000000}");
            if (index < 4) root.Children.Single().Children.Single().ExecutedAt = BaseNow;
            return root;
        }).ToArray();
        await _provider.AddTimeTickers(roots);
        var cursor = RetentionCursor.Start;
        var deleted = 0;
        for (var call = 0; call < 4; call++)
        {
            var batch = await _provider.DeleteEligibleTimeTickerChainsAsync(
                new RetentionCutoffs(BaseNow.AddDays(-7), BaseNow.AddDays(-7), null, null), 1, cursor);
            deleted += batch.Deleted;
            cursor = batch.NextCursor;
        }
        Assert.Equal(3, deleted);
        Assert.Equal(RetentionCursor.Start, cursor);
        Assert.Null(await _provider.GetTimeTickerById(roots[3].Id));
        Assert.NotNull(await _provider.GetTimeTickerById(roots[0].Id));
    }

    [Fact]
    public async Task OverallStatistics_CountsDashboardRootsAndOccurrences_RealLua()
    {
        var root = CompleteRetentionChain();
        root.Request = new byte[1024 * 1024];
        await _provider.AddTimeTickers([root]);
        var cron = NewCron("statistics");
        await _provider.InsertCronTickers([cron], CancellationToken.None);
        var occurrence = NewOccurrence(cron.Id, BaseNow, TickerStatus.Failed);
        await _provider.InsertCronTickerOccurrences([occurrence], CancellationToken.None);
        var counts = await _provider.GetOverallJobStatusCountsAsync();
        Assert.Equal(1, counts[TickerStatus.Done]);
        Assert.Equal(1, counts[TickerStatus.Failed]);
        Assert.Equal(2, counts.Values.Sum());
    }

    private TimeTickerEntity CompleteRetentionChain()
    {
        var root = NewIdleTicker();
        var child = NewIdleTicker();
        var leaf = NewIdleTicker();
        root.Children = [child];
        child.Children = [leaf];
        foreach (var node in new[] { root, child, leaf })
        {
            node.Status = TickerStatus.Done;
            node.ExecutedAt = BaseNow.AddDays(-10);
        }
        leaf.Status = TickerStatus.Failed;
        return root;
    }
}
