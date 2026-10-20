using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using TickerQ.BackgroundServices;
using TickerQ.Dispatcher;
using TickerQ.Provider;
using TickerQ.TickerQThreadPool;
using TickerQ.Utilities;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Instrumentation;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Interfaces.Managers;
using TickerQ.Utilities.Managers;
using TickerQ.Utilities.Models;

namespace TickerQ.Tests;

public partial class TickerManagerTests
{
    public sealed class DeepRuntimeTicker : TimeTickerEntity<DeepRuntimeTicker> { }

    [Theory]
    [InlineData("scheduled")]
    [InlineData("immediate")]
    [InlineData("run-now")]
    public async Task DeepTimeChain_PublicManagerThroughPersistenceAndExecution_DoesNotRecurse(string route)
    {
        const int depth = 20_000;
        var options = new SchedulerOptionsBuilder { MaxConcurrency = 1, MaxConcurrentChainSteps = 1 };
        options.BindRuntimeActivationScope("deep-chain-" + Guid.NewGuid().ToString("N"), 1, schedulerEnabled: false);
        await using var scheduler = new TickerQTaskScheduler(1);
        await using var services = new ServiceCollection().AddSingleton(_clock).AddSingleton(options)
            .AddSingleton<ITickerQTaskScheduler>(scheduler).BuildServiceProvider();
        var persistence = new TickerInMemoryPersistenceProvider<DeepRuntimeTicker, CronTickerEntity>(services);
        IInternalTickerManager internalManager = new InternalTickerManager<DeepRuntimeTicker, CronTickerEntity>(
            persistence, _clock, _notificationHubSender, options);
        var license = LicenseTestState.Active(_clock);
        var handler = new TickerExecutionTaskHandler(services, _clock, Substitute.For<ITickerQInstrumentation>(),
            internalManager, options, Substitute.For<ITickerQFailureNotifier>(), license, new TickerFunctionConcurrencyGate());
        using var queue = new AcquiredExecutionQueue(internalManager, handler, scheduler, NullLogger.Instance, license);
        var dispatcher = new TickerQDispatcher(scheduler, handler, license, queue);
        var executionContext = new TickerExecutionContext { RuntimePartition = options.RuntimePartition };
        ITimeTickerManager<DeepRuntimeTicker> manager = new TickerManager<DeepRuntimeTicker, CronTickerEntity>(
            persistence, _hostScheduler, _clock, _notificationHubSender, executionContext, dispatcher,
            options, new TickerQActivationGate());
        var root = new DeepRuntimeTicker
        {
            Function = ValidFunctionName,
            ExecutionTime = route == "immediate" ? FixedUtcNow : FixedUtcNow.AddMinutes(5)
        };
        var leaf = root;
        for (var i = 1; i < depth; i++)
        {
            var child = new DeepRuntimeTicker
            {
                Function = ValidFunctionName,
                RunCondition = i == 1 ? RunCondition.OnFailure : RunCondition.OnSuccess
            };
            leaf.Children.Add(child);
            leaf = child;
        }

        try
        {
            var added = await manager.AddAsync(root);
            Assert.True(added.IsSucceeded, added.Exception?.ToString());
            Assert.Equal(depth, added.AffectedRows);
            if (route == "scheduled")
            {
                _clock.UtcNow.Returns(FixedUtcNow.AddMinutes(5));
                var (_, planned) = await internalManager.GetNextTickers();
                executionContext.SetFunctions(planned);
                var acquired = await internalManager.SetTickersInProgress(executionContext.Functions);
                Assert.Single(acquired);
                executionContext.SetFunctions(acquired);
                await dispatcher.DispatchAsync(executionContext.Functions);
            }
            else if (route == "run-now")
            {
                var started = await manager.RunNowAsync(root.Id);
                Assert.True(started.IsSucceeded, started.Exception?.ToString());
                Assert.Equal(1, started.AffectedRows);
            }

            Assert.True(await scheduler.WaitForRunningTasksAsync(TimeSpan.FromSeconds(20)));
            Assert.Equal(TickerStatus.Done, (await persistence.GetTimeTickerById(root.Id)).Status);
            var storedLeaf = await persistence.GetTimeTickerById(leaf.Id);
            Assert.Equal(TickerStatus.Skipped, storedLeaf.Status);
            Assert.Equal(root.Id, storedLeaf.ChainRootId);
            Assert.Equal(0, scheduler.ActiveOrchestrationCount);
            var owned = new List<AcquisitionLease>();
            TickerCancellationTokenManager.SnapshotRunningForLeaseRenewal(options.RuntimePartition.StorageKey, owned, []);
            Assert.Empty(owned);
        }
        finally
        {
            await queue.StopAsync(CancellationToken.None);
            await scheduler.WaitForRunningTasksAsync(TimeSpan.FromSeconds(20));
            TickerInMemoryPersistenceProvider<DeepRuntimeTicker, CronTickerEntity>.ResetAllStateForTests();
        }
    }
}
