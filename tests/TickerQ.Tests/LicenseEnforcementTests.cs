using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using TickerQ.DependencyInjection;
using TickerQ.Dispatcher;
using TickerQ.Utilities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Instrumentation;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Interfaces.Managers;
using TickerQ.Utilities.Licensing;
using TickerQ.Utilities.Models;

namespace TickerQ.Tests;

public sealed class LicenseEnforcementTests
{
    [Fact]
    public void AddTickerQ_RegistersLicenseValidationBeforeEveryOtherHostedService()
    {
        var services = new ServiceCollection();

        services.AddTickerQ(options =>
        {
            options.UseDefinedCronApplicationNamespace("license-order-test");
            options.UseReconciliationEpoch(1);
        });

        var hostedServices = services
            .Where(descriptor => descriptor.ServiceType == typeof(IHostedService))
            .ToArray();

        Assert.NotEmpty(hostedServices);
        Assert.Equal("TickerQLicenseHostedService", hostedServices[0].ImplementationType?.Name);
    }

    [Fact]
    public async Task Dispatcher_MissingLicense_BlocksBeforeQueuePublication()
    {
        var scheduler = Substitute.For<ITickerQTaskScheduler>();
        var handler = Substitute.For<ITickerExecutionTaskHandler>();
        var license = MissingLicenseProvider();
        var dispatcher = new TickerQDispatcher(
            scheduler, handler, new TickerFunctionConcurrencyGate(), license);
        var context = new InternalFunctionContext
        {
            TickerId = Guid.NewGuid(),
            FunctionName = "Blocked",
            TimeTickerChildren = [],
        };

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => dispatcher.DispatchAsync([context]));

        Assert.Contains("execution is disabled", error.Message);
        await scheduler.DidNotReceiveWithAnyArgs().QueueAsync(default!, default, default);
    }

    [Fact]
    public async Task WorkerExecution_MissingLicense_BlocksBeforeDelegateInvocation()
    {
        var invoked = false;
        var handler = CreateHandler(MissingLicenseProvider());
        var context = new InternalFunctionContext
        {
            TickerId = Guid.NewGuid(),
            FunctionName = "BlockedWorker",
            Type = TickerType.TimeTicker,
            Status = TickerStatus.Idle,
            ExecutionTime = DateTime.UtcNow,
            RetryIntervals = [],
            TimeTickerChildren = [],
            CachedDelegate = (_, _, _) =>
            {
                invoked = true;
                return Task.CompletedTask;
            },
        };

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => handler.ExecuteWorkerTaskAsync(context, isDue: false));

        Assert.Contains("execution is disabled", error.Message);
        Assert.False(invoked);
    }

    [Fact]
    public async Task WorkerExecution_ExpiredFullLicense_RemainsAllowed()
    {
        var invoked = false;
        var clock = Substitute.For<ITickerClock>();
        clock.UtcNow.Returns(new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc));
        var license = new TickerQLicenseStateProvider(clock);
        license.Publish(TickerQLicenseState.ForCertificate(
            TickerQLicenseStatus.Expired,
            isEvaluation: false,
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Paid Workspace",
            "Business",
            "Full",
            new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            -31,
            4,
            "Ed25519",
            "test",
            "5.x",
            executionAllowedAfterExpiry: true));
        var handler = CreateHandler(license, clock);
        var context = new InternalFunctionContext
        {
            TickerId = Guid.NewGuid(),
            FunctionName = "PaidWorker",
            Type = TickerType.TimeTicker,
            Status = TickerStatus.Idle,
            ExecutionTime = clock.UtcNow,
            RetryIntervals = [],
            TimeTickerChildren = [],
            CachedDelegate = (_, _, _) =>
            {
                invoked = true;
                return Task.CompletedTask;
            },
        };

        var result = await handler.ExecuteWorkerTaskAsync(context, isDue: false);

        Assert.True(invoked);
        Assert.Equal(TickerStatus.Done, result.Status);
    }

    [Fact]
    public async Task WorkerExecution_LicenseExpiresDuringPreparation_BlocksAtFinalInvocationBoundary()
    {
        var now = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc);
        var clock = Substitute.For<ITickerClock>();
        clock.UtcNow.Returns(now);
        var license = new TickerQLicenseStateProvider(clock);
        license.Publish(TickerQLicenseState.ForCertificate(
            TickerQLicenseStatus.Active,
            isEvaluation: true,
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Evaluation Workspace",
            "Evaluation",
            "Evaluation",
            new DateTimeOffset(now.AddDays(-30)),
            new DateTimeOffset(now.AddMinutes(1)),
            1,
            4,
            "Ed25519",
            "test",
            null));

        var manager = Substitute.For<IInternalTickerManager>();
        manager.GetParentResultAsync(Arg.Any<Guid>(), Arg.Any<TickerType>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                clock.UtcNow.Returns(now.AddMinutes(2));
                return Task.FromResult<TickerResultEnvelope>(null);
            });
        var invoked = false;
        var handler = new TickerExecutionTaskHandler(
            new ServiceCollection().BuildServiceProvider(),
            clock,
            Substitute.For<ITickerQInstrumentation>(),
            manager,
            new SchedulerOptionsBuilder(),
            Substitute.For<ITickerQFailureNotifier>(),
            license,
            _ => null);
        var context = new InternalFunctionContext
        {
            TickerId = Guid.NewGuid(),
            ParentId = Guid.NewGuid(),
            FunctionName = "ExpiresBeforeInvocation",
            Type = TickerType.TimeTicker,
            Status = TickerStatus.Idle,
            ExecutionTime = now,
            RetryIntervals = [],
            TimeTickerChildren = [],
            CachedDelegate = (_, _, _) =>
            {
                invoked = true;
                return Task.CompletedTask;
            },
        };

        var result = await handler.ExecuteWorkerTaskAsync(context, isDue: false);

        Assert.False(invoked);
        Assert.Equal(TickerStatus.Failed, result.Status);
        Assert.Contains("execution is disabled", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    private static TickerQLicenseStateProvider MissingLicenseProvider()
    {
        var clock = Substitute.For<ITickerClock>();
        clock.UtcNow.Returns(DateTime.UtcNow);
        return new TickerQLicenseStateProvider(clock);
    }

    private static TickerExecutionTaskHandler CreateHandler(
        TickerQLicenseStateProvider license,
        ITickerClock? suppliedClock = null)
    {
        var services = new ServiceCollection().BuildServiceProvider();
        var clock = suppliedClock ?? Substitute.For<ITickerClock>();
        if (suppliedClock == null)
            clock.UtcNow.Returns(DateTime.UtcNow);
        return new TickerExecutionTaskHandler(
            services,
            clock,
            Substitute.For<ITickerQInstrumentation>(),
            Substitute.For<IInternalTickerManager>(),
            new SchedulerOptionsBuilder(),
            Substitute.For<ITickerQFailureNotifier>(),
            license,
            _ => null);
    }
}
