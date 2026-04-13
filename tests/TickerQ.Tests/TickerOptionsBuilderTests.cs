using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using TickerQ.Utilities;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Interfaces.Managers;
using TickerQ.Utilities.Enums;
using Xunit;

namespace TickerQ.Tests;

public class TickerOptionsBuilderTests
{
    private sealed class FakeTimeTicker : TimeTickerEntity<FakeTimeTicker> { }
    private sealed class FakeCronTicker : CronTickerEntity { }

    private sealed class FakeExceptionHandler : ITickerExceptionHandler
    {
        public System.Threading.Tasks.Task HandleExceptionAsync(Exception exception, Guid tickerId, TickerType tickerType)
            => System.Threading.Tasks.Task.CompletedTask;

        public System.Threading.Tasks.Task HandleCanceledExceptionAsync(Exception exception, Guid tickerId, TickerType tickerType)
            => System.Threading.Tasks.Task.CompletedTask;
    }

    [Fact]
    public void ConfigureRequestJsonOptions_Initializes_And_Invokes_Config()
    {
        var executionContext = new TickerExecutionContext();
        var schedulerOptions = new SchedulerOptionsBuilder();

        var builder = new TickerOptionsBuilder<FakeTimeTicker, FakeCronTicker>(executionContext, schedulerOptions);

        builder.ConfigureRequestJsonOptions(options =>
        {
            options.PropertyNameCaseInsensitive = true;
            options.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
        });

        var jsonOptions = typeof(TickerOptionsBuilder<FakeTimeTicker, FakeCronTicker>)
            .GetProperty("RequestJsonSerializerOptions", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(builder) as JsonSerializerOptions;

        Assert.NotNull(jsonOptions);
        Assert.True(jsonOptions!.PropertyNameCaseInsensitive);
        Assert.Equal(JsonIgnoreCondition.WhenWritingNull, jsonOptions.DefaultIgnoreCondition);
    }

    [Fact]
    public void UseGZipCompression_Sets_Flag()
    {
        var executionContext = new TickerExecutionContext();
        var schedulerOptions = new SchedulerOptionsBuilder();

        var builder = new TickerOptionsBuilder<FakeTimeTicker, FakeCronTicker>(executionContext, schedulerOptions);

        builder.UseGZipCompression();

        var flag = typeof(TickerOptionsBuilder<FakeTimeTicker, FakeCronTicker>)
            .GetProperty("RequestGZipCompressionEnabled", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(builder);

        var boolFlag = Assert.IsType<bool>(flag);
        Assert.True(boolFlag);
    }

    [Fact]
    public void IgnoreSeedDefinedCronTickers_Disables_Seeding_Flag()
    {
        var executionContext = new TickerExecutionContext();
        var schedulerOptions = new SchedulerOptionsBuilder();

        var builder = new TickerOptionsBuilder<FakeTimeTicker, FakeCronTicker>(executionContext, schedulerOptions);

        builder.IgnoreSeedDefinedCronTickers();

        var flag = typeof(TickerOptionsBuilder<FakeTimeTicker, FakeCronTicker>)
            .GetProperty("SeedDefinedCronTickers", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(builder);

        var boolFlag = Assert.IsType<bool>(flag);
        Assert.False(boolFlag);
    }

    [Fact]
    public void SetExceptionHandler_Sets_Handler_Type()
    {
        var executionContext = new TickerExecutionContext();
        var schedulerOptions = new SchedulerOptionsBuilder();

        var builder = new TickerOptionsBuilder<FakeTimeTicker, FakeCronTicker>(executionContext, schedulerOptions);

        builder.SetExceptionHandler<FakeExceptionHandler>();

        var handlerType = typeof(TickerOptionsBuilder<FakeTimeTicker, FakeCronTicker>)
            .GetProperty("TickerExceptionHandlerType", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(builder) as Type;

        Assert.Equal(typeof(FakeExceptionHandler), handlerType);
    }

    [Fact]
    public void UseTickerSeeder_Time_Sets_TimeSeederAction()
    {
        var executionContext = new TickerExecutionContext();
        var schedulerOptions = new SchedulerOptionsBuilder();

        var builder = new TickerOptionsBuilder<FakeTimeTicker, FakeCronTicker>(executionContext, schedulerOptions);

        builder.UseTickerSeeder(async (ITimeTickerManager<FakeTimeTicker> _) => { await System.Threading.Tasks.Task.CompletedTask; });

        var seeder = typeof(TickerOptionsBuilder<FakeTimeTicker, FakeCronTicker>)
            .GetProperty("TimeSeederAction", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(builder);

        Assert.NotNull(seeder);
    }

    [Fact]
    public void UseTickerSeeder_Cron_Sets_CronSeederAction()
    {
        var executionContext = new TickerExecutionContext();
        var schedulerOptions = new SchedulerOptionsBuilder();

        var builder = new TickerOptionsBuilder<FakeTimeTicker, FakeCronTicker>(executionContext, schedulerOptions);

        builder.UseTickerSeeder(async (ICronTickerManager<FakeCronTicker> _) => { await System.Threading.Tasks.Task.CompletedTask; });

        var seeder = typeof(TickerOptionsBuilder<FakeTimeTicker, FakeCronTicker>)
            .GetProperty("CronSeederAction", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(builder);

        Assert.NotNull(seeder);
    }

    [Fact]
    public void ConfigureScheduler_Invokes_Delegate()
    {
        var executionContext = new TickerExecutionContext();
        var schedulerOptions = new SchedulerOptionsBuilder();

        var builder = new TickerOptionsBuilder<FakeTimeTicker, FakeCronTicker>(executionContext, schedulerOptions);

        builder.ConfigureScheduler(options =>
        {
            options.MaxConcurrency = 42;
            options.NodeIdentifier = "test-node";
        });

        Assert.Equal(42, schedulerOptions.MaxConcurrency);
        Assert.Equal("test-node", schedulerOptions.NodeIdentifier);
    }

    [Fact]
    public void SkipStaleCronOccurrencesOnStartup_DefaultThreshold_SetsFiveSeconds()
    {
        var executionContext = new TickerExecutionContext();
        var schedulerOptions = new SchedulerOptionsBuilder();

        var builder = new TickerOptionsBuilder<FakeTimeTicker, FakeCronTicker>(executionContext, schedulerOptions);

        builder.SkipStaleCronOccurrencesOnStartup();

        Assert.Equal(TimeSpan.FromSeconds(5), schedulerOptions.StaleCronOccurrenceThreshold);
    }

    [Fact]
    public void SkipStaleCronOccurrencesOnStartup_CustomThreshold_SetsValue()
    {
        var executionContext = new TickerExecutionContext();
        var schedulerOptions = new SchedulerOptionsBuilder();

        var builder = new TickerOptionsBuilder<FakeTimeTicker, FakeCronTicker>(executionContext, schedulerOptions);

        builder.SkipStaleCronOccurrencesOnStartup(TimeSpan.FromMinutes(2));

        Assert.Equal(TimeSpan.FromMinutes(2), schedulerOptions.StaleCronOccurrenceThreshold);
    }

    [Fact]
    public void SkipStaleCronOccurrencesOnStartup_NotCalled_ThresholdRemainsZero()
    {
        var executionContext = new TickerExecutionContext();
        var schedulerOptions = new SchedulerOptionsBuilder();

        // Don't call SkipStaleCronOccurrencesOnStartup
        _ = new TickerOptionsBuilder<FakeTimeTicker, FakeCronTicker>(executionContext, schedulerOptions);

        Assert.Equal(TimeSpan.Zero, schedulerOptions.StaleCronOccurrenceThreshold);
    }

    [Fact]
    public void DisableBackgroundServices_Sets_Flag_To_False()
    {
        var executionContext = new TickerExecutionContext();
        var schedulerOptions = new SchedulerOptionsBuilder();

        var builder = new TickerOptionsBuilder<FakeTimeTicker, FakeCronTicker>(executionContext, schedulerOptions);

        // Default should be true
        var defaultFlag = typeof(TickerOptionsBuilder<FakeTimeTicker, FakeCronTicker>)
            .GetProperty("RegisterBackgroundServices", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(builder);
        var defaultBoolFlag = Assert.IsType<bool>(defaultFlag);
        Assert.True(defaultBoolFlag);

        // After calling DisableBackgroundServices, should be false
        builder.DisableBackgroundServices();

        var flag = typeof(TickerOptionsBuilder<FakeTimeTicker, FakeCronTicker>)
            .GetProperty("RegisterBackgroundServices", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(builder);

        var boolFlag = Assert.IsType<bool>(flag);
        Assert.False(boolFlag);
    }
}
