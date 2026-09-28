using MongoDB.Driver;
using TickerQ.Utilities;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Models;

namespace TickerQ.MongoDB.Tests;

[Collection("Mongo")]
public sealed class MongoDefinitionRevisionFencingTests : IAsyncLifetime
{
    private readonly MongoTestFixture _fixture;
    private DateTime Now => _fixture.FixedNow;

    public MongoDefinitionRevisionFencingTests(MongoTestFixture fixture) => _fixture = fixture;
    public Task InitializeAsync() => _fixture.DropAllAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task DirectOccurrenceInsert_StampsAuthoritativeRevision_AndQuarantinesExplicitStaleRevision()
    {
        var cron = Cron(3);
        await _fixture.Provider.InsertCronTickers([cron], CancellationToken.None);
        var unstamped = Occurrence(cron.Id, 0, Now.AddMinutes(1));
        var stale = Occurrence(cron.Id, 2, Now.AddMinutes(2));

        await _fixture.Provider.InsertCronTickerOccurrences([unstamped, stale], CancellationToken.None);

        var rows = await _fixture.Provider.GetAllCronTickerOccurrences(x => x.CronTickerId == cron.Id, CancellationToken.None);
        Assert.Equal(3, Assert.Single(rows, x => x.Id == unstamped.Id).DefinitionRevision);
        Assert.Equal(TickerStatus.Skipped, Assert.Single(rows, x => x.Id == stale.Id).Status);
    }

    [Fact]
    public async Task OldWriterQueuedOccurrence_CannotTransitionAfterSemanticPublication()
    {
        await _fixture.Provider.MigrateDefinedCronTickers(
            new DefinedCronSeedManifest("mongo-race", [new DefinedCronTickerSeed("old-writer", "*/5 * * * *")]),
            CancellationToken.None);
        var before = Assert.Single(await _fixture.Provider.GetCronTickers(x => x.Function == "old-writer", CancellationToken.None));
        var occurrence = Occurrence(before.Id, before.DefinitionRevision, Now.AddMinutes(1), TickerStatus.Queued);
        occurrence.LockHolder = _fixture.OwnerId;
        occurrence.LockedAt = Now;
        occurrence.LeaseUntil = Now.AddMinutes(5);
        occurrence.AcquisitionToken = Guid.NewGuid();
        await _fixture.CronTickerOccurrences.InsertOneAsync(occurrence);

        await _fixture.Provider.MigrateDefinedCronTickers(
            new DefinedCronSeedManifest("mongo-race", [new DefinedCronTickerSeed("old-writer", "*/9 * * * *")]),
            CancellationToken.None);

        Assert.Empty(await _fixture.Provider.TransitionQueuedCronOccurrencesToInProgressAsync(
            [new AcquisitionLease(occurrence.Id, occurrence.AcquisitionToken)], CancellationToken.None));
        var stored = await _fixture.CronTickerOccurrences.Find(x => x.Id == occurrence.Id).SingleAsync();
        Assert.Equal(TickerStatus.Queued, stored.Status);
    }

    [Fact]
    public async Task ExpiredStaleRevision_WithRestartPolicy_IsQuarantinedInsteadOfRetried()
    {
        var cron = Cron(2);
        cron.OnStale = StaleAction.Restart;
        await _fixture.CronTickers.InsertOneAsync(cron);
        var timedOut = Occurrence(cron.Id, 1, Now.AddMinutes(-5), TickerStatus.InProgress);
        timedOut.LockHolder = _fixture.OwnerId;
        timedOut.LockedAt = Now.AddMinutes(-5);
        timedOut.LeaseUntil = Now.AddMinutes(-1);
        timedOut.AcquisitionToken = Guid.NewGuid();
        await _fixture.CronTickerOccurrences.InsertOneAsync(timedOut);

        var recovery = await _fixture.Provider.RecoverStaleTickers(3, CancellationToken.None);

        Assert.Equal(0, recovery.RestartedCronOccurrences);
        var stored = await _fixture.CronTickerOccurrences.Find(x => x.Id == timedOut.Id).SingleAsync();
        Assert.Equal(TickerStatus.Skipped, stored.Status);
        Assert.Contains("revision", stored.SkippedReason, StringComparison.OrdinalIgnoreCase);
        Assert.Null(stored.LockHolder);
        Assert.Null(stored.AcquisitionToken);
    }

    [Fact]
    public async Task StaleOccurrence_IsRejectedByDiscoveryQueueImmediateAndTimedOutRecovery()
    {
        var cron = Cron(2);
        await _fixture.CronTickers.InsertOneAsync(cron);
        var discovery = Occurrence(cron.Id, 1, Now.AddMilliseconds(100));
        var queued = Occurrence(cron.Id, 1, Now.AddMinutes(1), TickerStatus.Queued);
        queued.LockHolder = _fixture.OwnerId;
        queued.AcquisitionToken = Guid.NewGuid();
        var immediate = Occurrence(cron.Id, 1, Now.AddMinutes(2));
        var timedOut = Occurrence(cron.Id, 1, Now.AddMinutes(-2));
        await _fixture.CronTickerOccurrences.InsertManyAsync([discovery, queued, immediate, timedOut]);

        Assert.Null(await _fixture.Provider.GetEarliestAvailableCronOccurrence([cron.Id], CancellationToken.None));
        Assert.Empty(await _fixture.Provider.AcquireImmediateCronOccurrencesAsync([immediate.Id], CancellationToken.None));
        Assert.Empty(await _fixture.Provider.TransitionQueuedCronOccurrencesToInProgressAsync(
            [new AcquisitionLease(queued.Id, queued.AcquisitionToken)], CancellationToken.None));
        Assert.Empty(await ToListAsync(_fixture.Provider.QueueTimedOutCronTickerOccurrences(CancellationToken.None)));
        Assert.Empty(await ToListAsync(_fixture.Provider.QueueCronTickerOccurrences(
            (Now.AddMinutes(3), [new InternalManagerContext(cron.Id)
            {
                FunctionName = cron.Function, Expression = cron.Expression, DefinitionRevision = 1
            }]), CancellationToken.None)));

        var recovered = await _fixture.CronTickerOccurrences.Find(x => x.Id == timedOut.Id).SingleAsync();
        Assert.Equal(TickerStatus.Skipped, recovered.Status);
        Assert.False(await _fixture.CronTickerOccurrences.Find(
            x => x.CronTickerId == cron.Id && x.Status == TickerStatus.InProgress).AnyAsync());
    }

    [Fact]
    public async Task QueuedOccurrencePublication_HoldsActivationAndParentRevisionFencesUntilInsertCommits()
    {
        var scope = new ReconciliationActivationScope("mongo-queued-publication-fence");
        var options = new SchedulerOptionsBuilder { NodeIdentifier = "mongo-queued-publication", ReconciliationEpoch = 1 };
        options.BindRuntimeActivationScope(scope.ApplicationNamespace, 1, schedulerEnabled: true);
        var provider = _fixture.NewProvider(options);
        var cron = Cron(1);
        cron.ApplicationNamespaceKey = options.RuntimePartition!.StorageKey;
        await _fixture.CronTickers.InsertOneAsync(cron);
        await provider.BeginReconciliationActivationEpochAsync(scope, 1);
        await provider.CommitReconciliationActivationEpochAsync(scope, 1);

        var admissionLocked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseInsert = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        provider.AfterRunnableAdmissionFenceForTestAsync = async _ =>
        {
            admissionLocked.TrySetResult();
            await releaseInsert.Task.WaitAsync(TimeSpan.FromSeconds(5));
        };

        try
        {
            var queue = ToListAsync(provider.QueueCronTickerOccurrences(
                (Now.AddMinutes(1), [new InternalManagerContext(cron.Id)
                {
                    FunctionName = cron.Function,
                    Expression = cron.Expression,
                    DefinitionRevision = cron.DefinitionRevision
                }]), CancellationToken.None));

            await admissionLocked.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var nextActivation = provider.BeginReconciliationActivationEpochAsync(scope, 2);
            await Task.Delay(100);
            Assert.False(nextActivation.IsCompleted);

            releaseInsert.TrySetResult();
            Assert.Single(await queue);
            Assert.Equal(ActivationEpochPhase.Activating, (await nextActivation).Phase);
        }
        finally
        {
            releaseInsert.TrySetResult();
            provider.AfterRunnableAdmissionFenceForTestAsync = null;
        }
    }

    [Fact]
    public async Task LiveLeasedStalePending_IsPreservedUntilExpiry_ThenTimedOutRecoveryQuarantinesIt()
    {
        var cron = Cron(2);
        await _fixture.CronTickers.InsertOneAsync(cron);
        var stale = Occurrence(cron.Id, 1, Now.AddMinutes(-2), TickerStatus.Queued);
        stale.LockHolder = "other-node";
        stale.LockedAt = Now;
        stale.LeaseUntil = Now.AddMinutes(1);
        stale.AcquisitionToken = Guid.NewGuid();
        await _fixture.CronTickerOccurrences.InsertOneAsync(stale);

        Assert.Empty(await ToListAsync(_fixture.Provider.QueueTimedOutCronTickerOccurrences(CancellationToken.None)));
        Assert.Equal(TickerStatus.Queued,
            (await _fixture.CronTickerOccurrences.Find(x => x.Id == stale.Id).SingleAsync()).Status);

        await _fixture.CronTickerOccurrences.UpdateOneAsync(x => x.Id == stale.Id,
            Builders<CronTickerOccurrenceEntity<CronTickerEntity>>.Update
                .Set(x => x.LeaseUntil, Now.AddSeconds(-1))
                .Set(x => x.LockHolder, (string?)null)
                .Set(x => x.AcquisitionToken, (Guid?)null));

        Assert.Empty(await ToListAsync(_fixture.Provider.QueueTimedOutCronTickerOccurrences(CancellationToken.None)));
        Assert.Equal(TickerStatus.Skipped,
            (await _fixture.CronTickerOccurrences.Find(x => x.Id == stale.Id).SingleAsync()).Status);
    }

    [Fact]
    public async Task TimedOutAcquisition_HoldsActivationAndParentRevisionFencesUntilCommit()
    {
        var scope = new ReconciliationActivationScope("mongo-timeout-acquisition-fence");
        var options = new SchedulerOptionsBuilder { NodeIdentifier = "mongo-timeout-acquisition", ReconciliationEpoch = 1 };
        options.BindRuntimeActivationScope(scope.ApplicationNamespace, 1, schedulerEnabled: true);
        var provider = _fixture.NewProvider(options);
        var cron = Cron(1);
        cron.ApplicationNamespaceKey = options.RuntimePartition!.StorageKey;
        await _fixture.CronTickers.InsertOneAsync(cron);
        var occurrence = Occurrence(cron.Id, cron.DefinitionRevision, Now.AddMinutes(-2));
        occurrence.ApplicationNamespaceKey = options.RuntimePartition.StorageKey;
        await _fixture.CronTickerOccurrences.InsertOneAsync(occurrence);
        await provider.BeginReconciliationActivationEpochAsync(scope, 1);
        await provider.CommitReconciliationActivationEpochAsync(scope, 1);

        var admissionLocked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseAcquire = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        provider.AfterRunnableAdmissionFenceForTestAsync = async _ =>
        {
            admissionLocked.TrySetResult();
            await releaseAcquire.Task.WaitAsync(TimeSpan.FromSeconds(5));
        };

        try
        {
            var acquire = ToListAsync(provider.QueueTimedOutCronTickerOccurrences(CancellationToken.None));
            await admissionLocked.Task.WaitAsync(TimeSpan.FromSeconds(1));
            var nextActivation = provider.BeginReconciliationActivationEpochAsync(scope, 2);
            await Task.Delay(100);
            Assert.False(nextActivation.IsCompleted);

            releaseAcquire.TrySetResult();
            Assert.Single(await acquire);
            Assert.Equal(ActivationEpochPhase.Activating, (await nextActivation).Phase);
        }
        finally
        {
            releaseAcquire.TrySetResult();
            provider.AfterRunnableAdmissionFenceForTestAsync = null;
        }
    }

    [Fact]
    public async Task StaleRestart_HoldsActivationAndParentRevisionFencesUntilCommit()
    {
        var scope = new ReconciliationActivationScope("mongo-stale-restart-fence");
        var options = new SchedulerOptionsBuilder { NodeIdentifier = "mongo-stale-restart", ReconciliationEpoch = 1 };
        options.BindRuntimeActivationScope(scope.ApplicationNamespace, 1, schedulerEnabled: true);
        var provider = _fixture.NewProvider(options);
        var cron = Cron(1);
        cron.ApplicationNamespaceKey = options.RuntimePartition!.StorageKey;
        cron.OnStale = StaleAction.Restart;
        await _fixture.CronTickers.InsertOneAsync(cron);
        var occurrence = Occurrence(cron.Id, cron.DefinitionRevision, Now.AddMinutes(-5), TickerStatus.InProgress);
        occurrence.ApplicationNamespaceKey = options.RuntimePartition.StorageKey;
        occurrence.LockHolder = "dead-node";
        occurrence.LockedAt = Now.AddMinutes(-5);
        occurrence.LeaseUntil = Now.AddMinutes(-1);
        occurrence.AcquisitionToken = Guid.NewGuid();
        await _fixture.CronTickerOccurrences.InsertOneAsync(occurrence);
        await provider.BeginReconciliationActivationEpochAsync(scope, 1);
        await provider.CommitReconciliationActivationEpochAsync(scope, 1);

        var admissionLocked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseRecovery = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        provider.BeforeStaleRecoveryTransactionCommitForTestAsync = async _ =>
        {
            admissionLocked.TrySetResult();
            await releaseRecovery.Task.WaitAsync(TimeSpan.FromSeconds(5));
        };

        try
        {
            var recovery = provider.RecoverStaleTickers(3, CancellationToken.None);
            await admissionLocked.Task.WaitAsync(TimeSpan.FromSeconds(1));
            var nextActivation = provider.BeginReconciliationActivationEpochAsync(scope, 2);
            await Task.Delay(100);
            Assert.False(nextActivation.IsCompleted);

            releaseRecovery.TrySetResult();
            Assert.Equal(1, (await recovery).RestartedCronOccurrences);
            Assert.Equal(ActivationEpochPhase.Activating, (await nextActivation).Phase);
        }
        finally
        {
            releaseRecovery.TrySetResult();
            provider.BeforeStaleRecoveryTransactionCommitForTestAsync = null;
        }
    }

    private CronTickerEntity Cron(long revision) => new()
    {
        Id = Guid.NewGuid(), Function = "cron-" + Guid.NewGuid().ToString("N"), Expression = "*/5 * * * *",
        DefinitionRevision = revision, Request = [], IsEnabled = true, CreatedAt = Now, UpdatedAt = Now
    };

    private CronTickerOccurrenceEntity<CronTickerEntity> Occurrence(
        Guid cronId, long revision, DateTime execution, TickerStatus status = TickerStatus.Idle) => new()
    {
        Id = Guid.NewGuid(), CronTickerId = cronId, DefinitionRevision = revision,
        ExecutionTime = execution, Status = status, CreatedAt = Now, UpdatedAt = Now
    };

    private static async Task<List<T>> ToListAsync<T>(IAsyncEnumerable<T> source)
    {
        var result = new List<T>();
        await foreach (var item in source) result.Add(item);
        return result;
    }
}
