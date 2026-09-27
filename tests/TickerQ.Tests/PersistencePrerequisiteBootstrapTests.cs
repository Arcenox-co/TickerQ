using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using TickerQ.BackgroundServices;
using TickerQ.Utilities;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Interfaces.Managers;
using TickerQ.Utilities.Models;

namespace TickerQ.Tests;

[Collection("TickerFunctionProviderState")]
public sealed class PersistencePrerequisiteBootstrapTests
{
    [Fact]
    public async Task Prerequisite_schema_bootstrap_runs_before_durable_begin_while_local_gate_is_closed()
    {
        var order = new List<string>();
        var gate = new TickerQActivationGate();
        var manager = Substitute.For<IInternalTickerManager>();
        ActivationEpochTestDouble.Configure(manager);
        manager.SupportsReconciliationActivationEpoch.Returns(true);
        manager.BeginReconciliationActivationEpochAsync(
                Arg.Any<ReconciliationActivationScope>(), 1, Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                Assert.Equal(new[] { "prerequisite" }, order);
                Assert.False(gate.IsActivated);
                order.Add("begin");
                return new ActivationEpochState { Epoch = 1, Phase = ActivationEpochPhase.Activating };
            });
        manager.RepairTimeTickerChainsAsync(Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                Assert.Contains("begin", order);
                order.Add("repair");
                return TimeTickerChainRepairResult.Empty;
            });
        manager.CommitReconciliationActivationEpochAsync(
                Arg.Any<ReconciliationActivationScope>(), 1, Arg.Any<CancellationToken>())
            .Returns(new ActivationEpochState { Epoch = 1, Phase = ActivationEpochPhase.Activated });

        var prerequisite = Substitute.For<ITickerQPersistencePrerequisiteBootstrapper>();
        prerequisite.BootstrapAsync(Arg.Any<CancellationToken>()).Returns(_ =>
        {
            Assert.False(gate.IsActivated);
            Assert.DoesNotContain("begin", order);
            order.Add("prerequisite");
            return Task.CompletedTask;
        });

        var context = new TickerExecutionContext
        {
            OptionsSeeding = new SchedulerOptionsSeeding()
        };
        var configuration = Substitute.For<IConfiguration>();
        var services = new ServiceCollection();
        services.AddSingleton(context);
        services.AddSingleton(configuration);
        services.AddSingleton(manager);
        services.AddSingleton<ITickerQActivationGate>(gate);
        services.AddSingleton(new SchedulerOptionsBuilder());
        services.AddSingleton(prerequisite);
        var initializer = new TickerQInitializerHostedService(context, services.BuildServiceProvider(), configuration)
        {
            InitializationRequested = true
        };

        await initializer.StartAsync(CancellationToken.None);

        Assert.Equal(new[] { "prerequisite", "begin", "repair" }, order);
        Assert.True(gate.IsActivated);
    }

    [Fact]
    public async Task Explicit_legacy_adoption_completes_after_schema_prerequisite_and_before_activation_begin()
    {
        var order = new List<string>();
        var gate = new TickerQActivationGate();
        var manager = Substitute.For<IInternalTickerManager>();
        ActivationEpochTestDouble.Configure(manager, 9);
        manager.SupportsReconciliationActivationEpoch.Returns(true);
        manager.SupportsLegacyRuntimePartitionAdoption.Returns(true);
        manager.AdoptLegacyRuntimePartitionAsync(
                Arg.Any<LegacyRuntimePartitionAdoption>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                Assert.Equal(new[] { "prerequisite" }, order);
                order.Add("adoption");
                return Task.CompletedTask;
            });
        manager.BeginReconciliationActivationEpochAsync(
                Arg.Any<ReconciliationActivationScope>(), 9, Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                Assert.Equal(new[] { "prerequisite", "adoption" }, order);
                order.Add("begin");
                return new ActivationEpochState { Epoch = 9, Phase = ActivationEpochPhase.Activating };
            });
        manager.RepairTimeTickerChainsAsync(Arg.Any<CancellationToken>())
            .Returns(TimeTickerChainRepairResult.Empty);
        manager.CommitReconciliationActivationEpochAsync(
                Arg.Any<ReconciliationActivationScope>(), 9, Arg.Any<CancellationToken>())
            .Returns(new ActivationEpochState { Epoch = 9, Phase = ActivationEpochPhase.Activated });
        var prerequisite = Substitute.For<ITickerQPersistencePrerequisiteBootstrapper>();
        prerequisite.BootstrapAsync(Arg.Any<CancellationToken>()).Returns(_ =>
        {
            order.Add("prerequisite");
            return Task.CompletedTask;
        });
        var adoption = new LegacyRuntimePartitionAdoption(
            new TickerQRuntimePartition("adoption-tests"), 9);
        var context = new TickerExecutionContext
        {
            OptionsSeeding = new AdoptionOptionsSeeding(adoption)
        };
        var services = new ServiceCollection();
        services.AddSingleton(context);
        services.AddSingleton(Substitute.For<IConfiguration>());
        services.AddSingleton(manager);
        services.AddSingleton<ITickerQActivationGate>(gate);
        services.AddSingleton(new SchedulerOptionsBuilder());
        services.AddSingleton(prerequisite);
        var initializer = new TickerQInitializerHostedService(
            context, services.BuildServiceProvider(), Substitute.For<IConfiguration>())
        { InitializationRequested = true };

        await initializer.StartAsync(CancellationToken.None);

        Assert.Equal(new[] { "prerequisite", "adoption", "begin" }, order);
    }

    private sealed class SchedulerOptionsSeeding : ITickerOptionsSeeding
    {
        public bool SeedDefinedCronTickers => true;
        public long ReconciliationEpoch => 1;
        public string DefinedCronApplicationNamespace => "prerequisite-tests";
        public Func<IServiceProvider, CancellationToken, Task> TimeSeederAction => null;
        public Func<IServiceProvider, CancellationToken, Task> CronSeederAction => null;
    }

    private sealed class AdoptionOptionsSeeding(LegacyRuntimePartitionAdoption adoption) : ITickerOptionsSeeding
    {
        public bool SeedDefinedCronTickers => true;
        public long ReconciliationEpoch => adoption.Epoch;
        public string DefinedCronApplicationNamespace => adoption.TargetPartition.ApplicationNamespace;
        public LegacyRuntimePartitionAdoption LegacyRuntimePartitionAdoption => adoption;
        public Func<IServiceProvider, CancellationToken, Task> TimeSeederAction => null;
        public Func<IServiceProvider, CancellationToken, Task> CronSeederAction => null;
    }
}
