using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using TickerQ.Utilities;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Interfaces.Managers;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Models;
using Xunit;

namespace TickerQ.Tests;

public class TickerOptionsBuilderTests
{
    public sealed class FakeTimeTicker : TimeTickerEntity<FakeTimeTicker> { }
    public sealed class FakeCronTicker : CronTickerEntity { }

    private sealed class FakeExceptionHandler : ITickerExceptionHandler
    {
        public System.Threading.Tasks.Task HandleExceptionAsync(Exception exception, Guid tickerId, TickerType tickerType)
            => System.Threading.Tasks.Task.CompletedTask;

        public System.Threading.Tasks.Task HandleCanceledExceptionAsync(Exception exception, Guid tickerId, TickerType tickerType)
            => System.Threading.Tasks.Task.CompletedTask;
    }

    [Fact]
    public void UseReconciliationEpoch_StoresPositiveStableEpoch()
    {
        var executionContext = new TickerExecutionContext();
        var builder = new TickerOptionsBuilder<FakeTimeTicker, FakeCronTicker>(
            executionContext, new SchedulerOptionsBuilder());

        builder.UseReconciliationEpoch(42);

        Assert.Equal(42, executionContext.OptionsSeeding.ReconciliationEpoch);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void UseReconciliationEpoch_RejectsNonPositiveEpoch(long epoch)
    {
        var builder = new TickerOptionsBuilder<FakeTimeTicker, FakeCronTicker>(
            new TickerExecutionContext(), new SchedulerOptionsBuilder());

        Assert.Throws<ArgumentOutOfRangeException>(() => builder.UseReconciliationEpoch(epoch));
    }

    [Fact]
    public void UseLegacyRuntimePartitionAdoption_BindsExactTargetAndEpoch()
    {
        var executionContext = new TickerExecutionContext();
        var builder = new TickerOptionsBuilder<FakeTimeTicker, FakeCronTicker>(
            executionContext, new SchedulerOptionsBuilder());

        builder.UseLegacyRuntimePartitionAdoption("orders-api", 12);

        Assert.Equal("orders-api", executionContext.OptionsSeeding.DefinedCronApplicationNamespace);
        Assert.Equal(12, executionContext.OptionsSeeding.ReconciliationEpoch);
        Assert.Equal(new TickerQRuntimePartition("orders-api").StorageKey,
            executionContext.OptionsSeeding.LegacyRuntimePartitionAdoption.TargetPartition.StorageKey);
    }

    [Theory]
    [InlineData(null, 1)]
    [InlineData("", 1)]
    [InlineData("orders", 0)]
    [InlineData("orders", -1)]
    public void UseLegacyRuntimePartitionAdoption_RejectsMissingOwnerOrNonPositiveEpoch(
        string owner, long epoch)
    {
        var builder = new TickerOptionsBuilder<FakeTimeTicker, FakeCronTicker>(
            new TickerExecutionContext(), new SchedulerOptionsBuilder());

        Assert.ThrowsAny<ArgumentException>(() =>
            builder.UseLegacyRuntimePartitionAdoption(owner, epoch));
    }

    [Fact]
    public void UseDefinedCronApplicationNamespace_StoresTrimmedExplicitNamespace()
    {
        var executionContext = new TickerExecutionContext();
        var builder = new TickerOptionsBuilder<FakeTimeTicker, FakeCronTicker>(
            executionContext, new SchedulerOptionsBuilder());

        builder.UseDefinedCronApplicationNamespace("  billing-api  ");

        Assert.Equal("billing-api", executionContext.OptionsSeeding.DefinedCronApplicationNamespace);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void UseDefinedCronApplicationNamespace_RejectsMissingNamespace(string value)
    {
        var builder = new TickerOptionsBuilder<FakeTimeTicker, FakeCronTicker>(
            new TickerExecutionContext(), new SchedulerOptionsBuilder());

        Assert.Throws<ArgumentException>(() => builder.UseDefinedCronApplicationNamespace(value));
    }

    [Fact]
    public void MapLegacyDefinedCronOwnership_NormalizesAndPublishesAnIdempotentMapping()
    {
        var executionContext = new TickerExecutionContext();
        var builder = new TickerOptionsBuilder<FakeTimeTicker, FakeCronTicker>(
            executionContext, new SchedulerOptionsBuilder());

        builder.MapLegacyDefinedCronOwnership("  ProcessOrders  ", "  orders-api  ")
            .MapLegacyDefinedCronOwnership("ProcessOrders", "orders-api");

        var mapping = Assert.Single(executionContext.OptionsSeeding.LegacyDefinedCronOwnership);
        Assert.Equal("ProcessOrders", mapping.Key);
        Assert.Equal("orders-api", mapping.Value);
    }

    [Fact]
    public void MapLegacyDefinedCronOwnership_RejectsConflictingOwnerForSameFunction()
    {
        var builder = new TickerOptionsBuilder<FakeTimeTicker, FakeCronTicker>(
            new TickerExecutionContext(), new SchedulerOptionsBuilder());
        builder.MapLegacyDefinedCronOwnership("ProcessOrders", "orders-api");

        var error = Assert.Throws<InvalidOperationException>(() =>
            builder.MapLegacyDefinedCronOwnership(" ProcessOrders ", "billing-api"));

        Assert.Contains("ProcessOrders", error.Message);
        Assert.Contains("orders-api", error.Message);
        Assert.Contains("billing-api", error.Message);
    }

    [Theory]
    [InlineData(null, "orders-api")]
    [InlineData("", "orders-api")]
    [InlineData("   ", "orders-api")]
    [InlineData("ProcessOrders", null)]
    [InlineData("ProcessOrders", "")]
    [InlineData("ProcessOrders", "   ")]
    public void MapLegacyDefinedCronOwnership_RejectsMissingIdentityParts(string function, string owner)
    {
        var builder = new TickerOptionsBuilder<FakeTimeTicker, FakeCronTicker>(
            new TickerExecutionContext(), new SchedulerOptionsBuilder());

        Assert.Throws<ArgumentException>(() => builder.MapLegacyDefinedCronOwnership(function, owner));
    }

    [Fact]
    public void MapLegacyDefinedCronOwnership_RejectsIdentityPartsBeyondPersistedContractLimit()
    {
        var builder = new TickerOptionsBuilder<FakeTimeTicker, FakeCronTicker>(
            new TickerExecutionContext(), new SchedulerOptionsBuilder());
        var oversized = new string('é', 257);

        Assert.Throws<ArgumentException>(() =>
            builder.MapLegacyDefinedCronOwnership(oversized, "orders-api"));
        Assert.Throws<ArgumentException>(() =>
            builder.MapLegacyDefinedCronOwnership("ProcessOrders", oversized));
    }

    [Fact]
    public void AdoptLegacyDefinedCronTickers_IsAnExplicitStickyOptIn()
    {
        var executionContext = new TickerExecutionContext();
        var builder = new TickerOptionsBuilder<FakeTimeTicker, FakeCronTicker>(
            executionContext, new SchedulerOptionsBuilder());

        Assert.Same(builder, builder.AdoptLegacyDefinedCronTickers());
        Assert.Same(builder, builder.AdoptLegacyDefinedCronTickers());
        Assert.True(executionContext.OptionsSeeding.AdoptAllLegacyDefinedCronTickers);
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
    public async System.Threading.Tasks.Task UseTickerSeeder_TimeCancellationOverload_ForwardsHostToken()
    {
        var executionContext = new TickerExecutionContext();
        var builder = new TickerOptionsBuilder<FakeTimeTicker, FakeCronTicker>(
            executionContext, new SchedulerOptionsBuilder());
        var manager = Substitute.For<ITimeTickerManager<FakeTimeTicker>>();
        var services = new ServiceCollection().AddSingleton(manager).BuildServiceProvider();
        using var cts = new System.Threading.CancellationTokenSource();
        await cts.CancelAsync();
        System.Threading.CancellationToken observed = default;

        builder.UseTickerSeeder((ITimeTickerManager<FakeTimeTicker> _, System.Threading.CancellationToken token) =>
        {
            observed = token;
            return System.Threading.Tasks.Task.CompletedTask;
        });

        await executionContext.OptionsSeeding.TimeSeederAction(services, cts.Token);

        Assert.Equal(cts.Token, observed);
        Assert.True(observed.IsCancellationRequested);
    }

    [Fact]
    public async System.Threading.Tasks.Task UseTickerSeeder_CronCancellationOverload_ForwardsHostToken()
    {
        var executionContext = new TickerExecutionContext();
        var builder = new TickerOptionsBuilder<FakeTimeTicker, FakeCronTicker>(
            executionContext, new SchedulerOptionsBuilder());
        var manager = Substitute.For<ICronTickerManager<FakeCronTicker>>();
        var services = new ServiceCollection().AddSingleton(manager).BuildServiceProvider();
        using var cts = new System.Threading.CancellationTokenSource();
        await cts.CancelAsync();
        System.Threading.CancellationToken observed = default;

        builder.UseTickerSeeder((ICronTickerManager<FakeCronTicker> _, System.Threading.CancellationToken token) =>
        {
            observed = token;
            return System.Threading.Tasks.Task.CompletedTask;
        });

        await executionContext.OptionsSeeding.CronSeederAction(services, cts.Token);

        Assert.Equal(cts.Token, observed);
        Assert.True(observed.IsCancellationRequested);
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
    public void DefinedCronRetirementGracePeriod_DefaultsTo24Hours()
    {
        var schedulerOptions = new SchedulerOptionsBuilder();

        Assert.Equal(TimeSpan.FromHours(24), schedulerOptions.DefinedCronRetirementGracePeriod);
    }

    [Fact]
    public void DefinedCronRetirementGracePeriod_AllowsZero_ForImmediateRetirement()
    {
        var schedulerOptions = new SchedulerOptionsBuilder
        {
            DefinedCronRetirementGracePeriod = TimeSpan.Zero
        };

        Assert.Equal(TimeSpan.Zero, schedulerOptions.DefinedCronRetirementGracePeriod);
    }

    [Fact]
    public void DefinedCronRetirementGracePeriod_RejectsNegativeValues()
    {
        var schedulerOptions = new SchedulerOptionsBuilder();

        Assert.Throws<ArgumentOutOfRangeException>(
            () => schedulerOptions.DefinedCronRetirementGracePeriod = TimeSpan.FromSeconds(-1));
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
