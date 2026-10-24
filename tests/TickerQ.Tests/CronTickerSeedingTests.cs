using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using TickerQ.BackgroundServices;
using TickerQ.Utilities;
using TickerQ.Utilities.Base;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Infrastructure;
using TickerQ.Utilities.Interfaces.Managers;
using TickerQ.Utilities.Models;
using Xunit;

namespace TickerQ.Tests;

/// <summary>
/// Cron tickers are seeded only from options.SeedCronTickers(...). The function attribute and the
/// code-first registration builder no longer declare crons.
/// </summary>
[Collection("TickerFunctionProviderState")]
public class CronTickerSeedingTests : IDisposable
{
    public CronTickerSeedingTests() => ResetProvider();

    public void Dispose() => ResetProvider();

    [Fact]
    public void Attribute_cron_constructor_is_a_compile_error()
    {
        var cronConstructor = typeof(TickerFunctionAttribute).GetConstructor(
            [typeof(string), typeof(string), typeof(TickerTaskPriority), typeof(int)]);
        var obsolete = cronConstructor?.GetCustomAttribute<ObsoleteAttribute>();

        Assert.NotNull(obsolete);
        Assert.True(obsolete!.IsError);
        Assert.Contains("SeedCronTickers", obsolete.Message);

        var supported = typeof(TickerFunctionAttribute).GetConstructor(
            [typeof(string), typeof(TickerTaskPriority), typeof(int)]);
        Assert.NotNull(supported);
        Assert.Null(supported!.GetCustomAttribute<ObsoleteAttribute>());
    }

    [Fact]
    public void Registration_builder_WithCron_is_a_compile_error()
    {
        var withCron = typeof(TickerFunctionBuilder).GetMethod(nameof(TickerFunctionBuilder.WithCron));
        var obsolete = withCron?.GetCustomAttribute<ObsoleteAttribute>();

        Assert.NotNull(obsolete);
        Assert.True(obsolete!.IsError);
    }

    [Fact]
    public void Builder_defaults_the_key_to_the_function_and_maps_retries_and_timeout()
    {
        var builder = new CronTickerSeedBuilder()
            .Add("DailyReport", "0 0 9 * * *", cron => cron
                .WithRetries(2, 300, 900)
                .WithTimeout(TimeSpan.FromSeconds(90.5)))
            .Add("DailyReport", "0 0 18 * * *", cron => cron.WithKey("DailyReport:evening"));

        var morning = builder.Seeds[0];
        Assert.Equal("DailyReport", morning.Key);
        Assert.Equal(2, morning.Retries);
        Assert.Equal([300, 900], morning.RetryIntervals);
        Assert.Equal(91, morning.TimeoutSeconds);

        var evening = builder.Seeds[1];
        Assert.Equal("DailyReport:evening", evening.Key);
        Assert.Equal(0, evening.Retries);
        Assert.Null(evening.RetryIntervals);
        Assert.Null(evening.TimeoutSeconds);
    }

    [Fact]
    public void Builder_rejects_duplicate_keys_and_invalid_settings()
    {
        var builder = new CronTickerSeedBuilder().Add("DailyReport", "0 0 9 * * *");

        var duplicate = Assert.Throws<InvalidOperationException>(() => builder.Add("DailyReport", "0 0 10 * * *"));
        Assert.Contains("WithKey", duplicate.Message);
        Assert.Throws<ArgumentException>(() => builder.Add(" ", "* * * * *"));
        Assert.Throws<ArgumentException>(() => builder.Add("Other", " "));
        Assert.Throws<ArgumentOutOfRangeException>(() => builder.Add("Other", "* * * * *", c => c.WithRetries(-1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => builder.Add("Other", "* * * * *", c => c.WithRetries(1, -5)));
        Assert.Throws<ArgumentOutOfRangeException>(() => builder.Add("Other", "* * * * *", c => c.WithTimeout(TimeSpan.Zero)));
    }

    [Fact]
    public void Options_builder_combines_declarations_from_several_calls()
    {
        var options = new TickerOptionsBuilder<TimeTickerEntity, CronTickerEntity>(
            new TickerExecutionContext(), new SchedulerOptionsBuilder());
        options.SeedCronTickers(crons => crons.Add("A", "* * * * *"));
        options.SeedCronTickers((crons, _) => crons.Add("B", "* * * * *"));

        var builder = new CronTickerSeedBuilder();
        foreach (var declare in ((ITickerOptionsSeeding)options).CronTickerSeeds)
            declare(builder, null!);

        Assert.Equal(["A", "B"], builder.Seeds.Select(seed => seed.Function));
    }

    [Fact]
    public void A_function_with_a_registry_cron_is_not_seeded_unless_declared()
    {
        Register("LegacyCron", "* * * * *");
        Register("Declared", null);
        TickerFunctionProvider.Build();

        var seeds = TickerQInitializerHostedService.BuildDefinedCronSeeds(
            Services(crons => crons.Add("Declared", "0 */5 * * * *", c => c.WithRetries(3, 60))));

        var seed = Assert.Single(seeds);
        Assert.Equal("Declared", seed.Function);
        Assert.Equal("Declared", seed.StableDefinitionId);
        Assert.Equal("0 */5 * * * *", seed.Expression);
        Assert.Equal(3, seed.Retries);
        Assert.Equal([60], seed.RetryIntervals);
        Assert.True(seed.CanSeed);
    }

    [Fact]
    public void Configuration_placeholders_are_resolved_at_startup()
    {
        Register("Reports", null);
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Reports:Cron"] = "*/10 * * * *" })
            .Build();
        TickerFunctionProvider.Build();

        var seed = Assert.Single(TickerQInitializerHostedService.BuildDefinedCronSeeds(
            Services(crons => crons.Add("Reports", "%Reports:Cron%"), configuration)));

        Assert.Equal("*/10 * * * *", seed.Expression);
    }

    [Theory]
    [InlineData("Missing", "* * * * *", "not registered")]
    [InlineData("Reports", "%Reports:Missing%", "has no value")]
    [InlineData("Reports", "not a cron", "invalid cron expression")]
    public void Startup_fails_clearly_for_bad_seeds(string function, string expression, string message)
    {
        Register("Reports", null);
        TickerFunctionProvider.Build();

        var error = Assert.Throws<InvalidOperationException>(() =>
            TickerQInitializerHostedService.BuildDefinedCronSeeds(Services(crons => crons.Add(function, expression))));

        Assert.Contains(message, error.Message);
    }

    [Fact]
    public async Task Startup_reconciles_only_declared_crons()
    {
        Register("LegacyCron", "* * * * *");
        Register("Declared", null);
        var internalManager = Substitute.For<IInternalTickerManager>();
        ActivationEpochTestDouble.Configure(internalManager);
        DefinedCronTickerSeed[]? captured = null;
        internalManager
            .When(m => m.MigrateDefinedCronTickers(Arg.Any<DefinedCronSeedManifest>(), Arg.Any<CancellationToken>()))
            .Do(call => captured = call.Arg<DefinedCronSeedManifest>().Seeds.ToArray());

        var context = new TickerExecutionContext
        {
            OptionsSeeding = new TestOptionsSeeding
            {
                CronTickerSeeds = [(crons, _) => crons.Add("Declared", "0 0 9 * * *")],
            },
        };
        var configuration = Substitute.For<IConfiguration>();
        var services = new ServiceCollection();
        services.AddSingleton(context);
        services.AddSingleton(configuration);
        services.AddSingleton(internalManager);
        services.AddSingleton(new SchedulerOptionsBuilder());
        var provider = services.BuildServiceProvider();

        await new TickerQInitializerHostedService(context, provider, configuration, LicenseTestState.Active())
        {
            InitializationRequested = true
        }.StartAsync(CancellationToken.None);

        Assert.NotNull(captured);
        Assert.Equal(["Declared"], captured!.Select(seed => seed.Function));
    }

    [Fact]
    public void Dashboard_marks_seeded_crons()
    {
        Assert.True(TickerDashboardDataService<TimeTickerEntity, CronTickerEntity>.IsSeededCron(
            new CronTickerEntity { SeedKey = "tq:cron-seed:v2:abc" }));
        Assert.False(TickerDashboardDataService<TimeTickerEntity, CronTickerEntity>.IsSeededCron(
            new CronTickerEntity()));
    }

    private static IServiceProvider Services(Action<CronTickerSeedBuilder> seeds, IConfiguration? configuration = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton(new TickerExecutionContext
        {
            OptionsSeeding = new TestOptionsSeeding { CronTickerSeeds = [(builder, _) => seeds(builder)] },
        });
        if (configuration != null) services.AddSingleton(configuration);
        return services.BuildServiceProvider();
    }

    private static void Register(string name, string? cron)
    {
        TickerFunctionProvider.RegisterFunctions(
            new Dictionary<string, (string, TickerTaskPriority, TickerFunctionDelegate, int)>
            {
                [name] = (cron ?? string.Empty, TickerTaskPriority.Normal, NoOp, 1)
            });
        TickerFunctionProvider.RegisterDescriptors(new Dictionary<string, TickerFunctionDescriptor>
        {
            [name] = new(name, TickerTaskPriority.Normal, cron),
        });
    }

    private static Task NoOp(CancellationToken ct, IServiceProvider sp, TickerFunctionContext c) => Task.CompletedTask;

    private static void ResetProvider()
    {
        var type = typeof(TickerFunctionProvider);
        const BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;

        type.GetField("TickerFunctions", flags)!.SetValue(null,
            System.Collections.Frozen.FrozenDictionary<string, (string, TickerTaskPriority, TickerFunctionDelegate, int)>.Empty);
        type.GetField("TickerFunctionRequestTypes", flags)!.SetValue(null,
            System.Collections.Frozen.FrozenDictionary<string, (string, Type)>.Empty);
        type.GetField("TickerFunctionRequestInfos", flags)!.SetValue(null,
            System.Collections.Frozen.FrozenDictionary<string, (string, string)>.Empty);
        type.GetField("_snapshot", flags)!.SetValue(null, TickerFunctionRegistrySnapshot.Empty);
        type.GetField("_functionRegistrations", flags)!.SetValue(null, null);
        type.GetField("_requestTypeRegistrations", flags)!.SetValue(null, null);
        type.GetField("_requestInfoRegistrations", flags)!.SetValue(null, null);
        type.GetField("_runtimeRequestRegistrations", flags)!.SetValue(null, null);
        ((System.Collections.IList)type.GetField("_pendingDescriptors", flags)!.GetValue(null)!).Clear();
        type.GetProperty("IsBuilt", flags)!.SetValue(null, false);
    }

    private sealed class TestOptionsSeeding : ITickerOptionsSeeding
    {
        public bool SeedDefinedCronTickers => true;
        public long ReconciliationEpoch => 1;
        public string ApplicationNamespace => "cron-seeding-tests";
        public Func<IServiceProvider, CancellationToken, Task>? TimeSeederAction => null;
        public Func<IServiceProvider, CancellationToken, Task>? CronSeederAction => null;
        public IReadOnlyList<Action<CronTickerSeedBuilder, IServiceProvider>> CronTickerSeeds { get; init; } = [];
    }
}
