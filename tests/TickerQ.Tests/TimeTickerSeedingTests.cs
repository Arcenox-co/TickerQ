using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TickerQ.DependencyInjection;
using TickerQ.Utilities;
using TickerQ.Utilities.Base;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Interfaces.Managers;
using TickerQ.Utilities.Models;
using Xunit;

namespace TickerQ.Tests;

/// <summary>
/// One-time work declared with options.SeedTimeTickers(...) uses the time-ticker chain builder, the time
/// manager's validation, and the keyed AddOnceAsync identity: one ticker per key, however often it runs.
/// </summary>
[Collection("TickerFunctionProviderState")]
public sealed class TimeTickerSeedingTests : IDisposable
{
    private readonly string _prefix = "tseed" + Guid.NewGuid().ToString("N")[..8] + ".";
    private readonly string _namespace = "time-seed-tests-" + Guid.NewGuid().ToString("N")[..8];

    public TimeTickerSeedingTests() => ResetProvider();

    public void Dispose() => ResetProvider();

    private string Fn(string name) => _prefix + name;

    [Fact]
    public void Nothing_is_seeded_unless_declared()
    {
        var services = Services(_ => { });

        Assert.Null(services.GetRequiredService<TickerExecutionContext>().OptionsSeeding.TimeTickerSeedAction);
    }

    [Fact]
    public async Task Seeds_carry_the_full_definition_and_running_again_creates_no_duplicate()
    {
        Register("Warm", "Import", "Index");
        var runAt = DateTime.UtcNow.AddDays(1);
        var services = Services(options => options.SeedTimeTickers(tickers => tickers
            .Add(Fn("Warm"))
            .Add("import-v1", ticker => ticker
                .RunAt(runAt)
                .UsingPolicy("business HOURS")
                .WhenBlocked(BlockedOccurrenceBehavior.MoveForward)
                .WithDescription("Initial import")
                .StartWith(Fn("Import"), step => { step.Retries = 3; step.RetryIntervals = [60]; })
                .Then(Fn("Index")))));
        await CreatePolicy(services, "Business hours");

        await Seed(services);
        await Seed(services);

        var warm = Assert.Single(await Tickers(services, Fn("Warm")));
        Assert.Equal(Fn("Warm"), warm.InitIdentifier);
        var import = Assert.Single(await Tickers(services, Fn("Import")));
        Assert.Equal("import-v1", import.InitIdentifier);
        Assert.Equal(runAt, import.ExecutionTime);
        Assert.Equal("Business hours", import.SchedulePolicyName);
        Assert.Equal("Initial import", import.Description);
        Assert.Equal(3, import.Retries);
        Assert.Equal([60], import.RetryIntervals);
        var index = Assert.Single(import.Children);
        Assert.Equal(Fn("Index"), index.Function);
    }

    [Fact]
    public async Task An_existing_key_is_left_untouched_and_a_new_key_creates_new_work()
    {
        Register("Import");
        var first = Services(options => options.SeedTimeTickers(tickers => tickers
            .Add("import-v1", ticker => ticker.WithDescription("first").StartWith(Fn("Import")))));
        await Seed(first);
        var created = Assert.Single(await Tickers(first, Fn("Import")));

        // Same key, changed definition, and a policy that does not exist: the stored ticker is not
        // re-validated or changed, because it is never recreated.
        await Seed(Services(options => options.SeedTimeTickers(tickers => tickers
            .Add("import-v1", ticker => ticker.WithDescription("second").UsingPolicy("Missing").StartWith(Fn("Import"))))));
        var kept = Assert.Single(await Tickers(first, Fn("Import")));
        Assert.Equal(created.Id, kept.Id);
        Assert.Equal("first", kept.Description);

        await Seed(Services(options => options.SeedTimeTickers(tickers => tickers
            .Add("import-v2", ticker => ticker.StartWith(Fn("Import"))))));
        Assert.Equal(["import-v1", "import-v2"],
            (await Tickers(first, Fn("Import"))).Select(t => t.InitIdentifier).Order());
    }

    [Fact]
    public async Task A_key_created_with_AddOnceAsync_is_the_same_seed()
    {
        Register("Import");
        var services = Services(options => options.SeedTimeTickers(tickers => tickers
            .Add("import-v1", ticker => ticker.StartWith(Fn("Import")))));
        await BeginEpoch(services);
        using (StartupSeederAdmissionContext.Enter(new ReconciliationActivationScope(_namespace).ScopeKey, 1))
            await services.GetRequiredService<ITimeTickerManager<TimeTickerEntity>>()
                .AddOnceAsync("import-v1", new TimeTickerEntity { Function = Fn("Import") });

        await Seed(services);

        Assert.Single(await Tickers(services, Fn("Import")));
    }

    [Fact]
    public async Task An_invalid_seed_fails_startup_before_anything_is_written()
    {
        Register("Warm", "Import");
        var services = Services(options => options.SeedTimeTickers(tickers => tickers
            .Add(Fn("Warm"))
            .Add("import-v1", ticker => ticker.UsingPolicy("Missing policy").StartWith(Fn("Import")))));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Seed(services));

        Assert.Contains("Seeded time ticker 'import-v1' is invalid", error.Message);
        Assert.Contains("Schedule policy 'Missing policy' does not exist", error.Message);
        Assert.Empty(await Tickers(services, Fn("Warm")));
    }

    [Theory]
    [InlineData("Missing", "is invalid")]
    [InlineData(null, "must start with StartWith")]
    public async Task Bad_seeds_fail_startup_clearly(string? function, string message)
    {
        Register("Import");
        var services = Services(options => options.SeedTimeTickers(tickers => tickers.Add("job", ticker =>
        {
            if (function != null) ticker.StartWith(Fn(function));
        })));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Seed(services));

        Assert.Contains("Seeded time ticker 'job'", error.Message);
        Assert.Contains(message, error.Message);
    }

    [Fact]
    public async Task Duplicate_keys_fail_startup()
    {
        Register("Import");
        var services = Services(options => options
            .SeedTimeTickers(tickers => tickers.Add(Fn("Import")))
            .SeedTimeTickers((tickers, _) => tickers.Add(ticker => ticker.StartWith(Fn("Import")))));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Seed(services));

        Assert.Contains("already seeded", error.Message);
    }

    private IServiceProvider Services(Action<TickerOptionsBuilder<TimeTickerEntity, CronTickerEntity>> configure)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddTickerQ(options =>
        {
            options.UseApplicationNamespace(_namespace);
            options.UseReconciliationEpoch(1);
            configure(options);
        });
        return services.BuildServiceProvider();
    }

    private async Task BeginEpoch(IServiceProvider services)
    {
        var provider = services.GetRequiredService<ITickerPersistenceProvider<TimeTickerEntity, CronTickerEntity>>();
        await provider.BeginReconciliationActivationEpochAsync(
            new ReconciliationActivationScope(_namespace), 1, CancellationToken.None);
    }

    private async Task Seed(IServiceProvider services)
    {
        await BeginEpoch(services);
        using var admission = StartupSeederAdmissionContext.Enter(new ReconciliationActivationScope(_namespace).ScopeKey, 1);
        await services.GetRequiredService<TickerExecutionContext>().OptionsSeeding
            .TimeTickerSeedAction!(services, CancellationToken.None);
    }

    private static async Task<TimeTickerEntity[]> Tickers(IServiceProvider services, string function)
        => await services.GetRequiredService<ITickerPersistenceProvider<TimeTickerEntity, CronTickerEntity>>()
            .GetTimeTickers(t => t.Function == function, CancellationToken.None);

    private static Task CreatePolicy(IServiceProvider services, string name)
        => services.GetRequiredService<ISchedulePolicyManager>().UpsertAsync(SchedulePolicy.Create(name).Build());

    private void Register(params string[] functions)
    {
        TickerFunctionProvider.RegisterFunctions(functions.ToDictionary(
            name => Fn(name),
            _ => (string.Empty, TickerTaskPriority.Normal, (TickerFunctionDelegate)NoOp, 1)));
        TickerFunctionProvider.Build();
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
}
