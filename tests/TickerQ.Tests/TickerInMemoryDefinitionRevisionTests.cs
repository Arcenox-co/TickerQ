using System.Collections.Concurrent;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using TickerQ.Provider;
using TickerQ.Utilities;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Models;

namespace TickerQ.Tests;

public sealed class TickerInMemoryDefinitionRevisionTests : IDisposable
{
    private sealed class TestTime : TimeTickerEntity<TestTime> { }
    private sealed class TestCron : CronTickerEntity { }

    private readonly DateTime _now = new(2026, 7, 30, 12, 0, 0, DateTimeKind.Utc);
    private readonly TickerInMemoryPersistenceProvider<TestTime, TestCron> _provider;
    private readonly ConcurrentDictionary<Guid, TestCron> _crons;
    private readonly ConcurrentDictionary<Guid, CronTickerOccurrenceEntity<TestCron>> _occurrences;
    private readonly ConcurrentDictionary<Guid, TickerResultEnvelope> _results;
    private readonly string _owner;

    public TickerInMemoryDefinitionRevisionTests()
    {
        TickerInMemoryPersistenceProvider<TestTime, TestCron>.ResetActivationEpochForTests();
        var clock = Substitute.For<ITickerClock>();
        clock.UtcNow.Returns(_now);
        var scheduler = new SchedulerOptionsBuilder { NodeIdentifier = "revision-memory" };
        _owner = scheduler.ExecutionOwnerId;
        var services = new ServiceCollection()
            .AddSingleton(clock)
            .AddSingleton(scheduler)
            .BuildServiceProvider();
        _provider = new TickerInMemoryPersistenceProvider<TestTime, TestCron>(services);
        _crons = _provider.CronTickersForTests;
        _occurrences = _provider.CronOccurrencesForTests;
        _results = _provider.CronOccurrenceResultsForTests;
        _crons.Clear();
        _occurrences.Clear();
        _results.Clear();
    }

    public void Dispose()
    {
        TickerInMemoryPersistenceProvider<TestTime, TestCron>.ResetActivationEpochForTests();
        _crons.Clear();
        _occurrences.Clear();
        _results.Clear();
    }

    [Fact]
    public async Task DirectOccurrenceInsert_StampsAuthoritativePositiveRevision()
    {
        await _provider.MigrateDefinedCronTickers(
            new DefinedCronSeedManifest("memory-revision", [new DefinedCronTickerSeed("stamp", "*/5 * * * *")]),
            CancellationToken.None);
        var cron = Assert.Single(await _provider.GetCronTickers(x => x.Function == "stamp", CancellationToken.None));
        var occurrence = Pending(cron.Id, definitionRevision: 0);

        await _provider.InsertCronTickerOccurrences([occurrence], CancellationToken.None);

        var stored = Assert.Single(await _provider.GetAllCronTickerOccurrences(x => x.Id == occurrence.Id, CancellationToken.None));
        Assert.Equal(cron.DefinitionRevision, stored.DefinitionRevision);
        Assert.True(stored.DefinitionRevision > 0);
    }

    [Fact]
    public async Task DefinitionChange_QuarantinesPendingOccurrence_AndPreservesDurableResult()
    {
        await _provider.MigrateDefinedCronTickers(
            new DefinedCronSeedManifest("memory-revision", [new DefinedCronTickerSeed("quarantine", "*/5 * * * *")]),
            CancellationToken.None);
        var cron = Assert.Single(await _provider.GetCronTickers(x => x.Function == "quarantine", CancellationToken.None));
        var occurrence = Pending(cron.Id, cron.DefinitionRevision);
        await _provider.InsertCronTickerOccurrences([occurrence], CancellationToken.None);
        _results[occurrence.Id] = new TickerResultEnvelope([1], 1, "application/octet-stream");

        await _provider.MigrateDefinedCronTickers(
            new DefinedCronSeedManifest("memory-revision", [new DefinedCronTickerSeed("quarantine", "*/9 * * * *")]),
            CancellationToken.None);

        var stored = Assert.Single(await _provider.GetAllCronTickerOccurrences(x => x.Id == occurrence.Id, CancellationToken.None));
        Assert.Equal(TickerStatus.Skipped, stored.Status);
        Assert.Contains("Quarantined", stored.SkippedReason, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(await _provider.GetCronTickerOccurrenceResultAsync(occurrence.Id, CancellationToken.None));
    }

    [Fact]
    public async Task StaleOccurrence_IsRejectedByDiscoveryQueueImmediateTransitionAndTimedOutRecovery()
    {
        var cron = new TestCron
        {
            Id = Guid.NewGuid(), Function = "fenced", Expression = "*/5 * * * *", DefinitionRevision = 2,
            Request = [], CreatedAt = _now, UpdatedAt = _now, IsEnabled = true
        };
        await _provider.InsertCronTickers([cron], CancellationToken.None);

        var discovery = Pending(cron.Id, 1, _now.AddSeconds(1));
        await _provider.InsertCronTickerOccurrences([discovery], CancellationToken.None);
        _occurrences[discovery.Id].DefinitionRevision = 1; // simulate an old writer persisted before publication
        Assert.Null(await _provider.GetEarliestAvailableCronOccurrence([cron.Id], CancellationToken.None));
        Assert.Equal(TickerStatus.Skipped, _occurrences[discovery.Id].Status);

        var queued = Pending(cron.Id, 1, _now.AddMinutes(1));
        queued.Status = TickerStatus.Queued;
        queued.LockHolder = _owner;
        queued.AcquisitionToken = Guid.NewGuid();
        queued.LeaseUntil = _now.AddMinutes(5);
        _occurrences[queued.Id] = queued;
        var transition = await _provider.TransitionQueuedCronOccurrencesToInProgressAsync(
            [new AcquisitionLease(queued.Id, queued.AcquisitionToken)], CancellationToken.None);
        Assert.Empty(transition);
        Assert.Equal(TickerStatus.Queued, _occurrences[queued.Id].Status);
        Assert.Equal(queued.AcquisitionToken, _occurrences[queued.Id].AcquisitionToken);

        var immediate = Pending(cron.Id, 1, _now.AddMinutes(2));
        _occurrences[immediate.Id] = immediate;
        Assert.Empty(await _provider.AcquireImmediateCronOccurrencesAsync([immediate.Id], CancellationToken.None));

        var timedOut = Pending(cron.Id, 1, _now.AddMinutes(-2));
        _occurrences[timedOut.Id] = timedOut;
        Assert.Empty(await ToListAsync(_provider.QueueTimedOutCronTickerOccurrences(CancellationToken.None)));

        var staleContext = new InternalManagerContext(cron.Id)
        {
            FunctionName = cron.Function, Expression = cron.Expression, DefinitionRevision = 1
        };
        Assert.Empty(await ToListAsync(_provider.QueueCronTickerOccurrences(
            (_now.AddMinutes(3), [staleContext]), CancellationToken.None)));

        Assert.Equal(TickerStatus.Skipped, _occurrences[immediate.Id].Status);
        Assert.Equal(TickerStatus.Skipped, _occurrences[timedOut.Id].Status);
        Assert.Contains("revision", _occurrences[timedOut.Id].SkippedReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ImmediateAcquisition_RechecksParentRevisionInsideGraphLock()
    {
        var cron = new TestCron
        {
            Id = Guid.NewGuid(), Function = "racing", Expression = "*/5 * * * *", DefinitionRevision = 1,
            Request = [], CreatedAt = _now, UpdatedAt = _now, IsEnabled = true
        };
        await _provider.InsertCronTickers([cron], CancellationToken.None);
        var occurrence = Pending(cron.Id, 1);
        await _provider.InsertCronTickerOccurrences([occurrence], CancellationToken.None);

        var providerType = typeof(TickerInMemoryPersistenceProvider<TestTime, TestCron>);
        var hook = providerType.GetField("BeforeCronOccurrenceMutationLockHook",
            BindingFlags.Static | BindingFlags.NonPublic)!;
        hook.SetValue(null, (Action<Guid>)(id =>
        {
            if (id != occurrence.Id) return;
            var replacement = new TestCron
            {
                Id = cron.Id, Function = cron.Function, Expression = "*/9 * * * *", DefinitionRevision = 2,
                Request = [], CreatedAt = cron.CreatedAt, UpdatedAt = _now, IsEnabled = true
            };
            _crons[cron.Id] = replacement;
            hook.SetValue(null, null);
        }));

        try
        {
            Assert.Empty(await _provider.AcquireImmediateCronOccurrencesAsync(
                [occurrence.Id], CancellationToken.None));
            Assert.Equal(TickerStatus.Idle, _occurrences[occurrence.Id].Status);
        }
        finally
        {
            hook.SetValue(null, null);
        }
    }

    [Fact]
    public async Task RevisionPublication_WinsAtomicBoundaryAgainstQueuedTransition()
    {
        var cron = await CreateRevisionedCron("transition-race");
        var queued = Pending(cron.Id, cron.DefinitionRevision);
        queued.Status = TickerStatus.Queued;
        queued.LockHolder = _owner;
        queued.AcquisitionToken = Guid.NewGuid();
        queued.LeaseUntil = _now.AddMinutes(5);
        _occurrences[queued.Id] = queued;

        await WithPublicationAtMutationBoundary(queued.Id, cron.Function, async () =>
        {
            var winners = await _provider.TransitionQueuedCronOccurrencesToInProgressAsync(
                [new AcquisitionLease(queued.Id, queued.AcquisitionToken)], CancellationToken.None);
            Assert.Empty(winners);
        });

        Assert.Equal(TickerStatus.Queued, _occurrences[queued.Id].Status);
        Assert.Equal(queued.AcquisitionToken, _occurrences[queued.Id].AcquisitionToken);
    }

    [Fact]
    public async Task RevisionPublication_WinsAtomicBoundaryAgainstTimedOutRecovery()
    {
        var cron = await CreateRevisionedCron("recovery-race");
        var timedOut = Pending(cron.Id, cron.DefinitionRevision, _now.AddMinutes(-5));
        _occurrences[timedOut.Id] = timedOut;

        await WithPublicationAtMutationBoundary(timedOut.Id, cron.Function, async () =>
            Assert.Empty(await ToListAsync(_provider.QueueTimedOutCronTickerOccurrences(CancellationToken.None))));

        Assert.NotEqual(TickerStatus.InProgress, _occurrences[timedOut.Id].Status);
    }

    [Fact]
    public async Task RevisionPublication_WinsAtomicBoundaryAgainstNewQueueAdmission()
    {
        var cron = await CreateRevisionedCron("queue-race");
        var next = Pending(cron.Id, cron.DefinitionRevision, _now.AddMinutes(1));
        var context = new InternalManagerContext(cron.Id)
        {
            FunctionName = cron.Function,
            Expression = cron.Expression,
            DefinitionRevision = cron.DefinitionRevision,
            NextCronOccurrence = new NextCronOccurrence(next.Id, _now)
        };

        await WithPublicationAtMutationBoundary(next.Id, cron.Function, async () =>
            Assert.Empty(await ToListAsync(_provider.QueueCronTickerOccurrences(
                (_now.AddMinutes(1), [context]), CancellationToken.None))));

        Assert.False(_occurrences.ContainsKey(next.Id));
    }

    [Fact]
    public async Task ActivationTransition_DoesNotBlockTruthfulTerminalPersistenceForAcquiredCurrentRevision()
    {
        var cron = await CreateRevisionedCron("terminal-during-activation");
        var occurrence = Pending(cron.Id, cron.DefinitionRevision);
        await _provider.InsertCronTickerOccurrences([occurrence], CancellationToken.None);
        var acquired = Assert.Single(await _provider.AcquireImmediateCronOccurrencesAsync(
            [occurrence.Id], CancellationToken.None));
        await _provider.BeginReconciliationActivationEpochAsync(2, CancellationToken.None);
        var result = new TickerResultEnvelope([7], TickerResultEnvelope.CurrentVersion, "application/octet-stream");
        var completion = new InternalFunctionContext()
            .SetProperty(x => x.TickerId, occurrence.Id)
            .SetProperty(x => x.Type, TickerType.CronTickerOccurrence)
            .SetProperty(x => x.AcquisitionToken, acquired.AcquisitionToken)
            .SetProperty(x => x.Status, TickerStatus.Done)
            .SetProperty(x => x.ResultEnvelope, result);

        Assert.True(await _provider.CommitSuccessfulTickerAsync(completion, CancellationToken.None));
        Assert.Equal(TickerStatus.Done, _occurrences[occurrence.Id].Status);
        Assert.Equal(result.Payload.ToArray(),
            (await _provider.GetCronTickerOccurrenceResultAsync(occurrence.Id)).Payload.ToArray());
    }

    private async Task<TestCron> CreateRevisionedCron(string function)
    {
        await _provider.MigrateDefinedCronTickers(
            new DefinedCronSeedManifest("memory-revision", [new DefinedCronTickerSeed(function, "*/5 * * * *")]),
            CancellationToken.None);
        return Assert.Single(await _provider.GetCronTickers(x => x.Function == function, CancellationToken.None));
    }

    private async Task WithPublicationAtMutationBoundary(Guid occurrenceId, string function, Func<Task> action)
    {
        var providerType = typeof(TickerInMemoryPersistenceProvider<TestTime, TestCron>);
        var hook = providerType.GetField("BeforeCronOccurrenceMutationLockHook",
            BindingFlags.Static | BindingFlags.NonPublic)!;
        hook.SetValue(null, (Action<Guid>)(id =>
        {
            if (id != occurrenceId) return;
            hook.SetValue(null, null);
            _provider.MigrateDefinedCronTickers(
                new DefinedCronSeedManifest("memory-revision",
                    [new DefinedCronTickerSeed(function, "*/9 * * * *")]),
                CancellationToken.None).GetAwaiter().GetResult();
        }));
        try
        {
            await action();
        }
        finally
        {
            hook.SetValue(null, null);
        }
    }

    private CronTickerOccurrenceEntity<TestCron> Pending(Guid cronId, long definitionRevision, DateTime? execution = null)
        => new()
        {
            Id = Guid.NewGuid(), CronTickerId = cronId, DefinitionRevision = definitionRevision,
            ExecutionTime = execution ?? _now.AddMinutes(5), Status = TickerStatus.Idle,
            CreatedAt = _now, UpdatedAt = _now
        };

    private static async Task<List<T>> ToListAsync<T>(IAsyncEnumerable<T> source)
    {
        var result = new List<T>();
        await foreach (var item in source) result.Add(item);
        return result;
    }
}
