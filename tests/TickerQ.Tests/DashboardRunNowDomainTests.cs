using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using TickerQ.Dashboard;
using TickerQ.Dashboard.Infrastructure.Dashboard;
using TickerQ.Provider;
using TickerQ.Utilities;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Models;
using Xunit;

namespace TickerQ.Tests;

/// <summary>
/// Regression coverage for the dashboard run-now / bulk-retry contract at the
/// domain boundary — the atomic <see cref="TickerInMemoryPersistenceProvider{T,C}.AcquireTimeTickerOnDemandAsync"/>
/// transition every terminal job is revived through, and the repository
/// <c>RunTimeTickerOnDemandAsync</c> the bulk-retry endpoint sums into its
/// <c>Affected</c> count. Exercises the real in-memory provider (not a mock)
/// so the terminal-status eligibility, in-progress rejection, exactly-once
/// acquisition, and metadata-reset semantics are all covered end-to-end.
/// </summary>
public class DashboardRunNowDomainTests
{
    private sealed class FakeTimeTicker : TimeTickerEntity<FakeTimeTicker> { }
    private sealed class FakeCronTicker : CronTickerEntity { }

    private readonly ITickerClock _clock;
    private readonly DateTime _now;
    private readonly SchedulerOptionsBuilder _options;
    private readonly TickerInMemoryPersistenceProvider<FakeTimeTicker, FakeCronTicker> _provider;

    public DashboardRunNowDomainTests()
    {
        _now = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        _clock = Substitute.For<ITickerClock>();
        _clock.UtcNow.Returns(_now);

        _options = new SchedulerOptionsBuilder { NodeIdentifier = "run-now-node" };
        var services = new ServiceCollection();
        services.AddSingleton(_clock);
        services.AddSingleton(_options);
        _provider = new TickerInMemoryPersistenceProvider<FakeTimeTicker, FakeCronTicker>(
            services.BuildServiceProvider());
    }

    private async Task<FakeTimeTicker> SeedAsync(TickerStatus status, Action<FakeTimeTicker>? configure = null)
    {
        var ticker = new FakeTimeTicker
        {
            Id = Guid.NewGuid(),
            Function = "job-func",
            Status = status,
            ExecutionTime = _now.AddMinutes(-10),
            CreatedAt = _now.AddMinutes(-30),
            UpdatedAt = _now.AddMinutes(-20),
        };
        configure?.Invoke(ticker);
        await _provider.AddTimeTickers(new[] { ticker }, CancellationToken.None);
        return ticker;
    }

    [Theory]
    [InlineData(TickerStatus.Done)]
    [InlineData(TickerStatus.DueDone)]
    [InlineData(TickerStatus.Failed)]
    [InlineData(TickerStatus.Cancelled)]
    [InlineData(TickerStatus.Skipped)]
    public async Task RunNow_RevivesEveryTerminalStatus_ExactlyOnceIntoInProgress(TickerStatus terminal)
    {
        var seeded = await SeedAsync(terminal, t => t.AcquisitionToken = Guid.NewGuid());
        var originalToken = seeded.AcquisitionToken;

        var acquired = await _provider.AcquireTimeTickerOnDemandAsync(seeded.Id, _now, CancellationToken.None);

        Assert.NotNull(acquired);
        Assert.NotEqual(originalToken, acquired!.AcquisitionToken);

        var stored = await _provider.GetTimeTickerById(seeded.Id, CancellationToken.None);
        Assert.Equal(TickerStatus.InProgress, stored.Status);
        Assert.Equal(_options.ExecutionOwnerId, stored.LockHolder);
    }

    [Fact]
    public async Task RunNow_ResetsTerminalMetadataConsistentlyAtDomainBoundary()
    {
        var seeded = await SeedAsync(TickerStatus.Failed, t =>
        {
            t.RetryCount = 4;
            t.ExceptionMessage = "boom: secret-connection-string";
            t.SkippedReason = "was-skipped-earlier";
            t.StaleRestartCount = 3;
            t.ExecutedAt = _now.AddMinutes(-9);
            t.ElapsedTime = 12345;
        });

        var acquired = await _provider.AcquireTimeTickerOnDemandAsync(seeded.Id, _now, CancellationToken.None);
        Assert.NotNull(acquired);

        var stored = await _provider.GetTimeTickerById(seeded.Id, CancellationToken.None);
        Assert.Equal(TickerStatus.InProgress, stored.Status);
        Assert.Equal(0, stored.RetryCount);
        Assert.Null(stored.ExceptionMessage);
        Assert.Null(stored.SkippedReason);
        Assert.Equal(0, stored.StaleRestartCount);
        Assert.Null(stored.ExecutedAt);
        Assert.Equal(0, stored.ElapsedTime);
        Assert.NotNull(stored.AcquisitionToken);
    }

    [Fact]
    public async Task RunNow_RejectsInProgressJob_WithoutMutatingOwnershipOrToken()
    {
        var running = await SeedAsync(TickerStatus.InProgress, t =>
        {
            t.AcquisitionToken = Guid.NewGuid();
            t.LockHolder = "another-live-owner";
            t.LockedAt = _now.AddMinutes(-1);
        });
        var token = running.AcquisitionToken;

        var acquired = await _provider.AcquireTimeTickerOnDemandAsync(running.Id, _now, CancellationToken.None);

        Assert.Null(acquired);
        var stored = await _provider.GetTimeTickerById(running.Id, CancellationToken.None);
        Assert.Equal(TickerStatus.InProgress, stored.Status);
        Assert.Equal(token, stored.AcquisitionToken);
        Assert.Equal("another-live-owner", stored.LockHolder);
    }

    [Fact]
    public async Task RunNow_ConcurrentCalls_ProduceExactlyOneAcquisition()
    {
        var seeded = await SeedAsync(TickerStatus.Failed);

        var attempts = await Task.WhenAll(Enumerable.Range(0, 16).Select(_ =>
            Task.Run(() => _provider.AcquireTimeTickerOnDemandAsync(seeded.Id, _now, CancellationToken.None))));

        Assert.Equal(1, attempts.Count(a => a != null));

        var stored = await _provider.GetTimeTickerById(seeded.Id, CancellationToken.None);
        Assert.Equal(TickerStatus.InProgress, stored.Status);
    }

    [Fact]
    public async Task BulkRetry_AffectedCount_ReflectsOnlyRevivedRows()
    {
        // Mirror the endpoint's per-item loop: BulkRetryExecutions increments
        // Affected only when RunTimeTickerOnDemandAsync returns true. Two terminal
        // rows are revivable; the in-progress row must not be counted.
        var failed = await SeedAsync(TickerStatus.Failed);
        var done = await SeedAsync(TickerStatus.Done);
        var running = await SeedAsync(TickerStatus.InProgress, t => t.LockHolder = "live");

        var dispatcher = Substitute.For<ITickerQDispatcher>();
        var notifier = Substitute.For<ITickerQNotificationHubSender>();
        var repository = new TickerDashboardRepository<FakeTimeTicker, FakeCronTicker>(
            new TickerExecutionContext(),
            _provider,
            Substitute.For<ITickerQHostScheduler>(),
            notifier,
            new DashboardOptionsBuilder(),
            dispatcher);

        var affected = 0;
        foreach (var id in new[] { failed.Id, done.Id, running.Id })
            if (await repository.RunTimeTickerOnDemandAsync(id))
                affected++;

        Assert.Equal(2, affected);
        await dispatcher.Received(2).DispatchAsync(
            Arg.Any<InternalFunctionContext[]>(), CancellationToken.None);
        // The in-progress row was rejected at the domain boundary and left untouched.
        var stillRunning = await _provider.GetTimeTickerById(running.Id, CancellationToken.None);
        Assert.Equal(TickerStatus.InProgress, stillRunning.Status);
    }
}
