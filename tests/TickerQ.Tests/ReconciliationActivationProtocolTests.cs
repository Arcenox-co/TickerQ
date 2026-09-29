using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using TickerQ.BackgroundServices;
using TickerQ.DependencyInjection;
using TickerQ.Utilities;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Interfaces.Managers;
using TickerQ.Utilities.Models;
using Xunit;

namespace TickerQ.Tests;

/// <summary>
/// First vertical slice of the reconciliation activation protocol wiring:
/// (1) the scheduler, fallback, and stale-recovery loops touch no persistence until the shared
///     <see cref="ITickerQActivationGate"/> opens;
/// (2) the initializer opens the gate only after bootstrap, chain repair, defined-Cron
///     reconciliation, and finalizers all succeed;
/// (3) any exception or cancellation during startup leaves the gate closed (fail-closed); and
/// (4) a legacy provider that does not advertise <c>SupportsReconciliationActivationEpoch</c> fails
///     startup before any repair/reconciliation runs.
/// </summary>
[Collection("TickerCancellationTokenState")]
public sealed class ReconciliationActivationProtocolTests : IDisposable
{
    public void Dispose() => TickerCancellationTokenManager.CleanUpTickerCancellationTokens();

    private static Task RunExecuteAsync(object service, CancellationToken ct)
    {
        var method = service.GetType()
            .GetMethod("ExecuteAsync", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        var task = (Task)method!.Invoke(service, new object[] { ct })!;
        return task;
    }

    private static async Task<bool> WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
                return true;
            await Task.Delay(15);
        }
        return condition();
    }

    private static async Task DrainAsync(Task task)
    {
        try
        {
            await task.WaitAsync(TimeSpan.FromSeconds(2));
        }
        catch (OperationCanceledException)
        {
            // Expected when the loop unwinds through a cancelled activation wait or poll delay.
        }
        catch (TimeoutException)
        {
            // The loop parked (e.g. sleeping a day after a poll); the test is finished with it.
        }
    }

    // ----- Requirement 1: loops perform no persistence before the gate opens -----

    [Fact]
    public async Task Scheduler_TouchesNoPersistence_UntilGateOpens()
    {
        var gate = new TickerQActivationGate();
        var manager = Substitute.For<IInternalTickerManager>();
        manager.GetNextTickers(Arg.Any<CancellationToken>())
            .Returns((TimeSpan.FromDays(1), Array.Empty<InternalFunctionContext>()));

        var service = new TickerQSchedulerBackgroundService(
            new TickerExecutionContext(),
            Substitute.For<ITickerExecutionTaskHandler>(),
            Substitute.For<ITickerQTaskScheduler>(),
            manager,
            new SchedulerOptionsBuilder(),
            new TickerFunctionConcurrencyGate(),
            LicenseTestState.Active(),
            activationGate: gate);

        using var cts = new CancellationTokenSource();
        var run = RunExecuteAsync(service, cts.Token);

        await Task.Delay(150);
        await manager.DidNotReceive().GetNextTickers(Arg.Any<CancellationToken>());
        Assert.False(run.IsCompleted);

        gate.SignalActivated();

        Assert.True(await WaitUntilAsync(
            () => manager.ReceivedCalls().Any(c => c.GetMethodInfo().Name == nameof(IInternalTickerManager.GetNextTickers)),
            TimeSpan.FromSeconds(2)));

        cts.Cancel();
        await DrainAsync(run);
    }

    [Fact]
    public async Task Fallback_TouchesNoPersistence_UntilGateOpens()
    {
        var gate = new TickerQActivationGate();
        var manager = Substitute.For<IInternalTickerManager>();
        manager.RunTimedOutTickers(Arg.Any<CancellationToken>())
            .Returns(Array.Empty<InternalFunctionContext>());
        var taskScheduler = Substitute.For<ITickerQTaskScheduler>();
        taskScheduler.IsFrozen.Returns(false);
        taskScheduler.IsDisposed.Returns(false);

        var service = new TickerQFallbackBackgroundService(
            manager,
            new SchedulerOptionsBuilder { FallbackIntervalChecker = TimeSpan.FromMilliseconds(30) },
            Substitute.For<ITickerExecutionTaskHandler>(),
            taskScheduler,
            new TickerFunctionConcurrencyGate(),
            LicenseTestState.Active(),
            activationGate: gate);

        using var cts = new CancellationTokenSource();
        var run = RunExecuteAsync(service, cts.Token);

        await Task.Delay(150);
        await manager.DidNotReceive().RunTimedOutTickers(Arg.Any<CancellationToken>());
        Assert.False(run.IsCompleted);

        gate.SignalActivated();

        Assert.True(await WaitUntilAsync(
            () => manager.ReceivedCalls().Any(c => c.GetMethodInfo().Name == nameof(IInternalTickerManager.RunTimedOutTickers)),
            TimeSpan.FromSeconds(2)));

        cts.Cancel();
        await DrainAsync(run);
    }

    [Fact]
    public async Task StaleRecovery_TouchesNoPersistence_UntilGateOpens()
    {
        var gate = new TickerQActivationGate();
        var manager = Substitute.For<IInternalTickerManager>();
        manager.SupportsLeaseBasedRecovery.Returns(true);
        manager.RecoverStaleTickersAsync(Arg.Any<CancellationToken>())
            .Returns(new StaleTickerRecoveryResult());

        var service = new TickerQStaleJobRecoveryBackgroundService(
            manager,
            new SchedulerOptionsBuilder { LeaseRenewalInterval = TimeSpan.FromMilliseconds(20) },
            NullLogger<TickerQStaleJobRecoveryBackgroundService>.Instance,
            Substitute.For<ITickerQFailureNotifier>(),
            LicenseTestState.Active(),
            activationGate: gate);

        using var cts = new CancellationTokenSource();
        var run = RunExecuteAsync(service, cts.Token);

        await Task.Delay(150);
        await manager.DidNotReceive().RecoverStaleTickersAsync(Arg.Any<CancellationToken>());
        Assert.False(run.IsCompleted);

        gate.SignalActivated();

        Assert.True(await WaitUntilAsync(
            () => manager.ReceivedCalls().Any(c => c.GetMethodInfo().Name == nameof(IInternalTickerManager.RecoverStaleTickersAsync)),
            TimeSpan.FromSeconds(2)));

        cts.Cancel();
        await DrainAsync(run);
    }

    // ----- Requirement 2: initializer opens the gate only after all startup steps succeed -----

    [Fact]
    public async Task Initializer_OpensGate_OnlyAfterBootstrapRepairReconcileAndFinalizersSucceed()
    {
        var gate = new TickerQActivationGate();
        var order = new System.Collections.Generic.List<string>();

        var manager = Substitute.For<IInternalTickerManager>();
        manager.SupportsReconciliationActivationEpoch.Returns(true);
        manager.BeginReconciliationActivationEpochAsync(Arg.Any<ReconciliationActivationScope>(), 1, Arg.Any<CancellationToken>())
            .Returns(new ActivationEpochState { Epoch = 1, Phase = ActivationEpochPhase.Activating });
        manager.AdvanceReconciliationCheckpointAsync(Arg.Any<ReconciliationActivationScope>(), 1, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ci => new ActivationEpochState
            {
                Epoch = 1, Phase = ActivationEpochPhase.Activating, Checkpoint = ci.ArgAt<string>(2)
            });
        manager.CommitReconciliationActivationEpochAsync(Arg.Any<ReconciliationActivationScope>(), 1, Arg.Any<CancellationToken>())
            .Returns(new ActivationEpochState { Epoch = 1, Phase = ActivationEpochPhase.Activated });
        manager.RepairTimeTickerChainsAsync(Arg.Any<CancellationToken>())
            .Returns(_ => { order.Add("repair"); return Task.FromResult(TimeTickerChainRepairResult.Empty); });
        manager.MigrateDefinedCronTickers(Arg.Any<DefinedCronSeedManifest>(), Arg.Any<CancellationToken>())
            .Returns(_ => { order.Add("reconcile"); return Task.CompletedTask; });

        var bootstrapper = Substitute.For<ITickerQPersistenceBootstrapper>();
        bootstrapper.BootstrapAsync(Arg.Any<CancellationToken>())
            .Returns(_ => { order.Add("bootstrap"); return Task.CompletedTask; });

        var gateOpenDuringFinalize = true;
        var finalizer = Substitute.For<ITickerQPersistenceFinalizer>();
        finalizer.FinalizeAsync(Arg.Any<CancellationToken>())
            .Returns(_ => { order.Add("finalize"); gateOpenDuringFinalize = gate.IsActivated; return Task.CompletedTask; });

        var initializer = BuildInitializer(manager, gate, bootstrapper, finalizer);

        Assert.False(gate.IsActivated);
        await initializer.StartAsync(CancellationToken.None);

        Assert.True(gate.IsActivated);
        Assert.False(gateOpenDuringFinalize);
        Assert.Equal(new[] { "bootstrap", "repair", "reconcile", "finalize" }, order);
        await manager.Received(1).BeginReconciliationActivationEpochAsync(Arg.Any<ReconciliationActivationScope>(), 1, Arg.Any<CancellationToken>());
        await manager.Received().AdvanceReconciliationCheckpointAsync(
            Arg.Any<ReconciliationActivationScope>(), 1, "01-bootstrap-complete", Arg.Any<CancellationToken>());
        await manager.Received().AdvanceReconciliationCheckpointAsync(
            Arg.Any<ReconciliationActivationScope>(), 1, "04-finalization-complete", Arg.Any<CancellationToken>());
        await manager.Received(1).CommitReconciliationActivationEpochAsync(Arg.Any<ReconciliationActivationScope>(), 1, Arg.Any<CancellationToken>());
    }

    // ----- Strict epoch fencing -----

    [Fact]
    public async Task Initializer_FailsClosed_WhenHigherEpochIsActivating()
    {
        var gate = new TickerQActivationGate();
        var manager = Substitute.For<IInternalTickerManager>();
        manager.SupportsReconciliationActivationEpoch.Returns(true);
        manager.BeginReconciliationActivationEpochAsync(Arg.Any<ReconciliationActivationScope>(), 1, Arg.Any<CancellationToken>())
            .Returns(new ActivationEpochState { Epoch = 2, Phase = ActivationEpochPhase.Activating });
        var bootstrapper = Substitute.For<ITickerQPersistenceBootstrapper>();
        var initializer = BuildInitializer(manager, gate, bootstrapper, finalizer: null);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => initializer.StartAsync(CancellationToken.None));

        Assert.False(gate.IsActivated);
        await bootstrapper.DidNotReceive().BootstrapAsync(Arg.Any<CancellationToken>());
        await manager.DidNotReceive().RepairTimeTickerChainsAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Initializer_SameActivatedEpoch_SkipsFencedMutationsAndOpensGate()
    {
        var gate = new TickerQActivationGate();
        var localProviderStartupRan = false;
        var manager = Substitute.For<IInternalTickerManager>();
        manager.SupportsReconciliationActivationEpoch.Returns(true);
        manager.BeginReconciliationActivationEpochAsync(Arg.Any<ReconciliationActivationScope>(), 1, Arg.Any<CancellationToken>())
            .Returns(new ActivationEpochState { Epoch = 1, Phase = ActivationEpochPhase.Activated });
        var bootstrapper = Substitute.For<ITickerQPersistenceBootstrapper>();
        var initializer = BuildInitializer(manager, gate, bootstrapper, finalizer: null,
            externalProviderApplicationAction: _ => localProviderStartupRan = true);

        await initializer.StartAsync(CancellationToken.None);

        Assert.True(gate.IsActivated);
        Assert.True(localProviderStartupRan);
        await bootstrapper.DidNotReceive().BootstrapAsync(Arg.Any<CancellationToken>());
        await manager.DidNotReceive().RepairTimeTickerChainsAsync(Arg.Any<CancellationToken>());
        await manager.DidNotReceive().CommitReconciliationActivationEpochAsync(
            Arg.Any<long>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Initializer_SameEpochFollower_RunsRepeatableReadinessBeforeOpeningGate()
    {
        var gate = new TickerQActivationGate();
        var manager = Substitute.For<IInternalTickerManager>();
        manager.SupportsReconciliationActivationEpoch.Returns(true);
        manager.SupportsAuthoritativeCronReconciliation.Returns(true);
        manager.BeginReconciliationActivationEpochAsync(Arg.Any<ReconciliationActivationScope>(), 1,
                Arg.Any<CancellationToken>())
            .Returns(new ActivationEpochState { Epoch = 1, Phase = ActivationEpochPhase.Activated });
        var observedClosedGate = false;
        var readiness = Substitute.For<ITickerQPersistenceReadinessProbe>();
        readiness.ProbeAsync(Arg.Any<CancellationToken>()).Returns(_ =>
        {
            observedClosedGate = !gate.IsActivated;
            return Task.CompletedTask;
        });
        var initializer = BuildInitializer(manager, gate, null, null, readiness: readiness);

        await initializer.StartAsync(CancellationToken.None);

        Assert.True(observedClosedGate);
        await readiness.Received(1).ProbeAsync(Arg.Any<CancellationToken>());
        Assert.True(gate.IsActivated);
    }

    [Fact]
    public async Task Initializer_SchedulerHost_RequiresExplicitEpoch()
    {
        var manager = Substitute.For<IInternalTickerManager>();
        manager.SupportsReconciliationActivationEpoch.Returns(true);
        manager.SupportsAuthoritativeCronReconciliation.Returns(true);
        var initializer = BuildInitializer(manager, new TickerQActivationGate(), null, null,
            reconciliationEpoch: 0);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => initializer.StartAsync(CancellationToken.None));

        Assert.Contains("UseReconciliationEpoch", error.Message, StringComparison.Ordinal);
        await manager.DidNotReceive().BeginReconciliationActivationEpochAsync(
            Arg.Any<ReconciliationActivationScope>(), Arg.Any<long>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Initializer_UsesConfiguredEpoch_ForBeginCheckpointAndCommit()
    {
        const long epoch = 42;
        var gate = new TickerQActivationGate();
        var manager = Substitute.For<IInternalTickerManager>();
        manager.SupportsReconciliationActivationEpoch.Returns(true);
        manager.BeginReconciliationActivationEpochAsync(Arg.Any<ReconciliationActivationScope>(), epoch, Arg.Any<CancellationToken>())
            .Returns(new ActivationEpochState { Epoch = epoch, Phase = ActivationEpochPhase.Activating });
        manager.AdvanceReconciliationCheckpointAsync(Arg.Any<ReconciliationActivationScope>(), epoch, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new ActivationEpochState { Epoch = epoch, Phase = ActivationEpochPhase.Activating });
        manager.CommitReconciliationActivationEpochAsync(Arg.Any<ReconciliationActivationScope>(), epoch, Arg.Any<CancellationToken>())
            .Returns(new ActivationEpochState { Epoch = epoch, Phase = ActivationEpochPhase.Activated });
        manager.RepairTimeTickerChainsAsync(Arg.Any<CancellationToken>())
            .Returns(TimeTickerChainRepairResult.Empty);
        var initializer = BuildInitializer(manager, gate, null, null, epoch);

        await initializer.StartAsync(CancellationToken.None);

        await manager.Received(1).BeginReconciliationActivationEpochAsync(Arg.Any<ReconciliationActivationScope>(), epoch, Arg.Any<CancellationToken>());
        await manager.Received().AdvanceReconciliationCheckpointAsync(
            Arg.Any<ReconciliationActivationScope>(), epoch, Arg.Any<string>(), Arg.Any<CancellationToken>());
        await manager.Received(1).CommitReconciliationActivationEpochAsync(Arg.Any<ReconciliationActivationScope>(), epoch, Arg.Any<CancellationToken>());
        Assert.True(gate.IsActivated);
    }

    [Fact]
    public async Task Initializer_DoesNotOpenGate_WhenCommitIsNotExactTargetActivated()
    {
        var gate = new TickerQActivationGate();
        var manager = Substitute.For<IInternalTickerManager>();
        manager.SupportsReconciliationActivationEpoch.Returns(true);
        manager.BeginReconciliationActivationEpochAsync(Arg.Any<ReconciliationActivationScope>(), 1, Arg.Any<CancellationToken>())
            .Returns(new ActivationEpochState { Epoch = 1, Phase = ActivationEpochPhase.Activating });
        manager.AdvanceReconciliationCheckpointAsync(Arg.Any<ReconciliationActivationScope>(), 1, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new ActivationEpochState { Epoch = 1, Phase = ActivationEpochPhase.Activating });
        manager.CommitReconciliationActivationEpochAsync(Arg.Any<ReconciliationActivationScope>(), 1, Arg.Any<CancellationToken>())
            .Returns(new ActivationEpochState { Epoch = 2, Phase = ActivationEpochPhase.Activating });
        manager.RepairTimeTickerChainsAsync(Arg.Any<CancellationToken>())
            .Returns(TimeTickerChainRepairResult.Empty);
        var initializer = BuildInitializer(manager, gate, null, null);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => initializer.StartAsync(CancellationToken.None));

        Assert.False(gate.IsActivated);
    }

    [Fact]
    public async Task Initializer_QueueOnlyHost_SkipsLegacyProviderReconciliationWork()
    {
        const string queueOnlyFunction = "queue-only-initializer-discovery";
        TickerFunctionProvider.Build();
        TickerFunctionProvider.RegisterFunctions(new System.Collections.Generic.Dictionary<string,
            (string, Utilities.Enums.TickerTaskPriority, TickerFunctionDelegate, int)>
        {
            [queueOnlyFunction] = (string.Empty, Utilities.Enums.TickerTaskPriority.Normal, (_, _, _) => Task.CompletedTask, 0)
        });
        Assert.DoesNotContain(queueOnlyFunction, TickerFunctionProvider.TickerFunctions.Keys);
        var gate = new TickerQActivationGate();
        var manager = Substitute.For<IInternalTickerManager>();
        var initializer = BuildInitializer(manager, gate, null, null,
            registerBackgroundServices: false);

        await initializer.StartAsync(CancellationToken.None);

        Assert.Contains(queueOnlyFunction, TickerFunctionProvider.TickerFunctions.Keys);
        await manager.DidNotReceive().BeginReconciliationActivationEpochAsync(
            Arg.Any<long>(), Arg.Any<CancellationToken>());
        await manager.DidNotReceive().RepairTimeTickerChainsAsync(Arg.Any<CancellationToken>());
        await manager.DidNotReceive().MigrateDefinedCronTickers(
            Arg.Any<DefinedCronTickerSeed[]>(), Arg.Any<CancellationToken>());
    }

    // ----- Requirement 3: any exception or cancellation leaves the gate closed -----

    [Fact]
    public async Task Initializer_LeavesGateClosed_WhenAStepThrows()
    {
        var gate = new TickerQActivationGate();
        var boom = new InvalidOperationException("reconciliation failed");

        var manager = Substitute.For<IInternalTickerManager>();
        manager.SupportsReconciliationActivationEpoch.Returns(true);
        manager.BeginReconciliationActivationEpochAsync(Arg.Any<ReconciliationActivationScope>(), 1, Arg.Any<CancellationToken>())
            .Returns(new ActivationEpochState { Epoch = 1, Phase = ActivationEpochPhase.Activating });
        manager.AdvanceReconciliationCheckpointAsync(Arg.Any<ReconciliationActivationScope>(), 1, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new ActivationEpochState { Epoch = 1, Phase = ActivationEpochPhase.Activating });

        var finalizer = Substitute.For<ITickerQPersistenceFinalizer>();
        finalizer.FinalizeAsync(Arg.Any<CancellationToken>()).Returns<Task>(_ => throw boom);

        var initializer = BuildInitializer(manager, gate, bootstrapper: null, finalizer);

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(
            () => initializer.StartAsync(CancellationToken.None));

        Assert.Same(boom, thrown);
        Assert.False(gate.IsActivated);
        Assert.Same(boom, gate.ClosedReason);
    }

    [Fact]
    public async Task Initializer_LeavesGateClosed_WhenCancelled()
    {
        var gate = new TickerQActivationGate();

        var manager = Substitute.For<IInternalTickerManager>();
        manager.SupportsReconciliationActivationEpoch.Returns(true);
        manager.BeginReconciliationActivationEpochAsync(Arg.Any<ReconciliationActivationScope>(), 1, Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                call.Arg<CancellationToken>().ThrowIfCancellationRequested();
                return new ActivationEpochState { Epoch = 1, Phase = ActivationEpochPhase.Activating };
            });

        var bootstrapper = Substitute.For<ITickerQPersistenceBootstrapper>();
        bootstrapper.BootstrapAsync(Arg.Any<CancellationToken>())
            .Returns(ci => { ci.Arg<CancellationToken>().ThrowIfCancellationRequested(); return Task.CompletedTask; });

        var initializer = BuildInitializer(manager, gate, bootstrapper, finalizer: null);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => initializer.StartAsync(cts.Token));

        Assert.False(gate.IsActivated);
        Assert.IsAssignableFrom<OperationCanceledException>(gate.ClosedReason);
    }

    // ----- Requirement 4: legacy provider fails startup before repair/reconciliation -----

    [Fact]
    public async Task CapabilityValidator_FailsStartup_ForLegacyProvider_BeforeRepairOrReconciliation()
    {
        var manager = Substitute.For<IInternalTickerManager>();
        // A legacy provider inherits the fail-closed default (false) — never configured here.

        var validator = new TickerQReconciliationActivationCapabilityValidator(manager);

        await Assert.ThrowsAsync<NotSupportedException>(() => validator.StartAsync(CancellationToken.None));

        await manager.DidNotReceive().RepairTimeTickerChainsAsync(Arg.Any<CancellationToken>());
        await manager.DidNotReceive().MigrateDefinedCronTickers(
            Arg.Any<DefinedCronTickerSeed[]>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CapabilityValidator_Passes_ForSupportedProvider()
    {
        var manager = Substitute.For<IInternalTickerManager>();
        manager.SupportsReconciliationActivationEpoch.Returns(true);

        var validator = new TickerQReconciliationActivationCapabilityValidator(manager);

        await validator.StartAsync(CancellationToken.None);
        await validator.StopAsync(CancellationToken.None);
    }

    // ----- DI wiring -----

    [Fact]
    public void AddTickerQ_RegistersActivationGate_AsSingleton()
    {
        var services = new ServiceCollection();
        services.AddTickerQ(options =>
            options.UseDefinedCronApplicationNamespace("activation-gate-tests"));
        var provider = services.BuildServiceProvider();

        var gate = provider.GetService<ITickerQActivationGate>();
        Assert.NotNull(gate);
        Assert.Same(gate, provider.GetService<ITickerQActivationGate>());
        Assert.False(gate!.IsActivated);
    }

    private static TickerQInitializerHostedService BuildInitializer(
        IInternalTickerManager manager,
        ITickerQActivationGate gate,
        ITickerQPersistenceBootstrapper bootstrapper,
        ITickerQPersistenceFinalizer finalizer,
        long reconciliationEpoch = 1,
        bool registerBackgroundServices = true,
        Action<IServiceProvider> externalProviderApplicationAction = null,
        ITickerQPersistenceReadinessProbe readiness = null)
    {
        manager.SupportsAuthoritativeCronReconciliation.Returns(true);
        var context = new TickerExecutionContext
        {
            OptionsSeeding = new TestOptionsSeeding(reconciliationEpoch, registerBackgroundServices),
            ExternalProviderApplicationAction = externalProviderApplicationAction
        };
        var configuration = new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build();

        var services = new ServiceCollection();
        services.AddSingleton(context);
        services.AddSingleton(configuration);
        services.AddSingleton(manager);
        services.AddSingleton(new SchedulerOptionsBuilder());
        services.AddSingleton(gate);
        if (bootstrapper != null)
            services.AddSingleton(bootstrapper);
        if (finalizer != null)
            services.AddSingleton(finalizer);
        if (readiness != null)
            services.AddSingleton(readiness);

        return new TickerQInitializerHostedService(
            context, services.BuildServiceProvider(), configuration, LicenseTestState.Active())
        {
            InitializationRequested = true
        };
    }

    private sealed class TestOptionsSeeding(long reconciliationEpoch, bool registerBackgroundServices)
        : ITickerOptionsSeeding
    {
        public bool SeedDefinedCronTickers => true;
        public bool RegisterBackgroundServices { get; } = registerBackgroundServices;
        public long ReconciliationEpoch { get; } = reconciliationEpoch;
        public string DefinedCronApplicationNamespace => "tickerq-tests";
        public Func<IServiceProvider, CancellationToken, Task> TimeSeederAction => null;
        public Func<IServiceProvider, CancellationToken, Task> CronSeederAction => null;
    }
}
