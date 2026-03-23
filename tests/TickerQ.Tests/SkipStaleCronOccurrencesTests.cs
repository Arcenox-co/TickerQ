using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using TickerQ.Provider;
using TickerQ.Utilities;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Models;
using Xunit;

namespace TickerQ.Tests;

public class SkipStaleCronOccurrencesTests : IAsyncLifetime
{
    private sealed class FakeTimeTicker : TimeTickerEntity<FakeTimeTicker> { }
    private sealed class FakeCronTicker : CronTickerEntity { }

    private readonly ITickerClock _clock;
    private readonly TickerInMemoryPersistenceProvider<FakeTimeTicker, FakeCronTicker> _provider;
    private readonly DateTime _now;
    private readonly List<Guid> _createdCronTickerIds = new();
    private readonly List<Guid> _createdOccurrenceIds = new();

    public SkipStaleCronOccurrencesTests()
    {
        _now = new DateTime(2025, 6, 15, 12, 0, 0, DateTimeKind.Utc);
        _clock = Substitute.For<ITickerClock>();
        _clock.UtcNow.Returns(_now);

        var services = new ServiceCollection();
        services.AddSingleton(_clock);
        services.AddSingleton(new SchedulerOptionsBuilder { NodeIdentifier = "test-node" });
        var serviceProvider = services.BuildServiceProvider();

        _provider = new TickerInMemoryPersistenceProvider<FakeTimeTicker, FakeCronTicker>(serviceProvider);
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        if (_createdOccurrenceIds.Count > 0)
            await _provider.RemoveCronTickerOccurrences(_createdOccurrenceIds.ToArray(), CancellationToken.None);
        if (_createdCronTickerIds.Count > 0)
            await _provider.RemoveCronTickers(_createdCronTickerIds.ToArray(), CancellationToken.None);
    }

    [Fact]
    public async Task SkipStaleCronOccurrences_SkipsPastDueIdleOccurrences()
    {
        var cronTickerId = Guid.NewGuid();
        await SetupCronTicker(cronTickerId);

        // Create an occurrence that is 10 seconds past-due (well beyond the 5s grace)
        var occurrenceId = await InsertOccurrence(cronTickerId, _now.AddSeconds(-10), TickerStatus.Idle);

        var skipped = await _provider.SkipStaleCronOccurrencesAsync(CancellationToken.None);

        Assert.Equal(1, skipped);

        // Verify it's now Skipped
        var all = await _provider.GetAllCronTickerOccurrences(x => x.Id == occurrenceId, CancellationToken.None);
        Assert.Single(all);
        Assert.Equal(TickerStatus.Skipped, all[0].Status);
        Assert.Equal("Missed: occurrence was pending when the application restarted", all[0].SkippedReason);
    }

    [Fact]
    public async Task SkipStaleCronOccurrences_SkipsPastDueQueuedOccurrences()
    {
        var cronTickerId = Guid.NewGuid();
        await SetupCronTicker(cronTickerId);

        // Create a Queued occurrence that is 10 seconds past-due
        var occurrenceId = await InsertOccurrence(cronTickerId, _now.AddSeconds(-10), TickerStatus.Queued);

        var skipped = await _provider.SkipStaleCronOccurrencesAsync(CancellationToken.None);

        Assert.Equal(1, skipped);

        var all = await _provider.GetAllCronTickerOccurrences(x => x.Id == occurrenceId, CancellationToken.None);
        Assert.Single(all);
        Assert.Equal(TickerStatus.Skipped, all[0].Status);
    }

    [Fact]
    public async Task SkipStaleCronOccurrences_DoesNotSkipFutureOccurrences()
    {
        var cronTickerId = Guid.NewGuid();
        await SetupCronTicker(cronTickerId);

        // Create a future occurrence (30 seconds from now)
        var occurrenceId = await InsertOccurrence(cronTickerId, _now.AddSeconds(30), TickerStatus.Idle);

        var skipped = await _provider.SkipStaleCronOccurrencesAsync(CancellationToken.None);

        Assert.Equal(0, skipped);

        var all = await _provider.GetAllCronTickerOccurrences(x => x.Id == occurrenceId, CancellationToken.None);
        Assert.Single(all);
        Assert.Equal(TickerStatus.Idle, all[0].Status);
    }

    [Fact]
    public async Task SkipStaleCronOccurrences_DoesNotSkipRecentPastDueWithinGracePeriod()
    {
        var cronTickerId = Guid.NewGuid();
        await SetupCronTicker(cronTickerId);

        // Create an occurrence that is only 2 seconds past-due (within 5s grace period)
        var occurrenceId = await InsertOccurrence(cronTickerId, _now.AddSeconds(-2), TickerStatus.Idle);

        var skipped = await _provider.SkipStaleCronOccurrencesAsync(CancellationToken.None);

        Assert.Equal(0, skipped);

        var all = await _provider.GetAllCronTickerOccurrences(x => x.Id == occurrenceId, CancellationToken.None);
        Assert.Single(all);
        Assert.Equal(TickerStatus.Idle, all[0].Status);
    }

    [Fact]
    public async Task SkipStaleCronOccurrences_DoesNotAffectCompletedOccurrences()
    {
        var cronTickerId = Guid.NewGuid();
        await SetupCronTicker(cronTickerId);

        // Create a completed past-due occurrence
        var occurrenceId = await InsertOccurrence(cronTickerId, _now.AddSeconds(-60), TickerStatus.Done);

        var skipped = await _provider.SkipStaleCronOccurrencesAsync(CancellationToken.None);

        Assert.Equal(0, skipped);

        var all = await _provider.GetAllCronTickerOccurrences(x => x.Id == occurrenceId, CancellationToken.None);
        Assert.Single(all);
        Assert.Equal(TickerStatus.Done, all[0].Status);
    }

    [Fact]
    public async Task SkipStaleCronOccurrences_DoesNotAffectInProgressOccurrences()
    {
        var cronTickerId = Guid.NewGuid();
        await SetupCronTicker(cronTickerId);

        // Create an InProgress past-due occurrence (being actively processed by another node)
        var occurrenceId = await InsertOccurrence(cronTickerId, _now.AddSeconds(-60), TickerStatus.InProgress);

        var skipped = await _provider.SkipStaleCronOccurrencesAsync(CancellationToken.None);

        Assert.Equal(0, skipped);

        var all = await _provider.GetAllCronTickerOccurrences(x => x.Id == occurrenceId, CancellationToken.None);
        Assert.Single(all);
        Assert.Equal(TickerStatus.InProgress, all[0].Status);
    }

    [Fact]
    public async Task SkipStaleCronOccurrences_IsIdempotent()
    {
        var cronTickerId = Guid.NewGuid();
        await SetupCronTicker(cronTickerId);

        await InsertOccurrence(cronTickerId, _now.AddSeconds(-10), TickerStatus.Idle);

        var first = await _provider.SkipStaleCronOccurrencesAsync(CancellationToken.None);
        var second = await _provider.SkipStaleCronOccurrencesAsync(CancellationToken.None);

        Assert.Equal(1, first);
        Assert.Equal(0, second); // Already Skipped, should not match again
    }

    [Fact]
    public async Task SkipStaleCronOccurrences_SkippedOccurrencesNotPickedUpByFallback()
    {
        var cronTickerId = Guid.NewGuid();
        await SetupCronTicker(cronTickerId);

        // Create a past-due Idle occurrence
        await InsertOccurrence(cronTickerId, _now.AddSeconds(-10), TickerStatus.Idle);

        // Skip stale occurrences
        await _provider.SkipStaleCronOccurrencesAsync(CancellationToken.None);

        // Fallback should find no timed-out occurrences
        var fallbackResults = await CollectAsync(
            _provider.QueueTimedOutCronTickerOccurrences(CancellationToken.None));

        Assert.Empty(fallbackResults);
    }

    private async Task SetupCronTicker(Guid cronTickerId)
    {
        var cronTicker = new FakeCronTicker
        {
            Id = cronTickerId,
            Function = "TestFunc",
            Expression = "* * * * *"
        };
        await _provider.InsertCronTickers(new[] { cronTicker }, CancellationToken.None);
        _createdCronTickerIds.Add(cronTickerId);
    }

    private async Task<Guid> InsertOccurrence(Guid cronTickerId, DateTime executionTime, TickerStatus status)
    {
        var id = Guid.NewGuid();
        var occurrence = new CronTickerOccurrenceEntity<FakeCronTicker>
        {
            Id = id,
            CronTickerId = cronTickerId,
            ExecutionTime = executionTime,
            Status = status,
            CreatedAt = _now.AddMinutes(-5),
            UpdatedAt = _now.AddMinutes(-5)
        };
        await _provider.InsertCronTickerOccurrences(new[] { occurrence }, CancellationToken.None);
        _createdOccurrenceIds.Add(id);
        return id;
    }

    private static async Task<List<T>> CollectAsync<T>(IAsyncEnumerable<T> source)
    {
        var list = new List<T>();
        await foreach (var item in source)
            list.Add(item);
        return list;
    }
}
