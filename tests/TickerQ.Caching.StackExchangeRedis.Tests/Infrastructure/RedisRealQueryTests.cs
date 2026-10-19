using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using StackExchange.Redis;
using TickerQ.Caching.StackExchangeRedis.Helpers;
using TickerQ.Caching.StackExchangeRedis.Infrastructure;
using TickerQ.Utilities;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Infrastructure;
using static TickerQ.Caching.StackExchangeRedis.DependencyInjection.ServiceExtension;

namespace TickerQ.Caching.StackExchangeRedis.Tests.Infrastructure;

public sealed partial class RedisRealScriptTests
{
    [Fact]
    public async Task Query_ScansLargePayloadsInPages_WithAccurateCountsAndProjection()
    {
        var tickers = Enumerable.Range(0, 420).Select(i => new TimeTickerEntity
        {
            Id = Guid.NewGuid(), Function = i % 3 == 0 ? "selected" : "other",
            ExecutionTime = BaseNow.AddMinutes(i), Request = new byte[16 * 1024],
            CreatedAt = BaseNow, UpdatedAt = BaseNow
        }).ToArray();
        // Seed in bounded writes so the test itself does not require a full-payload MSET.
        foreach (var ticker in tickers)
        {
            await _db.StringSetAsync(RedisKeyBuilder.TimeTickerKey(ticker.Id),
                JsonSerializer.Serialize(ticker, TestJsonSerializerContext.Default.TimeTickerEntity));
            await _db.SetAddAsync(RedisKeyBuilder.TimeTickerIdsKey, ticker.Id.ToString());
        }
        var missing = Guid.NewGuid();
        var corrupt = Guid.NewGuid();
        await _db.SetAddAsync(RedisKeyBuilder.TimeTickerIdsKey,
            [missing.ToString(), corrupt.ToString(), "invalid-id"]);
        await _db.StringSetAsync(RedisKeyBuilder.TimeTickerKey(corrupt), "{broken");

        var query = _provider.TimeTickersQuery().Where(row => row.Function == "selected")
            .OrderByDescending(row => row.ExecutionTime);
        var projected = (ITickerProjectionQueryable<TimeTickerEntity>)query;
        var page = await projected.ToPaginatedAsync(row => row.Id, 20, 7);
        var expected = tickers.Where(row => row.Function == "selected")
            .OrderByDescending(row => row.ExecutionTime).Select(row => row.Id).ToArray();

        Assert.Equal(expected.Length, page.TotalCount);
        Assert.Equal(expected.Skip(19 * 7).Take(7), page.Items);
        var fullPage = await _provider.GetTimeTickersPaginated(row => row.Function == "selected", 2, 3);
        Assert.Equal(expected.Skip(3).Take(3), fullPage.Items.Select(row => row.Id));
        Assert.All(fullPage.Items, row => Assert.Equal(16 * 1024, row.Request.Length));
    }

    [Fact]
    public async Task Query_IdCandidateAndParentFilter_StayWithinTheirPartitionAndPredicate()
    {
        var first = NewCron("first");
        var second = NewCron("second");
        await _provider.InsertCronTickers([first, second], CancellationToken.None);
        var selected = NewOccurrence(first.Id, BaseNow.AddMinutes(1), TickerStatus.Done);
        var excluded = NewOccurrence(second.Id, BaseNow.AddMinutes(2), TickerStatus.Done);
        await SeedLegacyOccurrenceAsync(selected);
        await SeedLegacyOccurrenceAsync(excluded);
        // Legacy/repaired datasets may have incomplete reverse indexes. The bounded
        // authoritative scan must still find every occurrence and apply the predicate.
        await _db.SetRemoveAsync(RedisKeyBuilder.CronOccurrencesByCronKey(first.Id), selected.Id.ToString());
        await _db.SetAddAsync(RedisKeyBuilder.CronOccurrencesByCronKey(first.Id), excluded.Id.ToString());

        var page = await _provider.CronTickerOccurrencesQuery().Where(row => row.CronTickerId == first.Id)
            .OrderBy(row => row.ExecutionTime).ToPaginatedAsync(1, 10);
        Assert.Equal(1, page.TotalCount);
        Assert.Equal(selected.Id, Assert.Single(page.Items).Id);
        var legacyPage = await _provider.GetAllCronTickerOccurrencesPaginated(
            row => row.CronTickerId == first.Id, 1, 10);
        Assert.Equal(1, legacyPage.TotalCount);
        Assert.Equal(selected.Id, Assert.Single(legacyPage.Items).Id);
        Assert.Null(await _provider.CronTickersQuery()
            .Where(row => row.Id == first.Id && row.Function == "wrong").FirstOrDefaultAsync());

        var options = new SchedulerOptionsBuilder { NodeIdentifier = "query-other-partition" };
        options.BindRuntimeActivationScope("query-other-partition", 1, false);
        var other = new TickerRedisPersistenceProvider<TimeTickerEntity, CronTickerEntity>(
            _db, _clock, options, new TickerQRedisOptionBuilder { JsonSerializerContext = TestJsonSerializerContext.Default },
            NullLogger<TickerRedisPersistenceProvider<TimeTickerEntity, CronTickerEntity>>.Instance);
        Assert.Empty(await other.CronTickersQuery().ToArrayAsync());
        Assert.Null(await other.CronTickersQuery().Where(row => row.Id == first.Id).FirstOrDefaultAsync());
        Assert.Equal(0, await other.CronTickerOccurrencesQuery().Where(row => row.CronTickerId == first.Id).CountAsync());
    }
    [Fact]
    public async Task AcquisitionBatch_LimitsRoots_PreservesChildren_AndLeavesOthersEligible()
    {
        _options.AcquisitionBatchSize = 2;
        var roots = Enumerable.Range(0, 5).Select(i =>
        {
            var root = NewIdleTicker();
            root.ExecutionTime = BaseNow.AddMilliseconds(100 + i * 10);
            var child = NewIdleTicker();
            child.ExecutionTime = null;
            child.Children.Add(NewIdleTicker());
            root.Children.Add(child);
            return root;
        }).ToArray();
        await _provider.AddTimeTickers(roots);

        var earliest = await _provider.GetEarliestTimeTickers();
        Assert.Equal(2, earliest.Length);
        Assert.Equal(roots.Take(2).Select(row => row.Id), earliest.Select(row => row.Id));
        Assert.All(earliest, row => Assert.Single(Assert.Single(row.Children).Children));

        var acquired = await _provider.AcquireImmediateTimeTickersAsync(roots.Select(row => row.Id).ToArray());
        Assert.Equal(2, acquired.Length);
        Assert.All(acquired, row =>
        {
            Assert.Equal(TickerStatus.InProgress, row.Status);
            Assert.Single(Assert.Single(row.Children).Children);
        });
        foreach (var root in roots.Skip(2))
        {
            Assert.Equal(TickerStatus.Idle, (await _provider.GetTimeTickerById(root.Id)).Status);
            Assert.NotNull(await _db.SortedSetScoreAsync(RedisKeyBuilder.TimeTickerPendingKey, root.Id.ToString()));
        }

        var next = await _provider.GetEarliestTimeTickers();
        Assert.Equal(roots.Skip(2).Take(2).Select(row => row.Id), next.Select(row => row.Id));
        var second = await _provider.AcquireImmediateTimeTickersAsync(roots.Select(row => row.Id).ToArray());
        Assert.Equal(roots.Skip(2).Take(2).Select(row => row.Id), second.Select(row => row.Id));
        var last = Assert.Single(await _provider.GetEarliestTimeTickers());
        Assert.Equal(roots[4].Id, last.Id);
    }

    [Fact]
    public async Task EarliestBatch_SkipsMissingAndUnacquirableIndexEntries_WithoutHidingSiblings()
    {
        _options.AcquisitionBatchSize = 2;
        var stale = NewIdleTicker();
        stale.ExecutionTime = BaseNow.AddMilliseconds(20);
        stale.Status = TickerStatus.Done;
        var first = NewIdleTicker();
        first.ExecutionTime = BaseNow.AddMilliseconds(100);
        var second = NewIdleTicker();
        second.ExecutionTime = BaseNow.AddMilliseconds(200);
        var third = NewIdleTicker();
        third.ExecutionTime = BaseNow.AddMilliseconds(300);
        await _provider.AddTimeTickers([stale, first, second, third]);
        var missing = Guid.NewGuid();
        await _db.SortedSetAddAsync(RedisKeyBuilder.TimeTickerPendingKey, missing.ToString(),
            RedisKeyBuilder.ToScore(BaseNow));
        await _db.SortedSetAddAsync(RedisKeyBuilder.TimeTickerPendingKey, stale.Id.ToString(),
            RedisKeyBuilder.ToScore(stale.ExecutionTime.Value));

        var earliest = await _provider.GetEarliestTimeTickers();
        Assert.Equal(new[] { first.Id, second.Id }, earliest.Select(row => row.Id));
        var acquired = await _provider.AcquireImmediateTimeTickersAsync([missing, stale.Id, first.Id, second.Id, third.Id]);
        Assert.Equal(new[] { first.Id, second.Id }, acquired.Select(row => row.Id));
        Assert.Equal(third.Id, Assert.Single(await _provider.GetEarliestTimeTickers()).Id);
    }

    [Fact]
    public async Task DashboardMetadata_FindsEmbeddedChildren_WithDetailsAndNestedChildCount()
    {
        var root = NewIdleTicker();
        var child = NewIdleTicker([2, 7]);
        child.ExecutionTime = null;
        child.ParentId = root.Id;
        child.Description = "embedded step detail";
        child.Retries = 3;
        child.TimeoutSeconds = 37;
        var grandchild = NewIdleTicker();
        grandchild.ExecutionTime = null;
        grandchild.ParentId = child.Id;
        child.Children.Add(grandchild);
        root.Children.Add(child);
        await _provider.AddTimeTickers([root]);
        var dashboard = new TickerDashboardDataService<TimeTickerEntity, CronTickerEntity>(_provider);

        var detail = await dashboard.GetTimeTickerByIdAsync(child.Id);
        Assert.NotNull(detail);
        Assert.Equal(child.Id, detail.Id);
        Assert.Equal(root.Id, detail.ParentId);
        Assert.Equal("embedded step detail", detail.Description);
        Assert.Equal(3, detail.Retries);
        Assert.Equal(new[] { 2, 7 }, detail.RetryIntervalsSeconds);
        Assert.Equal(37, detail.TimeoutSeconds);
        Assert.Equal(1, detail.ChildCount);

        var children = await dashboard.GetTimeTickerChildrenAsync(root.Id);
        Assert.Equal(child.Id, Assert.Single(children).Id);
        Assert.Equal(1, children[0].ChildCount);
        var chain = await dashboard.GetTimeTickerChainAsync(root.Id);
        Assert.Equal(3, chain.Count);
        Assert.Equal(child.Id, Assert.Single(chain, row => row.Id == grandchild.Id).ParentId);
    }

}
