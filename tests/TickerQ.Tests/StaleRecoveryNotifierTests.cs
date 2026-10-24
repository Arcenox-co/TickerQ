using Microsoft.Extensions.Logging;
using NSubstitute;
using TickerQ.BackgroundServices;
using TickerQ.Utilities;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Interfaces.Managers;
using TickerQ.Utilities.Models;

namespace TickerQ.Tests;

/// <summary>
/// The stale-job sweep used to call the failure notifier without a guard, so a throwing custom
/// notifier turned a successful recovery into a "sweep failed" warning and skipped its summary log.
/// </summary>
public class StaleRecoveryNotifierTests
{
    [Fact]
    public async Task Throwing_notifier_does_not_fail_the_sweep_or_skip_the_recovery_log()
    {
        var manager = Substitute.For<IInternalTickerManager>();
        manager.SupportsLeaseBasedRecovery.Returns(true);
        var sweeps = 0;
        manager.RecoverStaleTickersAsync(Arg.Any<CancellationToken>()).Returns(_ =>
            Task.FromResult(Interlocked.Increment(ref sweeps) == 1
                ? new StaleTickerRecoveryResult { RestartedTimeTickers = 1 }
                : new StaleTickerRecoveryResult()));
        var notifier = Substitute.For<ITickerQFailureNotifier>();
        notifier.When(n => n.Notify(Arg.Any<TickerFailureEvent>()))
            .Do(_ => throw new InvalidOperationException("custom notifier failed"));
        var logger = new CapturingLogger();

        using var service = new TickerQStaleJobRecoveryBackgroundService(manager,
            new SchedulerOptionsBuilder { LeaseRenewalInterval = TimeSpan.FromMilliseconds(25) },
            logger, notifier, LicenseTestState.Active());
        await service.StartAsync(CancellationToken.None);
        try
        {
            await logger.RecoveryLogged.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }

        notifier.Received().Notify(Arg.Is<TickerFailureEvent>(e => e.Kind == "stale_recovered"));
        Assert.Contains(logger.Messages, m => m.Contains("notifier failed", StringComparison.Ordinal));
        Assert.DoesNotContain(logger.Messages, m => m.Contains("stale-job sweep failed", StringComparison.Ordinal));
    }

    private sealed class CapturingLogger : ILogger<TickerQStaleJobRecoveryBackgroundService>
    {
        private readonly object _gate = new();
        private readonly List<string> _messages = [];
        public TaskCompletionSource RecoveryLogged { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public IReadOnlyList<string> Messages { get { lock (_gate) return _messages.ToArray(); } }

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var message = formatter(state, exception);
            lock (_gate) _messages.Add(message);
            if (message.Contains("Stale-job watchdog recovered", StringComparison.Ordinal))
                RecoveryLogged.TrySetResult();
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose() { }
        }
    }
}
