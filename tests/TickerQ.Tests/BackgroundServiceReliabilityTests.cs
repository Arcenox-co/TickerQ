using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using TickerQ.BackgroundServices;
using TickerQ.DependencyInjection;
using TickerQ.Dispatcher;
using TickerQ.Utilities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Interfaces.Managers;
using TickerQ.Utilities.Models;

namespace TickerQ.Tests;

[Collection("TickerCancellationTokenState")]
public class BackgroundServiceReliabilityTests
{
    private static InternalFunctionContext Context(TickerType type = TickerType.TimeTicker) => new()
    { TickerId = Guid.NewGuid(), AcquisitionToken = Guid.NewGuid(), FunctionName = "BackgroundReview", Type = type };

    private static AcquisitionLease[] Leases()
    {
        var time = new List<AcquisitionLease>();
        var cron = new List<AcquisitionLease>();
        TickerCancellationTokenManager.SnapshotRunningForLeaseRenewal(time, cron);
        return time.Concat(cron).ToArray();
    }

    [Fact]
    public async Task ImmediateCronDispatch_TracksLeaseBeforeAdmission_AndExecutesWithoutOverdueFlag()
    {
        var manager = Substitute.For<IInternalTickerManager>();
        var scheduler = Substitute.For<ITickerQTaskScheduler>();
        var handler = Substitute.For<ITickerExecutionTaskHandler>();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowAdmission = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Func<CancellationToken, Task>? work = null;
        scheduler.QueueAsync(Arg.Any<Func<CancellationToken, Task>>(), Arg.Any<TickerTaskPriority>(), Arg.Any<CancellationToken>())
            .Returns(call => { work = call.ArgAt<Func<CancellationToken, Task>>(0); entered.TrySetResult(); return new ValueTask(allowAdmission.Task); });
        var context = Context(TickerType.CronTickerOccurrence);
        context.ParentId = Guid.NewGuid();
        var license = LicenseTestState.Active();
        using var queue = new AcquiredExecutionQueue(manager, handler, scheduler, NullLogger.Instance, license);
        var dispatcher = new TickerQDispatcher(scheduler, handler, license, queue);
        var dispatch = dispatcher.DispatchAsync([context]);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        try { Assert.Contains(Leases(), x => x.TickerId == context.TickerId && x.AcquisitionToken == context.AcquisitionToken); }
        finally { allowAdmission.TrySetResult(); }
        await dispatch;
        await work!(CancellationToken.None);
        await handler.Received(1).ExecuteRegisteredTaskAsync(context, false, Arg.Any<CancellationTokenSource>(), Arg.Any<CancellationToken>());
        Assert.DoesNotContain(Leases(), x => x.TickerId == context.TickerId);
    }

    [Fact]
    public async Task ImmediateDispatch_AdmissionFailureReleasesAcquisition_AndDoesNotReportSuccess()
    {
        var manager = Substitute.For<IInternalTickerManager>();
        var scheduler = Substitute.For<ITickerQTaskScheduler>();
        scheduler.QueueAsync(Arg.Any<Func<CancellationToken, Task>>(), Arg.Any<TickerTaskPriority>(), Arg.Any<CancellationToken>())
            .Returns(_ => ValueTask.FromException(new InvalidOperationException("Scheduler stopped")));
        var handler = Substitute.For<ITickerExecutionTaskHandler>();
        var license = LicenseTestState.Active();
        using var queue = new AcquiredExecutionQueue(manager, handler, scheduler, NullLogger.Instance, license);
        var context = Context(TickerType.CronTickerOccurrence);
        var dispatcher = new TickerQDispatcher(scheduler, handler, license, queue);
        await Assert.ThrowsAsync<InvalidOperationException>(() => dispatcher.DispatchAsync([context]));
        await manager.Received(1).ReleaseAcquiredResources(Arg.Is<InternalFunctionContext[]>(items => items.Single() == context), CancellationToken.None);
        Assert.DoesNotContain(Leases(), x => x.TickerId == context.TickerId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CronOccurrence_UsesOwnGenerationEvenWithScheduleParent(bool legacy)
    {
        var context = Context(TickerType.CronTickerOccurrence);
        context.ParentId = Guid.NewGuid();
        CancellationTokenSource source;
        if (legacy)
        {
            source = new CancellationTokenSource();
            TickerCancellationTokenManager.AddTickerCancellationToken(source, context, false);
        }
        else source = TickerCancellationTokenManager.TryRegisterAcquired(context, false)!;
        try
        {
            Assert.Equal(context.AcquisitionToken, Leases().Single(x => x.TickerId == context.TickerId).AcquisitionToken);
            Assert.True(TickerCancellationTokenManager.RequestTickerCancellation(
                new TickerExecutionLease(context.Type, context.TickerId, context.AcquisitionToken)));
            Assert.True(source.IsCancellationRequested);
        }
        finally { TickerCancellationTokenManager.RemoveTickerCancellationToken(context.TickerId, source); }
    }

    [Fact]
    public async Task AcquiredBatch_IsLeasedBeforeCapacityWait_AndAllUnpublishedJobsReleaseOnFailure()
    {
        var contexts = new[] { Context(), Context() };
        var manager = Substitute.For<IInternalTickerManager>();
        var scheduler = Substitute.For<ITickerQTaskScheduler>();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var unblock = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        scheduler.QueueAsync(Arg.Any<Func<CancellationToken, Task>>(), Arg.Any<TickerTaskPriority>(), Arg.Any<CancellationToken>())
            .Returns(_ => new ValueTask(Block()));
        async Task Block() { entered.TrySetResult(); await unblock.Task; throw new InvalidOperationException("Frozen"); }
        using var queue = new AcquiredExecutionQueue(manager, Substitute.For<ITickerExecutionTaskHandler>(), scheduler, NullLogger.Instance);
        var publish = queue.PublishAsync(contexts, true, CancellationToken.None);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        try
        {
            foreach (var context in contexts)
                Assert.Contains(Leases(), x => x.TickerId == context.TickerId && x.AcquisitionToken == context.AcquisitionToken);
        }
        finally { unblock.TrySetResult(); }
        Assert.False(await publish);
        foreach (var context in contexts)
        {
            await manager.Received().ReleaseAcquiredResources(Arg.Is<InternalFunctionContext[]>(a => a.Length == 1 && a[0] == context), Arg.Any<CancellationToken>());
            Assert.DoesNotContain(Leases(), x => x.TickerId == context.TickerId);
        }
    }

    [Fact]
    public async Task ScannerCancellation_ReleasesQueuedJobs_ButAllowsRunningJobsToDrain()
    {
        var manager = Substitute.For<IInternalTickerManager>();
        var scheduler = Substitute.For<ITickerQTaskScheduler>();
        var handler = Substitute.For<ITickerExecutionTaskHandler>();
        var work = new List<Func<CancellationToken, Task>>();
        scheduler.QueueAsync(Arg.Any<Func<CancellationToken, Task>>(), Arg.Any<TickerTaskPriority>(), Arg.Any<CancellationToken>())
            .Returns(call => { work.Add(call.ArgAt<Func<CancellationToken, Task>>(0)); return ValueTask.CompletedTask; });
        var contexts = new[] { Context(), Context() };
        var started = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        handler.ExecuteRegisteredTaskAsync(contexts[0], true, Arg.Any<CancellationTokenSource>(), Arg.Any<CancellationToken>())
            .Returns(call => { started.TrySetResult(call.ArgAt<CancellationToken>(3)); return finish.Task; });
        using var queue = new AcquiredExecutionQueue(manager, handler, scheduler, NullLogger.Instance);
        using var scanner = new CancellationTokenSource();
        await queue.PublishAsync(contexts, true, scanner.Token);
        var running = work[0](CancellationToken.None);
        var token = await started.Task.WaitAsync(TimeSpan.FromSeconds(3));
        try
        {
            scanner.Cancel();
            Assert.False(token.IsCancellationRequested);
            Assert.Contains(Leases(), x => x.TickerId == contexts[0].TickerId);
            Assert.DoesNotContain(Leases(), x => x.TickerId == contexts[1].TickerId);
            await work[1](CancellationToken.None); // Late dequeue must not execute a released job.
            await handler.DidNotReceive().ExecuteRegisteredTaskAsync(contexts[1], true, Arg.Any<CancellationTokenSource>(), Arg.Any<CancellationToken>());
        }
        finally { finish.TrySetResult(); await running; }
        Assert.DoesNotContain(Leases(), x => x.TickerId == contexts[0].TickerId);
    }

    [Fact]
    public async Task QueueShutdown_WaitsForExactReleaseBeforeHostDisposesPersistence()
    {
        var context = Context();
        var manager = Substitute.For<IInternalTickerManager>();
        var releaseStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        manager.ReleaseAcquiredResources(Arg.Any<InternalFunctionContext[]>(), Arg.Any<CancellationToken>())
            .Returns(_ => { releaseStarted.TrySetResult(); return finish.Task; });
        using var queue = new AcquiredExecutionQueue(manager, Substitute.For<ITickerExecutionTaskHandler>(),
            Substitute.For<ITickerQTaskScheduler>(), NullLogger.Instance);
        await queue.PublishAsync([context], false, CancellationToken.None);
        var stop = queue.StopAsync(CancellationToken.None);
        await releaseStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));
        try
        {
            Assert.False(stop.IsCompleted);
            Assert.Contains(Leases(), x => x.TickerId == context.TickerId);
        }
        finally { finish.TrySetResult(); await stop; }
        Assert.DoesNotContain(Leases(), x => x.TickerId == context.TickerId);
    }

    [Fact]
    public async Task SlowStaleSweep_DoesNotBlockRenewals_AndReceivesDeadlineCancellation()
    {
        var context = Context();
        var owned = TickerCancellationTokenManager.TryRegisterAcquired(context, false)!;
        var manager = Substitute.For<IInternalTickerManager>();
        manager.SupportsLeaseBasedRecovery.Returns(true);
        var renewals = 0;
        var renewed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        manager.RenewActiveTickerLeasesAsync(Arg.Any<IReadOnlyCollection<AcquisitionLease>>(), Arg.Any<IReadOnlyCollection<AcquisitionLease>>(), Arg.Any<CancellationToken>())
            .Returns(call => { if (Interlocked.Increment(ref renewals) >= 3) renewed.TrySetResult(); return call.ArgAt<IReadOnlyCollection<AcquisitionLease>>(0).Count; });
        var sweep = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        var unblock = new TaskCompletionSource<StaleTickerRecoveryResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        // Deliberately ignore cancellation to prove renewal is independent even of a broken provider.
        manager.RecoverStaleTickersAsync(Arg.Any<CancellationToken>()).Returns(call => { sweep.TrySetResult(call.Arg<CancellationToken>()); return unblock.Task; });
        using var service = new TickerQStaleJobRecoveryBackgroundService(manager,
            new SchedulerOptionsBuilder { LeaseRenewalInterval = TimeSpan.FromMilliseconds(25) },
            NullLogger<TickerQStaleJobRecoveryBackgroundService>.Instance, Substitute.For<ITickerQFailureNotifier>(), LicenseTestState.Active());
        await service.StartAsync(CancellationToken.None);
        try
        {
            var token = await sweep.Task.WaitAsync(TimeSpan.FromSeconds(3));
            await renewed.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.True(token.IsCancellationRequested);
        }
        finally
        {
            unblock.TrySetResult(new StaleTickerRecoveryResult());
            await service.StopAsync(CancellationToken.None);
            TickerCancellationTokenManager.RemoveTickerCancellationToken(context.TickerId, owned);
        }
    }

    [Fact]
    public async Task Scheduler_LogsOriginalFault_AndSurvivesThrowingNotificationAndCleanup()
    {
        var context = new TickerExecutionContext { NotifyCoreAction = (_, _) => throw new InvalidOperationException("notification") };
        var manager = Substitute.For<IInternalTickerManager>();
        var failure = new InvalidOperationException("database");
        var recovered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var scans = 0;
        manager.GetNextTickers(Arg.Any<CancellationToken>()).Returns(_ =>
        {
            if (Interlocked.Increment(ref scans) == 1) throw failure;
            recovered.TrySetResult();
            return (Timeout.InfiniteTimeSpan, Array.Empty<InternalFunctionContext>());
        });
        manager.ReleaseAcquiredResources(Arg.Any<InternalFunctionContext[]>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("cleanup")));
        var sink = Substitute.For<ILogger>();
        var factory = Substitute.For<ILoggerFactory>();
        factory.CreateLogger(Arg.Any<string>()).Returns(sink);
        var logger = new Logger<TickerQSchedulerBackgroundService>(factory);
        using var service = new TickerQSchedulerBackgroundService(context, Substitute.For<ITickerExecutionTaskHandler>(),
            Substitute.For<ITickerQTaskScheduler>(), manager, new SchedulerOptionsBuilder(), LicenseTestState.Active(), logger);
        await service.StartAsync(CancellationToken.None);
        try
        {
            await recovered.Task.WaitAsync(TimeSpan.FromSeconds(4));
            Assert.True(service.IsRunning);
            Assert.Contains(sink.ReceivedCalls(), c => c.GetMethodInfo().Name == "Log" && c.GetArguments().Contains(failure));
        }
        finally { await service.StopAsync(CancellationToken.None); }
        Assert.False(service.IsRunning);
    }

    [Theory]
    [InlineData("renewal-zero")]
    [InlineData("renewal-negative")]
    [InlineData("lease-too-short")]
    [InlineData("fallback-negative")]
    [InlineData("fallback-too-long")]
    [InlineData("batch-zero")]
    [InlineData("workers-zero")]
    [InlineData("shutdown-negative")]
    public void InvalidSchedulerConfiguration_FailsAtRegistration(string invalid)
    {
        Assert.ThrowsAny<ArgumentException>(() => new ServiceCollection().AddTickerQ(o => o.UseApplicationNamespace("background-tests").ConfigureScheduler(s =>
        {
            switch (invalid)
            {
                case "renewal-zero": s.LeaseRenewalInterval = TimeSpan.Zero; break;
                case "renewal-negative": s.LeaseRenewalInterval = TimeSpan.FromSeconds(-2); break;
                case "lease-too-short": s.LeaseDuration = s.LeaseRenewalInterval; break;
                case "fallback-negative": s.FallbackIntervalChecker = TimeSpan.FromSeconds(-1); break;
                case "fallback-too-long": s.FallbackIntervalChecker = TimeSpan.MaxValue; break;
                case "batch-zero": s.FallbackBatchSize = 0; break;
                case "workers-zero": s.MaxConcurrency = 0; break;
                case "shutdown-negative": s.ShutdownDrainTimeout = TimeSpan.FromSeconds(-1); break;
            }
        })));
    }

    [Fact]
    public void DefaultConfiguration_AndDocumentedZeroWaits_AreAccepted()
    {
        new ServiceCollection().AddTickerQ(o => o.UseApplicationNamespace("background-tests"));
        new ServiceCollection().AddTickerQ(o => o.UseApplicationNamespace("background-tests").ConfigureScheduler(s =>
        { s.MinPollingInterval = TimeSpan.FromSeconds(-1); s.ShutdownDrainTimeout = TimeSpan.Zero; s.MaxStaleRestarts = 0; }));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Webhook_RetriesTimeoutAndServerError_ThenDrains(bool timeout)
    {
        var transport = new TestTransport(call => call == 1
            ? timeout ? throw new TaskCanceledException("HTTP timeout", new TimeoutException()) : new(HttpStatusCode.ServiceUnavailable)
            : new(HttpStatusCode.OK));
        var notifier = new WebhookFailureNotifier(new FailureWebhookOptions { Url = "https://example.test/hook" }, NullLogger<WebhookFailureNotifier>.Instance);
        using var service = new TickerQWebhookNotifierBackgroundService(notifier, NullLogger<TickerQWebhookNotifierBackgroundService>.Instance, new HttpClient(transport));
        await service.StartAsync(CancellationToken.None);
        notifier.Notify(new TickerFailureEvent { Kind = "test" });
        await service.StopAsync(CancellationToken.None);
        Assert.Equal(2, transport.Calls);
        Assert.False(notifier.Reader.TryPeek(out _));
        Assert.True(service.ExecuteTask!.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task Webhook_DoesNotRetryPermanentClientError()
    {
        var transport = new TestTransport(_ => new(HttpStatusCode.Unauthorized));
        var notifier = new WebhookFailureNotifier(new FailureWebhookOptions { Url = "https://example.test/hook" }, NullLogger<WebhookFailureNotifier>.Instance);
        using var service = new TickerQWebhookNotifierBackgroundService(notifier, NullLogger<TickerQWebhookNotifierBackgroundService>.Instance, new HttpClient(transport));
        await service.StartAsync(CancellationToken.None);
        notifier.Notify(new TickerFailureEvent { Kind = "test" });
        await service.StopAsync(CancellationToken.None);
        Assert.Equal(1, transport.Calls);
    }

    [Fact]
    public async Task Webhook_ShutdownDeadline_CancelsSlowDeliveryWithoutRetry()
    {
        var transport = new SlowTransport();
        var notifier = new WebhookFailureNotifier(new FailureWebhookOptions
        { Url = "https://example.test/hook", ShutdownDrainTimeout = TimeSpan.FromMilliseconds(50) }, NullLogger<WebhookFailureNotifier>.Instance);
        using var service = new TickerQWebhookNotifierBackgroundService(notifier, NullLogger<TickerQWebhookNotifierBackgroundService>.Instance, new HttpClient(transport));
        await service.StartAsync(CancellationToken.None);
        notifier.Notify(new TickerFailureEvent { Kind = "test" });
        await transport.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await service.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(3));
        await service.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(1, transport.Calls);
        Assert.True(transport.Cancelled);
    }

    [Fact]
    public void HostRegistration_SharesOwnershipQueue_AndStopsWebhookAfterProducers()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddTickerQ(o => o.UseApplicationNamespace("background-tests").NotifyFailuresViaWebhook("https://example.test/hook"));
        using var host = builder.Build();
        var hosted = host.Services.GetServices<IHostedService>().ToList();
        var webhook = hosted.FindIndex(x => x is TickerQWebhookNotifierBackgroundService);
        Assert.True(webhook < hosted.FindIndex(x => x is TickerQSchedulerBackgroundService));
        Assert.True(webhook < hosted.FindIndex(x => x is TickerQStaleJobRecoveryBackgroundService));
        var queue = host.Services.GetRequiredService<AcquiredExecutionQueue>();
        foreach (var service in hosted.Where(x => x is TickerQSchedulerBackgroundService or TickerQFallbackBackgroundService))
            Assert.Same(queue, service.GetType().GetField("_acquiredQueue", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(service));
        var dispatcher = host.Services.GetRequiredService<ITickerQDispatcher>();
        Assert.Same(queue, dispatcher.GetType().GetField("_acquiredQueue", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(dispatcher));
    }

    private sealed class SlowTransport : HttpMessageHandler
    {
        public readonly TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Calls;
        public bool Cancelled;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Calls);
            Started.TrySetResult();
            try { await Task.Delay(Timeout.Infinite, cancellationToken); }
            catch (OperationCanceledException) { Cancelled = true; throw; }
            return new(HttpStatusCode.OK);
        }
    }

    private sealed class TestTransport(Func<int, HttpResponseMessage> response) : HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(response(Interlocked.Increment(ref Calls)));
    }
}
