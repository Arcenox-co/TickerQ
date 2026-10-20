using NSubstitute;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Infrastructure;
using TickerQ.Utilities.Interfaces;

namespace TickerQ.Tests;

public sealed class DashboardStatisticsTests
{
    [Fact]
    public async Task NativeStatisticsAvoidsMetadataQueriesAndSuppliesZeroStatuses()
    {
        var persistence = Substitute.For<ITickerPersistenceProvider<TimeTickerEntity, CronTickerEntity>, ITickerDashboardStatisticsProvider>();
        var statistics = (ITickerDashboardStatisticsProvider)persistence;
        statistics.GetOverallJobStatusCountsAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyDictionary<TickerStatus, int>>(new Dictionary<TickerStatus, int> { [TickerStatus.Done] = 42 }));
        var result = await new TickerDashboardDataService<TimeTickerEntity, CronTickerEntity>(persistence).GetOverallStatusesAsync();
        Assert.Equal(42, result.Single(row => row.Status == TickerStatus.Done).Count);
        Assert.Equal(0, result.Single(row => row.Status == TickerStatus.Idle).Count);
        Assert.Equal(Enum.GetValues<TickerStatus>().Length, result.Count);
        persistence.DidNotReceive().TimeTickersQuery();
        persistence.DidNotReceive().CronTickerOccurrencesQuery();
    }

    [Fact]
    public async Task ThirdPartyProviderRetainsMetadataFallback()
    {
        var persistence = Substitute.For<ITickerPersistenceProvider<TimeTickerEntity, CronTickerEntity>>();
        persistence.TimeTickersQuery().Returns(new InMemoryTickerQueryable<TimeTickerEntity>(
            _ => Task.FromResult(new List<TimeTickerEntity> { new() { Status = TickerStatus.Done } })));
        persistence.CronTickerOccurrencesQuery().Returns(new InMemoryTickerQueryable<CronTickerOccurrenceEntity<CronTickerEntity>>(
            _ => Task.FromResult(new List<CronTickerOccurrenceEntity<CronTickerEntity>> { new() { Status = TickerStatus.Done }, new() { Status = TickerStatus.Failed } })));
        var result = await new TickerDashboardDataService<TimeTickerEntity, CronTickerEntity>(persistence).GetOverallStatusesAsync();
        Assert.Equal(2, result.Single(row => row.Status == TickerStatus.Done).Count);
        Assert.Equal(1, result.Single(row => row.Status == TickerStatus.Failed).Count);
    }
}
