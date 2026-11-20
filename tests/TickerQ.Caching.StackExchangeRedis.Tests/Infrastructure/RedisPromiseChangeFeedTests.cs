using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Enums;

namespace TickerQ.Caching.StackExchangeRedis.Tests.Infrastructure;

/// <summary>
/// The change feed promise checks read instead of scanning every aggregate: roots by the time they were last
/// written, kept by the derived-index script and the scripts that keep their own indexes.
/// </summary>
public sealed partial class RedisRealScriptTests
{
    [Fact]
    public async Task ChangeFeed_ReturnsAggregatesWrittenSince_WithTheirSteps_AndFollowsCancelAndDelete()
    {
        var root = NewIdleTicker();
        root.Function = "promised-root";
        root.Request = [1, 2, 3];
        var step = NewIdleTicker();
        step.ExecutionTime = null;
        step.Function = "promised-step";
        step.Request = [4, 5, 6];
        root.Children.Add(step);
        await _provider.AddTimeTickers([root]);

        var rootJobs = await _provider.ReadTimeTickersChangedSinceAsync("promised-root", BaseNow.AddMinutes(-1), 10);
        Assert.Equal(root.Id, Assert.Single(rootJobs).Id);
        Assert.NotNull(Assert.Single(rootJobs).Request);
        var stepJobs = await _provider.ReadTimeTickersChangedSinceAsync("promised-step", BaseNow.AddMinutes(-1), 10);
        Assert.Equal((step.Id, root.Id), (Assert.Single(stepJobs).Id, Assert.Single(stepJobs).ChainRootId));
        Assert.Empty(await _provider.ReadTimeTickersChangedSinceAsync("promised-root", BaseNow.AddMinutes(1), 10));
        Assert.Empty(await _provider.ReadTimeTickersChangedSinceAsync("other", BaseNow.AddMinutes(-1), 10));

        // Cancelled a minute later through a script that keeps its own indexes: the root moves up the feed.
        _now = BaseNow.AddMinutes(1);
        Assert.Equal(2, await _provider.CancelPendingTimeTickerAsync(root.Id));
        var cancelled = await _provider.ReadTimeTickersChangedSinceAsync("promised-root", BaseNow.AddSeconds(30), 10);
        Assert.Equal(TickerStatus.Cancelled, Assert.Single(cancelled).Status);

        // Whole aggregates by root, for jobs the index already knows.
        var aggregate = await _provider.ReadTimeTickerAggregatesAsync([root.Id]);
        Assert.Equal([root.Id, step.Id], aggregate.Select(x => x.Id).OrderBy(id => id == step.Id));

        // Over the limit, the feed says so instead of returning a part.
        Assert.Null(await _provider.ReadTimeTickersChangedSinceAsync("promised-root", BaseNow.AddMinutes(-1), 0));

        Assert.Equal(1, await _provider.RemoveTimeTickers([root.Id]));
        Assert.Empty(await _provider.ReadTimeTickersChangedSinceAsync("promised-root", BaseNow.AddMinutes(-1), 10));
        Assert.Empty(await _provider.ReadTimeTickerAggregatesAsync([root.Id]));
    }
}
