using System;
using System.Collections.Generic;
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
/// Cron tickers are seeded only from options.SeedCronTickers(...), using the recurring-chain builder and
/// the cron manager's validation. The function attribute and WithCron(...) no longer declare crons.
/// </summary>
[Collection("TickerFunctionProviderState")]
public sealed class CronTickerSeedingTests : IDisposable
{
    private readonly string _prefix = "seed" + Guid.NewGuid().ToString("N")[..8] + ".";
    private readonly string _namespace = "seed-tests-" + Guid.NewGuid().ToString("N")[..8];

    public CronTickerSeedingTests() => ResetProvider();

    public void Dispose() => ResetProvider();

    private string Fn(string name) => _prefix + name;

    [Fact]
    public void Attribute_cron_constructor_is_a_compile_error()
    {
        var cronConstructor = typeof(TickerFunctionAttribute).GetConstructor(
            [typeof(string), typeof(string), typeof(TickerTaskPriority), typeof(int)]);
        var obsolete = cronConstructor?.GetCustomAttribute<ObsoleteAttribute>();

        Assert.NotNull(obsolete);
        Assert.True(obsolete!.IsError);
        Assert.Contains("SeedCronTickers", obsolete.Message);
        Assert.Null(typeof(TickerFunctionAttribute)
            .GetConstructor([typeof(string), typeof(TickerTaskPriority), typeof(int)])!
            .GetCustomAttribute<ObsoleteAttribute>());
    }

    [Fact]
    public void Registration_builder_WithCron_is_a_compile_error()
    {
        var obsolete = typeof(TickerFunctionBuilder).GetMethod(nameof(TickerFunctionBuilder.WithCron))
            ?.GetCustomAttribute<ObsoleteAttribute>();

        Assert.NotNull(obsolete);
        Assert.True(obsolete!.IsError);
    }

    [Fact]
    public async Task Nothing_is_seeded_unless_declared()
    {
        Register(("RegistryCron", "* * * * *"));

        var seeds = await BuildSeeds(_ => { });

        Assert.Empty(seeds);
    }

    [Fact]
    public async Task Shorthand_and_chain_builder_seeds_carry_the_full_definition()
    {
        Register(("Heartbeat", null), ("Report", null), ("Email", null), ("Archive", null), ("Done", null));
        var services = Services(options => options.SeedCronTickers(crons => crons
            .Add(Fn("Heartbeat"), "* * * * *")
            .Add(cron => cron
                .WithExpression("0 0 9 * * *")
                .InTimeZone("Europe/Berlin")
                .UsingPolicy("business HOURS")
                .SkipIfAlreadyRunning()
                .WithDescription("Daily report")
                .Enabled(false)
                .StartWith(Fn("Report"), step => { step.Retries = 2; step.RetryIntervals = [300]; })
                .ThenMany(branches =>
                {
                    branches.Add(Fn("Email"));
                    branches.Add(Fn("Archive"));
                })
                .ThenAfterAll(Fn("Done")))
            .Add("evening", cron => cron.WithExpression("0 0 18 * * *").StartWith(Fn("Report")))));
        await CreatePolicy(services, "Business hours");

        var seeds = await BuildSeeds(services);

        Assert.Equal([Fn("Heartbeat"), Fn("Report"), "evening"], seeds.Select(seed => seed.StableDefinitionId));
        var heartbeat = seeds[0];
        Assert.Null(heartbeat.Definition!.Chain);
        Assert.Null(heartbeat.Definition.SchedulePolicyName);

        var report = seeds[1];
        Assert.Equal(2, report.Retries);
        Assert.Equal([300], report.RetryIntervals);
        Assert.Equal("Europe/Berlin", report.Definition!.TimeZoneId);
        Assert.Equal("Business hours", report.Definition.SchedulePolicyName);
        Assert.NotNull(report.Definition.SchedulePolicyVersion);
        Assert.Equal(BlockedOccurrenceBehavior.Skip, report.Definition.BlockedOccurrenceBehavior);
        Assert.True(report.Definition.SkipIfAlreadyRunning);
        Assert.Equal("Daily report", report.Definition.Description);
        Assert.False(report.Definition.IsEnabled);
        var group = Assert.Single(report.Definition.Chain!.Children);
        Assert.Equal(2, group.Children.Count(child => child.Function != Fn("Done")));
    }

    [Fact]
    public async Task A_policy_that_does_not_exist_fails_startup()
    {
        Register(("Report", null));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => BuildSeeds(options =>
            options.SeedCronTickers(crons => crons.Add(cron => cron
                .WithExpression("0 0 9 * * *").UsingPolicy("Missing policy").StartWith(Fn("Report"))))));

        Assert.Contains($"Seeded cron ticker '{Fn("Report")}' is invalid", error.Message);
        Assert.Contains("Schedule policy 'Missing policy' does not exist", error.Message);
    }

    [Fact]
    public async Task A_policy_that_never_allows_the_expression_fails_startup()
    {
        Register(("Report", null));
        var services = Services(options => options.SeedCronTickers(crons => crons.Add(cron => cron
            .WithExpression("0 0 12 * * *").UsingPolicy("Mornings").StartWith(Fn("Report")))));
        await services.GetRequiredService<ISchedulePolicyManager>().UpsertAsync(
            SchedulePolicy.Create("Mornings").AllowBetween(new TimeOnly(8, 0), new TimeOnly(10, 0)).Build());

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => BuildSeeds(services));

        Assert.Contains("has no allowed future occurrence", error.Message);
    }

    [Theory]
    [InlineData("Missing", "* * * * *", "is invalid")]
    [InlineData("Report", "%Reports:Missing%", "has no value")]
    [InlineData("Report", "not a cron", "invalid cron expression")]
    public async Task Bad_seeds_fail_startup_clearly(string function, string expression, string message)
    {
        Register(("Report", null));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => BuildSeeds(options =>
            options.SeedCronTickers(crons => crons.Add(Fn(function), expression))));

        Assert.Contains(message, error.Message);
    }

    [Fact]
    public async Task Duplicate_keys_fail_startup()
    {
        Register(("Report", null));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => BuildSeeds(options =>
            options.SeedCronTickers(crons => crons
                .Add(Fn("Report"), "* * * * *")
                .Add(cron => cron.WithExpression("0 0 9 * * *").StartWith(Fn("Report"))))));

        Assert.Contains("already seeded", error.Message);
    }

    [Fact]
    public async Task Configuration_placeholders_and_service_overload_are_resolved()
    {
        Register(("Report", null), ("Export", null));
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Reports:Cron"] = "*/10 * * * *",
                ["Export:Cron"] = "0 0 1 * * *",
            })
            .Build();

        var seeds = await BuildSeeds(Services(options => options
            .SeedCronTickers(crons => crons.Add(Fn("Report"), "%Reports:Cron%"))
            .SeedCronTickers((crons, services) => crons.Add(
                Fn("Export"), services.GetRequiredService<IConfiguration>()["Export:Cron"]!)), configuration));

        Assert.Equal(["*/10 * * * *", "0 0 1 * * *"], seeds.Select(seed => seed.Expression));
    }

    [Fact]
    public async Task Reconciling_the_same_seeds_again_creates_no_duplicate_and_updates_in_place()
    {
        Register(("Report", null), ("Email", null));
        var services = Services(options => options.SeedCronTickers(crons => crons.Add(cron => cron
            .WithExpression("0 0 9 * * *")
            .InTimeZone("Europe/Berlin")
            .Enabled(false)
            .StartWith(Fn("Report"), step => step.Retries = 1)
            .Then(Fn("Email")))));
        var provider = services.GetRequiredService<ITickerPersistenceProvider<TimeTickerEntity, CronTickerEntity>>();
        var scope = new ReconciliationActivationScope(_namespace);
        await provider.BeginReconciliationActivationEpochAsync(scope, 1, CancellationToken.None);
        using var admission = StartupSeederAdmissionContext.Enter(scope.ScopeKey, 1);
        var seeds = await BuildSeeds(services);

        await provider.MigrateDefinedCronTickers(new DefinedCronSeedManifest(_namespace, seeds.ToList()));
        var created = Assert.Single(await provider.GetCronTickers(c => c.Function == Fn("Report"), CancellationToken.None));
        Assert.Equal("Europe/Berlin", created.TimeZoneId);
        Assert.Equal(1, created.Retries);
        Assert.False(created.IsEnabled);
        Assert.Equal(Fn("Email"), Assert.Single(created.Chain!.Children).Function);

        // An operator enables it in the dashboard; reconciling the same list again changes nothing.
        created.IsEnabled = true;
        await provider.UpdateCronTickers([created], CancellationToken.None);
        await provider.MigrateDefinedCronTickers(new DefinedCronSeedManifest(_namespace, seeds.ToList()));
        var again = Assert.Single(await provider.GetCronTickers(c => c.Function == Fn("Report"), CancellationToken.None));
        Assert.Equal(created.Id, again.Id);
        Assert.True(again.IsEnabled);
        var revision = again.DefinitionRevision;

        // A changed definition updates the same row and bumps its revision.
        var changed = await BuildSeeds(Services(options => options.SeedCronTickers(crons => crons.Add(cron => cron
            .WithExpression("0 0 9 * * *")
            .InTimeZone("Europe/Tirane")
            .StartWith(Fn("Report"), step => step.Retries = 3)
            .Then(Fn("Email"))))));
        await provider.MigrateDefinedCronTickers(new DefinedCronSeedManifest(_namespace, changed.ToList()));
        var updated = Assert.Single(await provider.GetCronTickers(c => c.Function == Fn("Report"), CancellationToken.None));
        Assert.Equal(created.Id, updated.Id);
        Assert.Equal("Europe/Tirane", updated.TimeZoneId);
        Assert.Equal(3, updated.Retries);
        Assert.True(updated.IsEnabled);
        Assert.True(updated.DefinitionRevision > revision);
    }

    [Fact]
    public void Dashboard_marks_seeded_crons()
    {
        Assert.True(Utilities.Infrastructure.TickerDashboardDataService<TimeTickerEntity, CronTickerEntity>.IsSeededCron(
            new CronTickerEntity { SeedKey = "tq:cron-seed:v2:abc" }));
        Assert.False(Utilities.Infrastructure.TickerDashboardDataService<TimeTickerEntity, CronTickerEntity>.IsSeededCron(
            new CronTickerEntity()));
    }

    private IServiceProvider Services(Action<TickerOptionsBuilder<TimeTickerEntity, CronTickerEntity>> configure,
        IConfiguration? configuration = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(configuration ?? new ConfigurationBuilder().Build());
        services.AddTickerQ(options =>
        {
            options.UseApplicationNamespace(_namespace);
            options.UseReconciliationEpoch(1);
            configure(options);
        });
        return services.BuildServiceProvider();
    }

    private Task<IReadOnlyList<DefinedCronTickerSeed>> BuildSeeds(
        Action<TickerOptionsBuilder<TimeTickerEntity, CronTickerEntity>> configure)
        => BuildSeeds(Services(configure));

    private static async Task<IReadOnlyList<DefinedCronTickerSeed>> BuildSeeds(IServiceProvider services)
    {
        var factory = services.GetRequiredService<TickerExecutionContext>().OptionsSeeding.CronTickerSeedFactory;
        return await factory(services, CancellationToken.None);
    }

    private static Task CreatePolicy(IServiceProvider services, string name)
        => services.GetRequiredService<ISchedulePolicyManager>().UpsertAsync(SchedulePolicy.Create(name).Build());

    private void Register(params (string Name, string? Cron)[] functions)
    {
        TickerFunctionProvider.RegisterFunctions(functions.ToDictionary(
            f => Fn(f.Name),
            f => (f.Cron ?? string.Empty, TickerTaskPriority.Normal, (TickerFunctionDelegate)NoOp, 1)));
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
