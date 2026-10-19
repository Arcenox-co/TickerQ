using System;
using System.Threading;
using System.Threading.Tasks;
using NSubstitute;
using TickerQ.Utilities;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Interfaces.Managers;
using TickerQ.Utilities.Managers;
using TickerQ.Utilities.Models;
using Xunit;

namespace TickerQ.Tests;

public class JobRetentionManagerTests
{
    public class FakeTimeTicker : TimeTickerEntity<FakeTimeTicker> { }
    public class FakeCronTicker : CronTickerEntity { }

    private static IInternalTickerManager NewManager(
        ITickerPersistenceProvider<FakeTimeTicker, FakeCronTicker> provider)
        => new InternalTickerManager<FakeTimeTicker, FakeCronTicker>(
            provider,
            Substitute.For<ITickerClock>(),
            Substitute.For<ITickerQNotificationHubSender>(),
            new SchedulerOptionsBuilder());

    [Fact]
    public void SupportsRetention_DelegatesToProvider()
    {
        var provider = Substitute.For<ITickerPersistenceProvider<FakeTimeTicker, FakeCronTicker>>();
        provider.SupportsRetention.Returns(true);

        Assert.True(NewManager(provider).SupportsRetention);
    }

    [Fact]
    public async Task SweepTimeChainsAsync_DelegatesToProvider_WithCursor()
    {
        var provider = Substitute.For<ITickerPersistenceProvider<FakeTimeTicker, FakeCronTicker>>();
        var cutoffs = new RetentionCutoffs(DateTime.UtcNow, null, null, null);
        var cursor = RetentionCursor.After(DateTime.UtcNow.AddDays(-3), Guid.NewGuid());
        var expected = new RetentionChainBatchResult(deleted: 3, hasMore: true, nextCursor: cursor);

        provider.DeleteEligibleTimeTickerChainsAsync(cutoffs, 500, cursor, Arg.Any<CancellationToken>())
            .Returns(expected);

        var result = await NewManager(provider).SweepTimeChainsAsync(cutoffs, 500, cursor, CancellationToken.None);

        Assert.Equal(3, result.Deleted);
        Assert.True(result.HasMore);
        Assert.Equal(cursor, result.NextCursor);
    }

    [Fact]
    public async Task SweepCronOccurrencesAsync_DelegatesToProvider()
    {
        var provider = Substitute.For<ITickerPersistenceProvider<FakeTimeTicker, FakeCronTicker>>();
        var cutoffs = new RetentionCutoffs(DateTime.UtcNow, null, null, null);

        provider.DeleteEligibleCronTickerOccurrencesAsync(cutoffs, 500, Arg.Any<CancellationToken>())
            .Returns(new RetentionBatchResult(deleted: 4, hasMore: true));

        var result = await NewManager(provider).SweepCronOccurrencesAsync(cutoffs, 500, CancellationToken.None);

        Assert.Equal(4, result.Deleted);
        Assert.True(result.HasMore);
    }
}
