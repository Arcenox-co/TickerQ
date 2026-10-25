using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TickerQ.BackgroundServices;
using TickerQ.DependencyInjection;
using TickerQ.Tests.Licensing;
using TickerQ.Utilities;
using TickerQ.Utilities.Base;
using TickerQ.Utilities.Cluster;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Models;
using NSubstitute;
using Xunit;

namespace TickerQ.Tests;

/// <summary>
/// Without UseReconciliationEpoch, TickerQ derives the epoch from the startup manifest: the same inputs
/// join, new inputs claim the next epoch and reconcile, and inputs of an earlier deployment stay fenced
/// unless no node runs the current epoch. Each "host" here is a separate service provider sharing one
/// in-memory namespace; node heartbeats are written directly to simulate live or stopped deployments.
/// </summary>
[Collection("TickerFunctionProviderState")]
public sealed class AutomaticReconciliationEpochTests : IDisposable
{
    private readonly string _prefix = "auto" + Guid.NewGuid().ToString("N")[..8] + ".";
    private readonly string _namespace = "auto-epoch-" + Guid.NewGuid().ToString("N")[..8];

    public AutomaticReconciliationEpochTests() => ResetProvider();

    public void Dispose() => ResetProvider();

    private string Fn(string name) => _prefix + name;

    [Fact]
    public void Decision_follows_the_manifest()
    {
        var history = new ActivationEpochState
        {
            Epoch = 4, Phase = ActivationEpochPhase.Activated, Manifest = "m1:new", ManifestHistory = ["m1:old"]
        };

        Assert.Equal(new AutomaticEpochDecision(AutomaticEpochAction.Claim, 1),
            AutomaticEpochDecision.Decide(ActivationEpochState.PreEpoch, "m1:new"));
        Assert.Equal(new AutomaticEpochDecision(AutomaticEpochAction.Join, 4),
            AutomaticEpochDecision.Decide(history, "m1:new"));
        Assert.Equal(new AutomaticEpochDecision(AutomaticEpochAction.Resume, 4),
            AutomaticEpochDecision.Decide(new ActivationEpochState
            {
                Epoch = 4, Phase = ActivationEpochPhase.Activating, Manifest = "m1:new"
            }, "m1:new"));
        Assert.Equal(new AutomaticEpochDecision(AutomaticEpochAction.Fenced, 4),
            AutomaticEpochDecision.Decide(history, "m1:old"));
        Assert.Equal(new AutomaticEpochDecision(AutomaticEpochAction.Claim, 5),
            AutomaticEpochDecision.Decide(history, "m1:other"));
    }

    [Fact]
    public void History_moves_the_previous_manifest_to_the_front_and_stays_bounded()
    {
        Assert.Equal(["b", "a"], ActivationManifests.Advance("b", ["a"], "c"));
        Assert.Equal(["c", "b"], ActivationManifests.Advance("c", ["a", "b"], "a"));
        var full = Enumerable.Range(0, 40).Select(i => "h" + i).ToArray();
        Assert.Equal(ActivationManifests.MaxHistory, ActivationManifests.Advance("x", full, "y").Count);
    }

    [Fact]
    public async Task Manifest_ignores_declaration_order_and_tracks_real_changes()
    {
        Register("Report", "Export");
        var a = await Manifest(options => options.SeedCronTickers(crons => crons
            .Add(Fn("Report"), "0 0 9 * * *").Add(Fn("Export"), "0 0 1 * * *")));
        var reordered = await Manifest(options => options.SeedCronTickers(crons => crons
            .Add(Fn("Export"), "0 0 1 * * *").Add(Fn("Report"), "0 0 9 * * *")));
        var changed = await Manifest(options => options.SeedCronTickers(crons => crons
            .Add(Fn("Report"), "0 0 10 * * *").Add(Fn("Export"), "0 0 1 * * *")));
        var policy = await Manifest(options => options
            .SeedSchedulePolicies(policies => policies.Add(SchedulePolicy.Create("Nights").AllowBetween(
                new TimeOnly(22, 0), new TimeOnly(6, 0))))
            .SeedCronTickers(crons => crons.Add(Fn("Report"), "0 0 9 * * *").Add(Fn("Export"), "0 0 1 * * *")));

        Assert.StartsWith(StartupManifestBuilder.Prefix, a);
        Assert.Equal(a, reordered);
        Assert.NotEqual(a, changed);
        Assert.NotEqual(a, policy);

        Register("Report", "Export", "Added");
        Assert.NotEqual(a, await Manifest(options => options.SeedCronTickers(crons => crons
            .Add(Fn("Report"), "0 0 9 * * *").Add(Fn("Export"), "0 0 1 * * *"))));
    }

    [Fact]
    public async Task First_start_claims_epoch_one_and_a_restart_joins_it()
    {
        Register("Report");
        Action<TickerOptionsBuilder<TimeTickerEntity, CronTickerEntity>> seeds =
            options => options.SeedCronTickers(crons => crons.Add(Fn("Report"), "0 0 9 * * *"));

        var first = await StartHost(seeds);
        var state = await State(first);
        var restart = await StartHost(seeds);

        Assert.Equal(1, state.Epoch);
        Assert.Equal(ActivationEpochPhase.Activated, state.Phase);
        Assert.NotNull(state.Manifest);
        Assert.Equal(1, Epoch(first));
        Assert.Equal(1, Epoch(restart));
        Assert.Equal(1, (await State(restart)).Epoch);
    }

    [Fact]
    public async Task Changed_seeds_claim_the_next_epoch_and_are_applied()
    {
        Register("Report");
        var first = await StartHost(options => options.SeedCronTickers(crons => crons.Add(Fn("Report"), "0 0 9 * * *")));
        var firstManifest = (await State(first)).Manifest;

        var second = await StartHost(options => options.SeedCronTickers(crons => crons.Add(Fn("Report"), "0 0 10 * * *")));

        var state = await State(second);
        Assert.Equal(2, state.Epoch);
        Assert.Equal(ActivationEpochPhase.Activated, state.Phase);
        Assert.Contains(firstManifest, state.ManifestHistory);
        Assert.Equal(2, Epoch(second));
        var cron = Assert.Single(await Crons(second));
        Assert.Equal("0 0 10 * * *", cron.Expression);
    }

    [Fact]
    public async Task An_older_deployment_stays_fenced_while_the_current_one_is_alive()
    {
        Register("Report");
        Action<TickerOptionsBuilder<TimeTickerEntity, CronTickerEntity>> old =
            options => options.SeedCronTickers(crons => crons.Add(Fn("Report"), "0 0 9 * * *"));
        await StartHost(old);
        var current = await StartHost(options => options.SeedCronTickers(crons => crons.Add(Fn("Report"), "0 0 10 * * *")));
        await Heartbeat(current, "current-node", 2);

        var stale = await StartHost(old);

        Assert.Equal(0, Epoch(stale));
        var state = await State(stale);
        Assert.Equal(2, state.Epoch);
        Assert.Equal("0 0 10 * * *", Assert.Single(await Crons(stale)).Expression);
    }

    [Fact]
    public async Task An_older_deployment_takes_over_when_no_node_runs_the_current_epoch()
    {
        Register("Report");
        Action<TickerOptionsBuilder<TimeTickerEntity, CronTickerEntity>> old =
            options => options.SeedCronTickers(crons => crons.Add(Fn("Report"), "0 0 9 * * *"));
        var first = await StartHost(old);
        var oldManifest = (await State(first)).Manifest;
        var current = await StartHost(options => options.SeedCronTickers(crons => crons.Add(Fn("Report"), "0 0 10 * * *")));
        await Heartbeat(current, "current-node", 0); // stopped gracefully

        var rollback = await StartHost(old);

        var state = await State(rollback);
        Assert.Equal(3, state.Epoch);
        Assert.Equal(oldManifest, state.Manifest);
        Assert.Equal(3, Epoch(rollback));
        Assert.Equal("0 0 9 * * *", Assert.Single(await Crons(rollback)).Expression);
    }

    [Fact]
    public async Task A_fenced_host_takes_over_through_the_monitor_once_the_current_deployment_stops()
    {
        Register("Report");
        Action<TickerOptionsBuilder<TimeTickerEntity, CronTickerEntity>> old =
            options => options.SeedCronTickers(crons => crons.Add(Fn("Report"), "0 0 9 * * *"));
        await StartHost(old);
        var current = await StartHost(options => options.SeedCronTickers(crons => crons.Add(Fn("Report"), "0 0 10 * * *")));
        await Heartbeat(current, "current-node", 2);
        var stale = await StartHost(old);
        Assert.Equal(0, Epoch(stale));

        await Heartbeat(current, "current-node", 0);
        await Monitor(stale).TickAsync(CancellationToken.None);

        Assert.Equal(3, Epoch(stale));
        Assert.Equal(3, (await State(stale)).Epoch);
        Assert.Equal("0 0 9 * * *", Assert.Single(await Crons(stale)).Expression);
    }

    [Fact]
    public async Task The_monitor_follows_a_newer_epoch_claimed_with_the_same_manifest()
    {
        Register("Report");
        var host = await StartHost(_ => { });
        var state = await State(host);
        var provider = host.GetRequiredService<ITickerPersistenceProvider<TimeTickerEntity, CronTickerEntity>>();
        var scope = new ReconciliationActivationScope(_namespace);
        await provider.BeginReconciliationActivationEpochAsync(scope, 2, state.Manifest, CancellationToken.None);
        await provider.CommitReconciliationActivationEpochAsync(scope, 2, CancellationToken.None);

        await Monitor(host).TickAsync(CancellationToken.None);

        Assert.Equal(2, Epoch(host));
    }

    [Fact]
    public async Task Due_seed_retirement_claims_a_maintenance_epoch_that_retires_the_cron()
    {
        Register("Report", "Export");
        await StartHost(options => options
            .ConfigureScheduler(s => s.DefinedCronRetirementGracePeriod = TimeSpan.FromHours(1))
            .SeedCronTickers(crons => crons.Add(Fn("Report"), "0 0 9 * * *").Add(Fn("Export"), "0 0 1 * * *")));
        var host = await StartHost(options => options
            .ConfigureScheduler(s => s.DefinedCronRetirementGracePeriod = TimeSpan.FromHours(1))
            .SeedCronTickers(crons => crons.Add(Fn("Report"), "0 0 9 * * *")));
        var removed = Assert.Single(await Crons(host), c => c.Function == Fn("Export"));
        Assert.NotNull(removed.RetirementRequestedAt);
        Assert.Null(removed.RetiredAt);
        removed.RetirementRequestedAt = DateTime.UtcNow.AddHours(-2);
        await host.GetRequiredService<ITickerPersistenceProvider<TimeTickerEntity, CronTickerEntity>>()
            .UpdateCronTickers([removed], CancellationToken.None);

        await Monitor(host).TickAsync(CancellationToken.None);

        Assert.Equal(3, Epoch(host));
        Assert.NotNull(Assert.Single(await Crons(host), c => c.Function == Fn("Export")).RetiredAt);
    }

    [Fact]
    public async Task An_invalid_seed_fails_startup_before_claiming_so_the_running_deployment_keeps_its_epoch()
    {
        Register("Report");
        await StartHost(options => options.SeedCronTickers(crons => crons.Add(Fn("Report"), "0 0 9 * * *")));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => StartHost(options =>
            options.SeedCronTickers(crons => crons.Add(Fn("Missing"), "0 0 9 * * *"))));

        Assert.Contains("is invalid", error.Message);
        var state = await State(BuildHost(_ => { }));
        Assert.Equal(1, state.Epoch);
        Assert.Equal(ActivationEpochPhase.Activated, state.Phase);
    }

    [Fact]
    public async Task A_seed_using_a_missing_policy_fails_before_claiming()
    {
        Register("Report");
        await StartHost(options => options.SeedCronTickers(crons => crons.Add(Fn("Report"), "0 0 9 * * *")));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => StartHost(options =>
            options.SeedCronTickers(crons => crons.Add(cron => cron
                .WithExpression("0 0 9 * * *").UsingPolicy("Deleted policy").StartWith(Fn("Report"))))));

        Assert.Contains("Schedule policy 'Deleted policy' does not exist", error.Message);
        var state = await State(BuildHost(_ => { }));
        Assert.Equal((1L, ActivationEpochPhase.Activated), (state.Epoch, state.Phase));

        // Declaring the policy in the same deployment is enough.
        var fixedHost = await StartHost(options => options
            .SeedSchedulePolicies(policies => policies.Add(SchedulePolicy.Create("Deleted policy")))
            .SeedCronTickers(crons => crons.Add(cron => cron
                .WithExpression("0 0 9 * * *").UsingPolicy("Deleted policy").StartWith(Fn("Report")))));
        Assert.Equal(2, Epoch(fixedHost));
    }

    [Fact]
    public async Task A_peer_committing_the_same_epoch_completes_activation_and_still_runs_local_startup()
    {
        var manager = Substitute.For<TickerQ.Utilities.Interfaces.Managers.IInternalTickerManager>();
        manager.AdvanceReconciliationCheckpointAsync(Arg.Any<ReconciliationActivationScope>(), 1, Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(new ActivationEpochState { Epoch = 1, Phase = ActivationEpochPhase.Activated });
        var externalActions = 0;
        var context = new TickerExecutionContext { ExternalProviderApplicationAction = _ => externalActions++ };
        var activator = new TickerQReconciliationActivator(new ServiceCollection().BuildServiceProvider(), context);

        await activator.RunAsync(manager, new ReconciliationActivationScope(_namespace), 1, CancellationToken.None);

        Assert.Equal(1, externalActions);
        await manager.DidNotReceive().CommitReconciliationActivationEpochAsync(
            Arg.Any<ReconciliationActivationScope>(), Arg.Any<long>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_newer_claim_during_activation_is_reported_as_superseded()
    {
        var manager = Substitute.For<TickerQ.Utilities.Interfaces.Managers.IInternalTickerManager>();
        manager.AdvanceReconciliationCheckpointAsync(Arg.Any<ReconciliationActivationScope>(), 1, Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(new ActivationEpochState { Epoch = 2, Phase = ActivationEpochPhase.Activating });
        var activator = new TickerQReconciliationActivator(
            new ServiceCollection().BuildServiceProvider(), new TickerExecutionContext());

        await Assert.ThrowsAsync<TickerQReconciliationActivator.ActivationSupersededException>(() =>
            activator.RunAsync(manager, new ReconciliationActivationScope(_namespace), 1, CancellationToken.None));
    }

    [Fact]
    public async Task An_explicit_epoch_keeps_manual_control()
    {
        Register("Report");
        await StartHost(options => options.UseReconciliationEpoch(5)
            .SeedCronTickers(crons => crons.Add(Fn("Report"), "0 0 9 * * *")));
        var host = await StartHost(options => options.UseReconciliationEpoch(5)
            .SeedCronTickers(crons => crons.Add(Fn("Report"), "0 0 10 * * *")));

        Assert.Equal(5, (await State(host)).Epoch);
        Assert.Equal("0 0 9 * * *", Assert.Single(await Crons(host)).Expression);
        Assert.Null(host.GetServices<IHostedService>().OfType<TickerQActivationMonitorBackgroundService>().FirstOrDefault());
    }

    private IServiceProvider BuildHost(Action<TickerOptionsBuilder<TimeTickerEntity, CronTickerEntity>> configure)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        var environment = NSubstitute.Substitute.For<IHostEnvironment>();
        environment.EnvironmentName.Returns(Environments.Production);
        environment.ContentRootPath.Returns(AppContext.BaseDirectory);
        services.AddSingleton(environment);
        services.AddTickerQ(options =>
        {
            options.UseApplicationNamespace(_namespace);
            configure(options);
        });
        return services.BuildServiceProvider();
    }

    private async Task<IServiceProvider> StartHost(Action<TickerOptionsBuilder<TimeTickerEntity, CronTickerEntity>> configure)
    {
        var host = BuildHost(configure);
        var initializer = new TickerQInitializerHostedService(
            host.GetRequiredService<TickerExecutionContext>(), host,
            host.GetRequiredService<IConfiguration>(), LicenseTestState.Active())
        {
            InitializationRequested = true
        };
        await initializer.StartAsync(CancellationToken.None);
        return host;
    }

    private async Task<string> Manifest(Action<TickerOptionsBuilder<TimeTickerEntity, CronTickerEntity>> configure)
    {
        var host = BuildHost(configure);
        return await host.GetRequiredService<TickerExecutionContext>().OptionsSeeding
            .StartupManifestFactory(host, CancellationToken.None);
    }

    private Task<ActivationEpochState> State(IServiceProvider host)
        => host.GetRequiredService<ITickerPersistenceProvider<TimeTickerEntity, CronTickerEntity>>()
            .GetReconciliationActivationStateAsync(new ReconciliationActivationScope(_namespace), CancellationToken.None);

    private static long Epoch(IServiceProvider host) => host.GetRequiredService<SchedulerOptionsBuilder>().RuntimeActivationEpoch;

    private static Task<CronTickerEntity[]> Crons(IServiceProvider host)
        => host.GetRequiredService<ITickerPersistenceProvider<TimeTickerEntity, CronTickerEntity>>()
            .GetCronTickers(_ => true, CancellationToken.None);

    private static Task Heartbeat(IServiceProvider host, string nodeKey, long epoch)
        => host.GetRequiredService<ITickerPersistenceProvider<TimeTickerEntity, CronTickerEntity>>().UpsertNodeAsync(
            new TickerQNodeRecord
            {
                NodeKey = nodeKey, ExecutionOwnerId = nodeKey, NodeIdentifier = nodeKey, DisplayName = nodeKey,
                LastSeenAtUtc = DateTime.UtcNow, StartedAtUtc = DateTime.UtcNow, IsRunning = epoch > 0,
                ActivationEpoch = epoch
            });

    private static TickerQActivationMonitorBackgroundService Monitor(IServiceProvider host)
        => host.GetServices<IHostedService>().OfType<TickerQActivationMonitorBackgroundService>().Single();

    private void Register(params string[] functions)
    {
        ResetProvider();
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

        type.GetProperty("TickerFunctions", flags)!.SetValue(null,
            System.Collections.Frozen.FrozenDictionary<string, (string, TickerTaskPriority, TickerFunctionDelegate, int)>.Empty);
        type.GetProperty("TickerFunctionRequestTypes", flags)!.SetValue(null,
            System.Collections.Frozen.FrozenDictionary<string, (string, Type)>.Empty);
        type.GetProperty("TickerFunctionRequestInfos", flags)!.SetValue(null,
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
