using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NSubstitute;
using TickerQ.BackgroundServices;
using TickerQ.Utilities;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Interfaces.Managers;
using TickerQ.Utilities.Models;

namespace TickerQ.Tests;

// RED scaffold — proves the compatibility-safe defaults on ITickerPersistenceProvider
// fail closed: a provider that only implements the mandatory members (and inherits the
// Stale_Job_Recovery region) must NOT report lease-based recovery support and must NOT
// fake renewal/recovery success.
public class PersistenceReliabilityCapabilityTests
{
    public class FakeTimeTicker : TimeTickerEntity<FakeTimeTicker> { }
    public class FakeCronTicker : CronTickerEntity { }

    private static ITickerPersistenceProvider<FakeTimeTicker, FakeCronTicker> MinimalProvider()
        => new MinimalStubProvider();

    [Fact]
    public void Minimal_provider_inherits_unsupported_capability()
    {
        Assert.False(MinimalProvider().SupportsLeaseBasedRecovery);
    }

    [Fact]
    public async Task Unsupported_provider_logs_one_warning_and_never_enters_recovery_loops()
    {
        var manager = Substitute.For<IInternalTickerManager>();
        manager.SupportsLeaseBasedRecovery.Returns(false);
        var logger = new ListLogger<TickerQStaleJobRecoveryBackgroundService>();
        var notifier = Substitute.For<ITickerQFailureNotifier>();

        var service = new TickerQStaleJobRecoveryBackgroundService(
            manager, new SchedulerOptionsBuilder(), logger, notifier);

        await service.StartAsync(CancellationToken.None);
        await logger.WarningLogged.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await service.StopAsync(CancellationToken.None);

        Assert.Single(logger.Warnings);
        await manager.DidNotReceive().RenewActiveTickerLeasesAsync(
            Arg.Any<Guid[]>(), Arg.Any<Guid[]>(), Arg.Any<CancellationToken>());
        await manager.DidNotReceive().RecoverStaleTickersAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Minimal_provider_default_renewal_returns_zero_not_requested_count()
    {
        var provider = MinimalProvider();
        var ids = new[] { Guid.NewGuid(), Guid.NewGuid() };

        Assert.Equal(0, await provider.RenewTimeTickerLeases(ids, DateTime.UtcNow));
        Assert.Equal(0, await provider.RenewCronTickerOccurrenceLeases(ids, DateTime.UtcNow));
    }

    [Fact]
    public async Task Minimal_provider_default_still_held_returns_empty()
    {
        var provider = MinimalProvider();
        var ids = new[] { Guid.NewGuid(), Guid.NewGuid() };

        var held = await provider.GetStillHeldTickerIds(ids, ids);

        Assert.Empty(held);
    }

    [Fact]
    public async Task Minimal_provider_default_recovery_claims_no_success()
    {
        var provider = MinimalProvider();

        var result = await provider.RecoverStaleTickers(maxStaleRestarts: 5);

        Assert.Equal(0, result.Total);
    }

    // Retention seam: a provider that only implements the mandatory members must fail closed —
    // it reports no retention support and its default delete methods refuse to run rather than
    // silently no-op, so retention configured against an unsupported provider surfaces clearly.
    [Fact]
    public void Minimal_provider_does_not_support_retention()
    {
        Assert.False(MinimalProvider().SupportsRetention);
    }

    [Fact]
    public async Task Minimal_provider_default_retention_time_delete_throws()
    {
        var provider = MinimalProvider();
        var cutoffs = new RetentionCutoffs(DateTime.UtcNow, null, null, null);

        await Assert.ThrowsAsync<NotSupportedException>(
            () => provider.DeleteEligibleTimeTickerChainsAsync(cutoffs, batchSize: 100, RetentionCursor.Start));
    }

    [Fact]
    public async Task Minimal_provider_default_retention_cron_delete_throws()
    {
        var provider = MinimalProvider();
        var cutoffs = new RetentionCutoffs(DateTime.UtcNow, null, null, null);

        await Assert.ThrowsAsync<NotSupportedException>(
            () => provider.DeleteEligibleCronTickerOccurrencesAsync(cutoffs, batchSize: 100));
    }

    // Result-publication seam: a legacy provider that predates parent-result propagation (and thus
    // only implements the mandatory members) must still compile — this class compiling proves that —
    // and must fail closed: it reports no result support and its default result reads return null so
    // the runtime never trusts fabricated results.
    [Fact]
    public void Minimal_provider_does_not_support_result_publication()
    {
        Assert.False(MinimalProvider().SupportsResultPublication);
        Assert.False(MinimalProvider().SupportsAcknowledgedTerminalUpdates);
    }

    [Fact]
    public async Task Minimal_provider_default_result_reads_return_null()
    {
        var provider = MinimalProvider();

        Assert.Null(await provider.GetTimeTickerResultAsync(Guid.NewGuid()));
        Assert.Null(await provider.GetCronTickerOccurrenceResultAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task Advertising_result_support_without_atomic_override_fails_closed()
    {
        ITickerPersistenceProvider<FakeTimeTicker, FakeCronTicker> provider =
            new FalseAdvertisingResultProvider();

        Assert.True(provider.SupportsResultPublication);
        var error = await Assert.ThrowsAsync<NotSupportedException>(() =>
            provider.CommitSuccessfulTickerAsync(new InternalFunctionContext()));
        Assert.Contains(nameof(ITickerPersistenceProvider<FakeTimeTicker, FakeCronTicker>.CommitSuccessfulTickerAsync),
            error.Message);
    }

    // Captures warning-level log entries without pulling in a logging framework.
    private sealed class ListLogger<T> : ILogger<T>
    {
        public List<string> Warnings { get; } = new();
        public TaskCompletionSource WarningLogged { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel != LogLevel.Warning)
                return;

            Warnings.Add(formatter(state, exception));
            WarningLogged.TrySetResult();
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose() { }
        }
    }

    // Implements only the mandatory (non-default) interface members. Everything in the
    // Stale_Job_Recovery region is intentionally left to the interface defaults.
    private class MinimalStubProvider : ITickerPersistenceProvider<FakeTimeTicker, FakeCronTicker>
    {
        private static NotSupportedException NotUsed() => new("Not needed for capability tests");

        public IAsyncEnumerable<TimeTickerEntity> QueueTimeTickers(TimeTickerEntity[] timeTickers, CancellationToken cancellationToken = default) => throw NotUsed();
        public IAsyncEnumerable<TimeTickerEntity> QueueTimedOutTimeTickers(CancellationToken cancellationToken = default) => throw NotUsed();
        public Task ReleaseAcquiredTimeTickers(Guid[] timeTickerIds, CancellationToken cancellationToken = default) => throw NotUsed();
        public Task<TimeTickerEntity[]> GetEarliestTimeTickers(CancellationToken cancellationToken = default) => throw NotUsed();
        public Task<int> UpdateTimeTicker(InternalFunctionContext functionContext, CancellationToken cancellationToken = default) => throw NotUsed();
        public Task<byte[]> GetTimeTickerRequest(Guid id, CancellationToken cancellationToken) => throw NotUsed();
        public Task UpdateTimeTickersWithUnifiedContext(Guid[] timeTickerIds, InternalFunctionContext functionContext, CancellationToken cancellationToken = default) => throw NotUsed();
        public Task<TimeTickerEntity[]> AcquireImmediateTimeTickersAsync(Guid[] ids, CancellationToken cancellationToken = default) => throw NotUsed();

        public Task MigrateDefinedCronTickers(DefinedCronTickerSeed[] cronTickers, CancellationToken cancellationToken = default) => throw NotUsed();
        public Task<CronTickerEntity[]> GetAllCronTickerExpressions(CancellationToken cancellationToken) => throw NotUsed();
        public Task ReleaseDeadNodeTimeTickerResources(string instanceIdentifier, CancellationToken cancellationToken = default) => throw NotUsed();

        public Task<CronTickerOccurrenceEntity<FakeCronTicker>> GetEarliestAvailableCronOccurrence(Guid[] ids, CancellationToken cancellationToken = default) => throw NotUsed();
        public IAsyncEnumerable<CronTickerOccurrenceEntity<FakeCronTicker>> QueueCronTickerOccurrences((DateTime Key, InternalManagerContext[] Items) cronTickerOccurrences, CancellationToken cancellationToken = default) => throw NotUsed();
        public IAsyncEnumerable<CronTickerOccurrenceEntity<FakeCronTicker>> QueueTimedOutCronTickerOccurrences(CancellationToken cancellationToken = default) => throw NotUsed();
        public Task UpdateCronTickerOccurrence(InternalFunctionContext functionContext, CancellationToken cancellationToken = default) => throw NotUsed();
        public Task ReleaseAcquiredCronTickerOccurrences(Guid[] occurrenceIds, CancellationToken cancellationToken = default) => throw NotUsed();
        public Task<byte[]> GetCronTickerOccurrenceRequest(Guid tickerId, CancellationToken cancellationToken = default) => throw NotUsed();
        public Task UpdateCronTickerOccurrencesWithUnifiedContext(Guid[] timeTickerIds, InternalFunctionContext functionContext, CancellationToken cancellationToken = default) => throw NotUsed();
        public Task ReleaseDeadNodeOccurrenceResources(string instanceIdentifier, CancellationToken cancellationToken = default) => throw NotUsed();

        public ITickerQueryable<FakeTimeTicker> TimeTickersQuery() => throw NotUsed();
        public ITickerQueryable<FakeCronTicker> CronTickersQuery() => throw NotUsed();
        public ITickerQueryable<CronTickerOccurrenceEntity<FakeCronTicker>> CronTickerOccurrencesQuery() => throw NotUsed();

        public Task<FakeTimeTicker> GetTimeTickerById(Guid id, CancellationToken cancellationToken = default) => throw NotUsed();
        public Task<FakeTimeTicker[]> GetTimeTickers(Expression<Func<FakeTimeTicker, bool>> predicate, CancellationToken cancellationToken = default) => throw NotUsed();
        public Task<PaginationResult<FakeTimeTicker>> GetTimeTickersPaginated(Expression<Func<FakeTimeTicker, bool>> predicate, int pageNumber, int pageSize, CancellationToken cancellationToken = default) => throw NotUsed();
        public Task<int> AddTimeTickers(FakeTimeTicker[] tickers, CancellationToken cancellationToken = default) => throw NotUsed();
        public Task<int> UpdateTimeTickers(FakeTimeTicker[] tickers, CancellationToken cancellationToken = default) => throw NotUsed();
        public Task<int> RemoveTimeTickers(Guid[] tickerIds, CancellationToken cancellationToken = default) => throw NotUsed();

        public Task<FakeCronTicker> GetCronTickerById(Guid id, CancellationToken cancellationToken) => throw NotUsed();
        public Task<FakeCronTicker[]> GetCronTickers(Expression<Func<FakeCronTicker, bool>> predicate, CancellationToken cancellationToken) => throw NotUsed();
        public Task<PaginationResult<FakeCronTicker>> GetCronTickersPaginated(Expression<Func<FakeCronTicker, bool>> predicate, int pageNumber, int pageSize, CancellationToken cancellationToken = default) => throw NotUsed();
        public Task<int> InsertCronTickers(FakeCronTicker[] tickers, CancellationToken cancellationToken) => throw NotUsed();
        public Task<int> UpdateCronTickers(FakeCronTicker[] cronTicker, CancellationToken cancellationToken) => throw NotUsed();
        public Task<int> RemoveCronTickers(Guid[] cronTickerIds, CancellationToken cancellationToken) => throw NotUsed();

        public Task<CronTickerOccurrenceEntity<FakeCronTicker>[]> GetAllCronTickerOccurrences(Expression<Func<CronTickerOccurrenceEntity<FakeCronTicker>, bool>> predicate, CancellationToken cancellationToken = default) => throw NotUsed();
        public Task<PaginationResult<CronTickerOccurrenceEntity<FakeCronTicker>>> GetAllCronTickerOccurrencesPaginated(Expression<Func<CronTickerOccurrenceEntity<FakeCronTicker>, bool>> predicate, int pageNumber, int pageSize, CancellationToken cancellationToken = default) => throw NotUsed();
        public Task<int> InsertCronTickerOccurrences(CronTickerOccurrenceEntity<FakeCronTicker>[] cronTickerOccurrences, CancellationToken cancellationToken) => throw NotUsed();
        public Task<int> RemoveCronTickerOccurrences(Guid[] cronTickerOccurrences, CancellationToken cancellationToken) => throw NotUsed();
        public Task<CronTickerOccurrenceEntity<FakeCronTicker>[]> AcquireImmediateCronOccurrencesAsync(Guid[] occurrenceIds, CancellationToken cancellationToken = default) => throw NotUsed();
    }

    private sealed class FalseAdvertisingResultProvider : MinimalStubProvider,
        ITickerPersistenceProvider<FakeTimeTicker, FakeCronTicker>
    {
        bool ITickerPersistenceProvider<FakeTimeTicker, FakeCronTicker>.SupportsResultPublication => true;
    }
}
