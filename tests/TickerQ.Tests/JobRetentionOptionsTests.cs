using System;
using TickerQ.Utilities;
using TickerQ.Utilities.Entities;
using Xunit;

namespace TickerQ.Tests;

public class JobRetentionOptionsTests
{
    private sealed class FakeTimeTicker : TimeTickerEntity<FakeTimeTicker> { }
    private sealed class FakeCronTicker : CronTickerEntity { }

    private static TickerOptionsBuilder<FakeTimeTicker, FakeCronTicker> NewBuilder(out SchedulerOptionsBuilder scheduler)
    {
        var executionContext = new TickerExecutionContext();
        scheduler = new SchedulerOptionsBuilder();
        return new TickerOptionsBuilder<FakeTimeTicker, FakeCronTicker>(executionContext, scheduler);
    }

    [Fact]
    public void Defaults_AreDisabled_WithNoWindows_AndSensibleBounds()
    {
        var options = new JobRetentionOptions();

        Assert.Null(options.DeleteSucceededAfter);
        Assert.Null(options.DeleteFailedAfter);
        Assert.Null(options.DeleteCancelledAfter);
        Assert.Null(options.DeleteSkippedAfter);
        Assert.False(options.IsEnabled);

        Assert.Equal(TimeSpan.FromHours(1), options.SweepInterval);
        Assert.Equal(500, options.BatchSize);
        Assert.Equal(10, options.MaxBatchesPerSweep);
        Assert.Equal(1_000, options.MaxNodesPerChain);
    }

    [Theory]
    [InlineData(0)] // succeeded
    [InlineData(1)] // failed
    [InlineData(2)] // cancelled
    [InlineData(3)] // skipped
    public void IsEnabled_True_WhenAnySingleWindowSet(int which)
    {
        var options = new JobRetentionOptions();
        switch (which)
        {
            case 0: options.DeleteSucceededAfter = TimeSpan.FromDays(7); break;
            case 1: options.DeleteFailedAfter = TimeSpan.FromDays(7); break;
            case 2: options.DeleteCancelledAfter = TimeSpan.FromDays(7); break;
            case 3: options.DeleteSkippedAfter = TimeSpan.FromDays(7); break;
        }

        Assert.True(options.IsEnabled);
    }

    [Fact]
    public void ConfigureJobRetention_InvokesDelegate_AndStoresOptions()
    {
        var builder = NewBuilder(out _);

        builder.ConfigureJobRetention(o => o.DeleteSucceededAfter = TimeSpan.FromDays(30));

        Assert.True(builder.JobRetention.IsEnabled);
        Assert.Equal(TimeSpan.FromDays(30), builder.JobRetention.DeleteSucceededAfter);
    }

    [Fact]
    public void ConfigureJobRetention_NotCalled_LeavesRetentionDisabled()
    {
        var builder = NewBuilder(out _);

        Assert.False(builder.JobRetention.IsEnabled);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ConfigureJobRetention_Rejects_NonPositiveWindow(int seconds)
    {
        var builder = NewBuilder(out _);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            builder.ConfigureJobRetention(o => o.DeleteSucceededAfter = TimeSpan.FromSeconds(seconds)));
    }

    [Fact]
    public void ConfigureJobRetention_Rejects_NonPositiveSweepInterval()
    {
        var builder = NewBuilder(out _);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            builder.ConfigureJobRetention(o =>
            {
                o.DeleteSucceededAfter = TimeSpan.FromDays(1);
                o.SweepInterval = TimeSpan.Zero;
            }));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void ConfigureJobRetention_Rejects_NonPositiveBatchSize(int batchSize)
    {
        var builder = NewBuilder(out _);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            builder.ConfigureJobRetention(o =>
            {
                o.DeleteFailedAfter = TimeSpan.FromDays(1);
                o.BatchSize = batchSize;
            }));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void ConfigureJobRetention_Rejects_NonPositiveMaxBatches(int maxBatches)
    {
        var builder = NewBuilder(out _);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            builder.ConfigureJobRetention(o =>
            {
                o.DeleteFailedAfter = TimeSpan.FromDays(1);
                o.MaxBatchesPerSweep = maxBatches;
            }));
    }

    [Theory]
    [InlineData(JobRetentionOptions.MaxBatchSize + 1)]
    [InlineData(1_000_000)]
    public void ConfigureJobRetention_Rejects_BatchSizeAboveHardUpperBound(int batchSize)
    {
        var builder = NewBuilder(out _);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            builder.ConfigureJobRetention(o =>
            {
                o.DeleteFailedAfter = TimeSpan.FromDays(1);
                o.BatchSize = batchSize;
            }));
    }

    [Theory]
    [InlineData(JobRetentionOptions.MaxBatchesPerSweepLimit + 1)]
    [InlineData(100_000)]
    public void ConfigureJobRetention_Rejects_MaxBatchesAboveHardUpperBound(int maxBatches)
    {
        var builder = NewBuilder(out _);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            builder.ConfigureJobRetention(o =>
            {
                o.DeleteFailedAfter = TimeSpan.FromDays(1);
                o.MaxBatchesPerSweep = maxBatches;
            }));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(JobRetentionOptions.MaxNodesPerChainLimit + 1)]
    public void ConfigureJobRetention_Rejects_MaxNodesPerChainOutsideBounds(int maxNodes)
    {
        var builder = NewBuilder(out _);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            builder.ConfigureJobRetention(o =>
            {
                o.DeleteFailedAfter = TimeSpan.FromDays(1);
                o.MaxNodesPerChain = maxNodes;
            }));
    }

    [Fact]
    public void ConfigureJobRetention_Accepts_ValuesAtHardUpperBounds()
    {
        var builder = NewBuilder(out _);

        builder.ConfigureJobRetention(o =>
        {
            o.DeleteFailedAfter = TimeSpan.FromDays(1);
            o.BatchSize = JobRetentionOptions.MaxBatchSize;
            o.MaxBatchesPerSweep = JobRetentionOptions.MaxBatchesPerSweepLimit;
        });

        Assert.Equal(JobRetentionOptions.MaxBatchSize, builder.JobRetention.BatchSize);
        Assert.Equal(JobRetentionOptions.MaxBatchesPerSweepLimit, builder.JobRetention.MaxBatchesPerSweep);
    }

    [Fact]
    public void ConfigureJobRetention_Accepts_ValidConfiguration()
    {
        var builder = NewBuilder(out _);

        builder.ConfigureJobRetention(o =>
        {
            o.DeleteSucceededAfter = TimeSpan.FromDays(7);
            o.DeleteFailedAfter = TimeSpan.FromDays(30);
            o.SweepInterval = TimeSpan.FromMinutes(15);
            o.BatchSize = 100;
            o.MaxBatchesPerSweep = 5;
        });

        Assert.True(builder.JobRetention.IsEnabled);
    }
}
