using System;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Managers;
using Xunit;

namespace TickerQ.Tests;

/// <summary>
/// Unit tests for <see cref="PeriodicTickerManager{TPeriodicTicker}.CalculateNextExecution"/>.
/// The method is internal-static; reached here via InternalsVisibleTo("TickerQ.Tests").
/// </summary>
public class PeriodicTickerManagerCalculateNextExecutionTests
{
    private static readonly DateTime Now = new(2026, 5, 20, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void StartTimeInFuture_ReturnsStartTime()
    {
        var ticker = new PeriodicTickerEntity
        {
            Interval = TimeSpan.FromMinutes(5),
            StartTime = Now.AddHours(1)
        };

        var next = PeriodicTickerManager<PeriodicTickerEntity>.CalculateNextExecution(ticker, Now);

        Assert.Equal(ticker.StartTime!.Value, next);
    }

    [Fact]
    public void NeverExecuted_NoStartTime_ReturnsNow()
    {
        var ticker = new PeriodicTickerEntity
        {
            Interval = TimeSpan.FromMinutes(5)
        };

        var next = PeriodicTickerManager<PeriodicTickerEntity>.CalculateNextExecution(ticker, Now);

        Assert.Equal(Now, next);
    }

    [Fact]
    public void NeverExecuted_StartTimeInPast_ReturnsStartTime()
    {
        var startTime = Now.AddMinutes(-10);
        var ticker = new PeriodicTickerEntity
        {
            Interval = TimeSpan.FromMinutes(5),
            StartTime = startTime
        };

        var next = PeriodicTickerManager<PeriodicTickerEntity>.CalculateNextExecution(ticker, Now);

        Assert.Equal(startTime, next);
    }

    [Fact]
    public void NormalAdvance_LastPlusIntervalNotYetReached_ReturnsLastPlusInterval()
    {
        // Last executed 2 min ago, interval 5 min → next = last + 5 min (still in future)
        var lastExecuted = Now.AddMinutes(-2);
        var ticker = new PeriodicTickerEntity
        {
            Interval = TimeSpan.FromMinutes(5),
            LastExecutedAt = lastExecuted
        };

        var next = PeriodicTickerManager<PeriodicTickerEntity>.CalculateNextExecution(ticker, Now);

        Assert.Equal(lastExecuted.AddMinutes(5), next);
    }

    [Fact]
    public void CatchUpAfterGap_AlignsToNextIntervalBoundary()
    {
        // Interval 1 min, last executed 10.5 min ago.
        // 10 intervals fully elapsed; next aligned slot = last + 11 * interval = now + 0.5 min.
        var lastExecuted = Now.AddSeconds(-630); // 10.5 min ago
        var ticker = new PeriodicTickerEntity
        {
            Interval = TimeSpan.FromMinutes(1),
            LastExecutedAt = lastExecuted
        };

        var next = PeriodicTickerManager<PeriodicTickerEntity>.CalculateNextExecution(ticker, Now);

        var expected = lastExecuted.AddMinutes(11);
        Assert.Equal(expected, next);
        Assert.True(next > Now, "next-execution must be in the future after catch-up alignment");
    }

    [Fact]
    public void PastEndTime_ReturnsDateTimeMaxValue()
    {
        var ticker = new PeriodicTickerEntity
        {
            Interval = TimeSpan.FromMinutes(5),
            LastExecutedAt = Now.AddMinutes(-1),
            EndTime = Now.AddMinutes(1) // less than next exec (now + 4min)
        };

        var next = PeriodicTickerManager<PeriodicTickerEntity>.CalculateNextExecution(ticker, Now);

        Assert.Equal(DateTime.MaxValue, next);
    }

    [Fact]
    public void NextExecutionExactlyAtEndTime_IsAllowed()
    {
        // EndTime is exclusive only when nextExecution > EndTime; equality must allow execution.
        // lastExecuted = Now - 4 min, interval = 5 min => nextExecution = Now + 1 min (no catch-up branch).
        var lastExecuted = Now.AddMinutes(-4);
        var endTime = Now.AddMinutes(1);
        var ticker = new PeriodicTickerEntity
        {
            Interval = TimeSpan.FromMinutes(5),
            LastExecutedAt = lastExecuted,
            EndTime = endTime
        };

        var next = PeriodicTickerManager<PeriodicTickerEntity>.CalculateNextExecution(ticker, Now);

        Assert.Equal(endTime, next);
    }
}

