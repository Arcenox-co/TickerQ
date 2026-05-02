using System.Linq;
using TickerQ.Dashboard.Infrastructure.Dashboard;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Enums;
using Xunit;

namespace TickerQ.Tests;

public class TimeTickerFilterTests
{
    private static TimeTickerEntity Ticker(TickerStatus status, string function = null, string exceptionMessage = null)
        => new() { Status = status, Function = function, ExceptionMessage = exceptionMessage };

    [Fact]
    public void BuildTimeTickerFilter_NoFilters_ReturnsNull()
    {
        var predicate = TickerDashboardRepository<TimeTickerEntity, CronTickerEntity>
            .BuildTimeTickerFilter(status: null, search: null);

        Assert.Null(predicate);
    }

    [Fact]
    public void BuildTimeTickerFilter_WhitespaceSearch_TreatedAsNoSearch()
    {
        var predicate = TickerDashboardRepository<TimeTickerEntity, CronTickerEntity>
            .BuildTimeTickerFilter(status: null, search: "   ");

        Assert.Null(predicate);
    }

    [Fact]
    public void BuildTimeTickerFilter_StatusOnly_MatchesByStatus()
    {
        var predicate = TickerDashboardRepository<TimeTickerEntity, CronTickerEntity>
            .BuildTimeTickerFilter(TickerStatus.Failed, search: null)
            .Compile();

        Assert.True(predicate(Ticker(TickerStatus.Failed)));
        Assert.False(predicate(Ticker(TickerStatus.Done)));
    }

    [Fact]
    public void BuildTimeTickerFilter_SearchOnly_MatchesFunctionSubstring()
    {
        var predicate = TickerDashboardRepository<TimeTickerEntity, CronTickerEntity>
            .BuildTimeTickerFilter(status: null, search: "Order")
            .Compile();

        Assert.True(predicate(Ticker(TickerStatus.Done, function: "ProcessOrder")));
        Assert.False(predicate(Ticker(TickerStatus.Done, function: "ProcessPayment")));
    }

    [Fact]
    public void BuildTimeTickerFilter_SearchOnly_MatchesExceptionMessageSubstring()
    {
        var predicate = TickerDashboardRepository<TimeTickerEntity, CronTickerEntity>
            .BuildTimeTickerFilter(status: null, search: "timeout")
            .Compile();

        Assert.True(predicate(Ticker(TickerStatus.Failed, function: "DoWork", exceptionMessage: "Connection timeout after 30s")));
        Assert.False(predicate(Ticker(TickerStatus.Failed, function: "DoWork", exceptionMessage: "Permission denied")));
    }

    [Fact]
    public void BuildTimeTickerFilter_SearchOnly_HandlesNullFunctionAndExceptionMessage()
    {
        var predicate = TickerDashboardRepository<TimeTickerEntity, CronTickerEntity>
            .BuildTimeTickerFilter(status: null, search: "anything")
            .Compile();

        Assert.False(predicate(Ticker(TickerStatus.Done, function: null, exceptionMessage: null)));
    }

    [Fact]
    public void BuildTimeTickerFilter_StatusAndSearch_BothMustMatch()
    {
        var predicate = TickerDashboardRepository<TimeTickerEntity, CronTickerEntity>
            .BuildTimeTickerFilter(TickerStatus.Failed, search: "timeout")
            .Compile();

        Assert.True(predicate(Ticker(TickerStatus.Failed, exceptionMessage: "request timeout")));
        Assert.False(predicate(Ticker(TickerStatus.Done, exceptionMessage: "request timeout")));
        Assert.False(predicate(Ticker(TickerStatus.Failed, exceptionMessage: "other error")));
    }

    [Fact]
    public void BuildTimeTickerFilter_SearchOnly_MatchesAcrossEitherColumn()
    {
        var predicate = TickerDashboardRepository<TimeTickerEntity, CronTickerEntity>
            .BuildTimeTickerFilter(status: null, search: "shared")
            .Compile();

        Assert.True(predicate(Ticker(TickerStatus.Done, function: "sharedJob", exceptionMessage: null)));
        Assert.True(predicate(Ticker(TickerStatus.Failed, function: "DoWork", exceptionMessage: "shared lock contention")));
    }
}
