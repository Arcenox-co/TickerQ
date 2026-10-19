using MongoDB.Bson;
using MongoDB.Driver;
using TickerQ.Utilities;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Models;

namespace TickerQ.MongoDB.Tests;

[Collection("Mongo")]
public sealed class MongoReconciliationActivationEpochTests
{
    private readonly MongoTestFixture _fixture;

    public MongoReconciliationActivationEpochTests(MongoTestFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Idle_publication_paths_fail_closed_during_next_activation_epoch()
    {
        await _fixture.DropAllAsync();
        var scope = new ReconciliationActivationScope("mongo-idle-publication");
        var options = new SchedulerOptionsBuilder { ReconciliationEpoch = 1 };
        options.BindRuntimeActivationScope(scope.ApplicationNamespace, 1, schedulerEnabled: true);
        var provider = _fixture.NewProvider(options);
        await provider.BeginReconciliationActivationEpochAsync(scope, 1);
        await provider.CommitReconciliationActivationEpochAsync(scope, 1);
        TimeTickerEntity New(string function) => new()
        {
            Id = Guid.NewGuid(), Function = function, Request = [], ExecutionTime = _fixture.FixedNow,
            Status = TickerStatus.Idle, CreatedAt = _fixture.FixedNow, UpdatedAt = _fixture.FixedNow
        };
        var acquiredTicker = New("release-acquired");
        var deadTicker = New("release-dead");
        var staleTicker = New("recover-stale");
        var replacementRoot = New("replace-old");
        Assert.Equal(4, await provider.AddTimeTickers(
            [acquiredTicker, deadTicker, staleTicker, replacementRoot]));
        var acquired = Assert.Single(await provider.AcquireImmediateTimeTickersAsync([acquiredTicker.Id]));
        await _fixture.TimeTickers.UpdateOneAsync(x => x.Id == deadTicker.Id,
            Builders<TimeTickerEntity>.Update
                .Set(x => x.Status, TickerStatus.InProgress)
                .Set(x => x.LockHolder, "dead-node")
                .Set(x => x.AcquisitionToken, Guid.NewGuid()));
        await _fixture.TimeTickers.UpdateOneAsync(x => x.Id == staleTicker.Id,
            Builders<TimeTickerEntity>.Update
                .Set(x => x.Status, TickerStatus.InProgress)
                .Set(x => x.LockHolder, "stale-node")
                .Set(x => x.AcquisitionToken, Guid.NewGuid())
                .Set(x => x.LeaseUntil, _fixture.FixedNow.AddMinutes(-1))
                .Set(x => x.OnStale, StaleAction.Restart));
        await provider.BeginReconciliationActivationEpochAsync(scope, 2);

        await provider.ReleaseAcquiredTimeTickers([acquiredTicker.Id]);
        await provider.ReleaseDeadNodeTimeTickerResources("dead-node");
        var recovery = await provider.RecoverStaleTickers(2);
        var replacement = New("replace-new");
        var replaced = await provider.ReplaceTimeTickerChainAsync(replacementRoot.Id, replacement);

        Assert.Equal(0, recovery.RestartedTimeTickers);
        Assert.Equal(0, replaced);
        var rows = await _fixture.TimeTickers.Find(Builders<TimeTickerEntity>.Filter.Empty)
            .ToListAsync();
        Assert.Equal(TickerStatus.InProgress, rows.Single(x => x.Id == acquiredTicker.Id).Status);
        Assert.Equal(acquired.AcquisitionToken, rows.Single(x => x.Id == acquiredTicker.Id).AcquisitionToken);
        Assert.Equal("dead-node", rows.Single(x => x.Id == deadTicker.Id).LockHolder);
        Assert.Equal("stale-node", rows.Single(x => x.Id == staleTicker.Id).LockHolder);
        Assert.Contains(rows, x => x.Id == replacementRoot.Id);
        Assert.DoesNotContain(rows, x => x.Id == replacement.Id);
    }

    [Fact]
    public async Task Activation_epochs_are_isolated_by_application_scope()
    {
        await _fixture.DropAllAsync();
        var scopeA = new ReconciliationActivationScope("mongo-app-a");
        var scopeB = new ReconciliationActivationScope("mongo-app-b");

        await _fixture.Provider.BeginReconciliationActivationEpochAsync(scopeA, 7);
        await _fixture.Provider.CommitReconciliationActivationEpochAsync(scopeA, 7);

        Assert.True((await _fixture.Provider.GetReconciliationActivationStateAsync(scopeA)).IsActivatedFor(7));
        Assert.Equal(ActivationEpochPhase.Pending,
            (await _fixture.Provider.GetReconciliationActivationStateAsync(scopeB)).Phase);
    }

    [Fact]
    public async Task Scoped_queries_begin_and_legacy_calls_cannot_redirect_bound_runtime_scope()
    {
        await _fixture.DropAllAsync();
        var scopeA = new ReconciliationActivationScope("mongo-runtime-a");
        var scopeB = new ReconciliationActivationScope("mongo-query-b");
        var options = new SchedulerOptionsBuilder { ReconciliationEpoch = 1 };
        options.BindRuntimeActivationScope(scopeA.ApplicationNamespace, 1, schedulerEnabled: true);
        ITickerPersistenceProvider<TimeTickerEntity, CronTickerEntity> provider = _fixture.NewProvider(options);
        var ticker = new TimeTickerEntity
        {
            Id = Guid.NewGuid(), Function = "scope-runtime", Request = [],
            ExecutionTime = _fixture.FixedNow, Status = TickerStatus.Idle,
            CreatedAt = _fixture.FixedNow, UpdatedAt = _fixture.FixedNow
        };

        await provider.BeginReconciliationActivationEpochAsync(scopeA, 1);
        await provider.CommitReconciliationActivationEpochAsync(scopeA, 1);
        Assert.Equal(1, await provider.AddTimeTickers([ticker]));
        _ = await provider.GetReconciliationActivationStateAsync(scopeB);
        await provider.BeginReconciliationActivationEpochAsync(scopeB, 1);
        await provider.BeginReconciliationActivationEpochAsync(1);

        Assert.Single(await provider.AcquireImmediateTimeTickersAsync([ticker.Id]));
        Assert.Equal(ActivationEpochPhase.Activating,
            (await provider.GetReconciliationActivationStateAsync(scopeB)).Phase);
        Assert.True((await provider.GetReconciliationActivationStateAsync(scopeA)).IsActivatedFor(1));
    }

    [Fact]
    public async Task Beginning_another_scope_first_cannot_redirect_configured_runtime_admission()
    {
        await _fixture.DropAllAsync();
        var appA = new ReconciliationActivationScope("mongo-runtime-a");
        var appB = new ReconciliationActivationScope("mongo-runtime-b");
        var options = new SchedulerOptionsBuilder { ReconciliationEpoch = 1 };
        options.BindRuntimeActivationScope(appA.ApplicationNamespace, 1, schedulerEnabled: true);
        ITickerPersistenceProvider<TimeTickerEntity, CronTickerEntity> provider = _fixture.NewProvider(options);
        var ticker = new TimeTickerEntity
        {
            Id = Guid.NewGuid(), Function = "configured-scope", Request = [],
            ExecutionTime = _fixture.FixedNow, Status = TickerStatus.Idle,
            CreatedAt = _fixture.FixedNow, UpdatedAt = _fixture.FixedNow
        };

        await provider.BeginReconciliationActivationEpochAsync(appB, 1);
        await provider.CommitReconciliationActivationEpochAsync(appB, 1);
        Assert.Equal(0, await provider.AddTimeTickers([ticker]));

        await provider.BeginReconciliationActivationEpochAsync(appA, 1);
        await provider.CommitReconciliationActivationEpochAsync(appA, 1);
        Assert.Equal(1, await provider.AddTimeTickers([ticker]));
        Assert.Single(await provider.AcquireImmediateTimeTickersAsync([ticker.Id]));
    }

    [Fact]
    public async Task Namespaced_queue_only_runtime_bypasses_all_activation_metadata()
    {
        await _fixture.DropAllAsync();
        var options = new SchedulerOptionsBuilder { ReconciliationEpoch = 3 };
        options.BindRuntimeActivationScope("mongo-queue-only", 3, schedulerEnabled: false);
        ITickerPersistenceProvider<TimeTickerEntity, CronTickerEntity> provider = _fixture.NewProvider(options);
        var ticker = new TimeTickerEntity
        {
            Id = Guid.NewGuid(), Function = "queue-only", Request = [],
            ExecutionTime = _fixture.FixedNow, Status = TickerStatus.Idle,
            CreatedAt = _fixture.FixedNow, UpdatedAt = _fixture.FixedNow
        };

        await provider.BeginReconciliationActivationEpochAsync(
            new ReconciliationActivationScope("mongo-queue-only"), 99);
        await provider.BeginReconciliationActivationEpochAsync(77);

        Assert.Equal(1, await provider.AddTimeTickers([ticker]));
        Assert.NotNull(await provider.GetTimeTickerById(ticker.Id));
    }

    [Fact]
    public async Task Scheduler_enabled_without_runtime_scope_fails_closed()
    {
        await _fixture.DropAllAsync();
        var options = new SchedulerOptionsBuilder { ReconciliationEpoch = 3 };
        Assert.Throws<InvalidOperationException>(() =>
            options.BindRuntimeActivationScope(null, 3, schedulerEnabled: true));
    }

    [Fact]
    public async Task Runtime_epoch_is_immutable_after_provider_construction()
    {
        await _fixture.DropAllAsync();
        var options = new SchedulerOptionsBuilder { ReconciliationEpoch = 5 };
        ITickerPersistenceProvider<TimeTickerEntity, CronTickerEntity> provider = _fixture.NewProvider(options);
        await provider.BeginReconciliationActivationEpochAsync(5);
        await provider.CommitReconciliationActivationEpochAsync(5);
        options.ReconciliationEpoch = 6;
        var ticker = new TimeTickerEntity
        {
            Id = Guid.NewGuid(), Function = "immutable-epoch", Request = [],
            ExecutionTime = _fixture.FixedNow, Status = TickerStatus.Idle,
            CreatedAt = _fixture.FixedNow, UpdatedAt = _fixture.FixedNow
        };

        Assert.Equal(1, await provider.AddTimeTickers([ticker]));
        Assert.Single(await provider.AcquireImmediateTimeTickersAsync([ticker.Id]));
    }

    [Fact]
    public async Task Fresh_begin_checkpoint_commit_and_restart_are_durable_and_idempotent()
    {
        await _fixture.DropAllAsync();
        Assert.True(_fixture.Provider.SupportsReconciliationActivationEpoch);
        Assert.True(_fixture.Provider.SupportsAuthoritativeCronReconciliation);
        Assert.Equal(ActivationEpochPhase.Pending,
            (await _fixture.Provider.GetReconciliationActivationStateAsync()).Phase);

        var begun = await _fixture.Provider.BeginReconciliationActivationEpochAsync(7);
        Assert.Equal(7, begun.Epoch);
        Assert.Equal(ActivationEpochPhase.Activating, begun.Phase);

        var checkpoint = await _fixture.Provider.AdvanceReconciliationCheckpointAsync(7, "01-bootstrap-complete");
        Assert.Equal("01-bootstrap-complete", checkpoint.Checkpoint);
        Assert.Equal("01-bootstrap-complete",
            (await _fixture.Provider.AdvanceReconciliationCheckpointAsync(7, "01-bootstrap-complete")).Checkpoint);

        ITickerPersistenceProvider<TimeTickerEntity, CronTickerEntity> restarted = _fixture.NewProvider();
        Assert.Equal("01-bootstrap-complete",
            (await restarted.BeginReconciliationActivationEpochAsync(7)).Checkpoint);
        Assert.True((await restarted.CommitReconciliationActivationEpochAsync(7)).IsActivatedFor(7));
        Assert.True((await restarted.CommitReconciliationActivationEpochAsync(7)).IsActivatedFor(7));
        Assert.True((await _fixture.Provider.GetReconciliationActivationStateAsync()).IsActivatedFor(7));
    }

    [Fact]
    public async Task Sixteen_concurrent_starters_and_checkpoint_writers_converge()
    {
        await _fixture.DropAllAsync();
        var providers = Enumerable.Range(0, 16)
            .Select(_ => (ITickerPersistenceProvider<TimeTickerEntity, CronTickerEntity>)_fixture.NewProvider())
            .ToArray();
        var starts = await Task.WhenAll(providers.Select(p => p.BeginReconciliationActivationEpochAsync(11)));
        Assert.All(starts, state =>
        {
            Assert.Equal(11, state.Epoch);
            Assert.Equal(ActivationEpochPhase.Activating, state.Phase);
        });

        var checkpoints = await Task.WhenAll(providers.Select(p =>
            p.AdvanceReconciliationCheckpointAsync(11, "02-chain-repair-complete")));
        Assert.All(checkpoints, state => Assert.Equal("02-chain-repair-complete", state.Checkpoint));

        var document = await Metadata.Find(FilterDefinition<BsonDocument>.Empty).SingleAsync();
        Assert.Equal(11, document["ActivationEpoch"].AsInt64);
        Assert.Equal((int)ActivationEpochPhase.Activating, document["ActivationPhase"].AsInt32);
        Assert.True(document["Version"].AsInt64 > 0);
    }

    [Fact]
    public async Task Delayed_checkpoint_cannot_regress_and_lower_epoch_cannot_overwrite_higher_epoch()
    {
        await _fixture.DropAllAsync();
        await _fixture.Provider.BeginReconciliationActivationEpochAsync(17);
        await _fixture.Provider.AdvanceReconciliationCheckpointAsync(17, "04-finalization-complete");
        Assert.Equal("04-finalization-complete",
            (await _fixture.Provider.AdvanceReconciliationCheckpointAsync(17, "01-bootstrap-complete")).Checkpoint);
        await _fixture.Provider.CommitReconciliationActivationEpochAsync(17);

        var lowerBegin = await _fixture.Provider.BeginReconciliationActivationEpochAsync(16);
        var lowerCheckpoint = await _fixture.Provider.AdvanceReconciliationCheckpointAsync(16, "07-provider-startup-actions-complete");
        var lowerCommit = await _fixture.Provider.CommitReconciliationActivationEpochAsync(16);
        Assert.All(new[] { lowerBegin, lowerCheckpoint, lowerCommit }, state => Assert.True(state.IsActivatedFor(17)));

        var higher = await _fixture.Provider.BeginReconciliationActivationEpochAsync(18);
        Assert.Equal(18, higher.Epoch);
        Assert.Equal(ActivationEpochPhase.Activating, higher.Phase);
        Assert.Null(higher.Checkpoint);
    }

    [Fact]
    public async Task Cancellation_before_commit_leaves_truthful_activating_state()
    {
        await _fixture.DropAllAsync();
        await _fixture.Provider.BeginReconciliationActivationEpochAsync(13);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            _fixture.Provider.CommitReconciliationActivationEpochAsync(13, cancellation.Token));
        Assert.Equal(ActivationEpochPhase.Activating,
            (await _fixture.Provider.GetReconciliationActivationStateAsync()).Phase);
    }

    [Fact]
    public async Task Publication_failure_rolls_back_activation_and_cancellation_after_commit_is_not_false_failure()
    {
        await _fixture.DropAllAsync();
        await _fixture.Provider.BeginReconciliationActivationEpochAsync(21);
        var provider = _fixture.ConcreteProvider;
        provider.BeforeActivationTransactionCommitForTestAsync = _ =>
            Task.FromException(new InvalidOperationException("publication failed"));

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            provider.CommitReconciliationActivationEpochAsync(21));
        Assert.Equal("publication failed", failure.Message);
        Assert.Equal(ActivationEpochPhase.Activating,
            (await _fixture.Provider.GetReconciliationActivationStateAsync()).Phase);

        provider.BeforeActivationTransactionCommitForTestAsync = null;
        using var cancellation = new CancellationTokenSource();
        provider.AfterActivationTransactionCommitForTestAsync = _ =>
        {
            cancellation.Cancel();
            return Task.CompletedTask;
        };
        var committed = await provider.CommitReconciliationActivationEpochAsync(21, cancellation.Token);
        Assert.True(committed.IsActivatedFor(21));
        provider.AfterActivationTransactionCommitForTestAsync = null;
    }

    [Fact]
    public async Task Protocol_aware_writer_and_commit_publish_only_pre_epoch_or_complete_state()
    {
        await _fixture.DropAllAsync();
        await _fixture.Provider.BeginReconciliationActivationEpochAsync(31);
        var marker = _fixture.Database.GetCollection<BsonDocument>("ticker_ActivationPublicationProbe");
        await marker.DeleteManyAsync(FilterDefinition<BsonDocument>.Empty);
        var writerHasLock = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseWriter = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var activationId = (await Metadata.Find(FilterDefinition<BsonDocument>.Empty).SingleAsync())["_id"];

        var oldProtocolAwareWriter = Task.Run(async () =>
        {
            using var session = await _fixture.Client.StartSessionAsync();
            session.StartTransaction(new TransactionOptions(
                readConcern: ReadConcern.Snapshot, writeConcern: WriteConcern.WMajority));
            await Metadata.UpdateOneAsync(session,
                Builders<BsonDocument>.Filter.Eq("_id", activationId),
                Builders<BsonDocument>.Update.Inc("ProtocolWriterLock", 1L));
            await marker.InsertOneAsync(session, new BsonDocument("_id", "complete-publication"));
            writerHasLock.SetResult();
            await releaseWriter.Task;
            await session.CommitTransactionAsync();
        });

        await writerHasLock.Task;
        var commit = _fixture.Provider.CommitReconciliationActivationEpochAsync(31);
        await Task.Delay(100);
        Assert.False(commit.IsCompleted);
        Assert.Equal(ActivationEpochPhase.Activating,
            (await _fixture.Provider.GetReconciliationActivationStateAsync()).Phase);
        Assert.Equal(0, await marker.CountDocumentsAsync(FilterDefinition<BsonDocument>.Empty));

        releaseWriter.SetResult();
        await oldProtocolAwareWriter;
        Assert.True((await commit).IsActivatedFor(31));
        Assert.Equal(1, await marker.CountDocumentsAsync(FilterDefinition<BsonDocument>.Empty));
    }

    [Fact]
    public async Task Runnable_admission_requires_exact_activated_configured_epoch()
    {
        await _fixture.DropAllAsync();
        var scope = new ReconciliationActivationScope("mongo-exact-runtime");
        var options = new SchedulerOptionsBuilder { NodeIdentifier = "mongo-exact", ReconciliationEpoch = 1 };
        options.BindRuntimeActivationScope(scope.ApplicationNamespace, 1, schedulerEnabled: true);
        var provider = _fixture.NewProvider(options);
        var occurrence = await SeedRunnableOccurrenceAsync(scope.ApplicationNamespace);

        await provider.BeginReconciliationActivationEpochAsync(scope, 2);
        Assert.Empty(await provider.AcquireImmediateCronOccurrencesAsync([occurrence.Id]));

        await provider.CommitReconciliationActivationEpochAsync(scope, 2);
        Assert.Empty(await provider.AcquireImmediateCronOccurrencesAsync([occurrence.Id]));

        await _fixture.DropAllAsync();
        occurrence = await SeedRunnableOccurrenceAsync(scope.ApplicationNamespace);
        await provider.BeginReconciliationActivationEpochAsync(scope, 1);
        await provider.CommitReconciliationActivationEpochAsync(scope, 1);
        Assert.Single(await provider.AcquireImmediateCronOccurrencesAsync([occurrence.Id]));

        var second = await SeedRunnableOccurrenceAsync(scope.ApplicationNamespace);
        Assert.Throws<InvalidOperationException>(() => options.ReconciliationEpoch = 2);
        Assert.Single(await provider.AcquireImmediateCronOccurrencesAsync([second.Id]));
    }

    [Fact]
    public async Task Startup_capability_manifest_and_public_cron_crud_are_exactly_admitted()
    {
        await _fixture.DropAllAsync();
        const string applicationNamespace = "mongo-startup-crud";
        var scope = new ReconciliationActivationScope(applicationNamespace);
        var options = new SchedulerOptionsBuilder { ReconciliationEpoch = 1 };
        options.BindRuntimeActivationScope(applicationNamespace, 1, schedulerEnabled: true);
        var provider = _fixture.NewProvider(options);
        await provider.BeginReconciliationActivationEpochAsync(scope, 1);
        var cron = new CronTickerEntity
        {
            Id = Guid.NewGuid(), Function = "mongo-public-cron", Expression = "*/5 * * * *",
            Request = [], CreatedAt = _fixture.FixedNow, UpdatedAt = _fixture.FixedNow
        };

        Assert.Equal(0, await provider.InsertCronTickers([cron], CancellationToken.None));
        using (StartupSeederAdmissionContext.Enter(new ReconciliationActivationScope("wrong").ScopeKey, 1))
            Assert.Equal(0, await provider.InsertCronTickers([cron], CancellationToken.None));
        using (StartupSeederAdmissionContext.Enter(scope.ScopeKey, 1))
            Assert.Equal(1, await provider.InsertCronTickers([cron], CancellationToken.None));
        Assert.Equal(1, cron.DefinitionRevision);

        var seed = new DefinedCronTickerSeed("mongo-manifest", "*/7 * * * *");
        await Assert.ThrowsAsync<InvalidOperationException>(() => provider.MigrateDefinedCronTickers(
            new DefinedCronSeedManifest("wrong", [seed]), CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() => provider.MigrateDefinedCronTickers(
            new DefinedCronSeedManifest(applicationNamespace, [seed]), CancellationToken.None));
        using (StartupSeederAdmissionContext.Enter(scope.ScopeKey, 1))
            await provider.MigrateDefinedCronTickers(
                new DefinedCronSeedManifest(applicationNamespace, [seed]), CancellationToken.None);

        await provider.CommitReconciliationActivationEpochAsync(scope, 1);
        var occurrence = new CronTickerOccurrenceEntity<CronTickerEntity>
        {
            Id = Guid.NewGuid(), CronTickerId = cron.Id, DefinitionRevision = 1,
            Status = TickerStatus.Idle, ExecutionTime = _fixture.FixedNow.AddMinutes(1),
            CreatedAt = _fixture.FixedNow, UpdatedAt = _fixture.FixedNow
        };
        Assert.Equal(1, await provider.InsertCronTickerOccurrences([occurrence], CancellationToken.None));
        cron.Expression = "*/9 * * * *";
        Assert.Equal(1, await provider.UpdateCronTickers([cron], CancellationToken.None));
        Assert.Equal(2, cron.DefinitionRevision);
        Assert.Equal(TickerStatus.Skipped,
            (await _fixture.CronTickerOccurrences.Find(x => x.Id == occurrence.Id).SingleAsync()).Status);

        await provider.BeginReconciliationActivationEpochAsync(scope, 2);
        Assert.Equal(0, await provider.RemoveCronTickers([cron.Id], CancellationToken.None));
    }

    [Fact]
    public async Task Activation_transition_and_acquisition_share_one_transactional_admission_boundary()
    {
        await _fixture.DropAllAsync();
        var scope = new ReconciliationActivationScope("mongo-admission-boundary");
        var options = new SchedulerOptionsBuilder { NodeIdentifier = "mongo-boundary", ReconciliationEpoch = 1 };
        options.BindRuntimeActivationScope(scope.ApplicationNamespace, 1, schedulerEnabled: true);
        var provider = _fixture.NewProvider(options);
        var occurrence = await SeedRunnableOccurrenceAsync(scope.ApplicationNamespace);
        await provider.BeginReconciliationActivationEpochAsync(scope, 1);
        await provider.CommitReconciliationActivationEpochAsync(scope, 1);

        var fenceReached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFence = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        provider.AfterRunnableAdmissionFenceForTestAsync = async _ =>
        {
            fenceReached.TrySetResult();
            await releaseFence.Task;
        };

        var acquire = provider.AcquireImmediateCronOccurrencesAsync([occurrence.Id]);
        await fenceReached.Task;
        var transition = provider.BeginReconciliationActivationEpochAsync(scope, 2);
        try
        {
            await Task.Delay(100);
            Assert.False(transition.IsCompleted);

            releaseFence.TrySetResult();
            Assert.Single(await acquire);
            Assert.Equal(ActivationEpochPhase.Activating, (await transition).Phase);
        }
        finally
        {
            releaseFence.TrySetResult();
            provider.AfterRunnableAdmissionFenceForTestAsync = null;
            await Task.WhenAll(acquire, transition);
        }
    }

    [Fact]
    public async Task Cancellation_during_commit_acknowledgement_reads_back_truthful_committed_state()
    {
        await _fixture.DropAllAsync();
        await _fixture.Provider.BeginReconciliationActivationEpochAsync(41);
        using var cancellation = new CancellationTokenSource();
        _fixture.ConcreteProvider.CommitActivationTransactionForTestAsync = async session =>
        {
            await Task.Yield();
            cancellation.Cancel();
            throw new OperationCanceledException(cancellation.Token);
        };

        var state = await _fixture.Provider.CommitReconciliationActivationEpochAsync(41, cancellation.Token);

        Assert.Equal(41, state.Epoch);
        Assert.Equal(ActivationEpochPhase.Activated, state.Phase);
        _fixture.ConcreteProvider.CommitActivationTransactionForTestAsync = null;
    }

    private async Task<CronTickerOccurrenceEntity<CronTickerEntity>> SeedRunnableOccurrenceAsync(string applicationNamespace)
    {
        var partitionKey = new TickerQRuntimePartition(applicationNamespace).StorageKey;
        var cron = new CronTickerEntity
        {
            ApplicationNamespaceKey = partitionKey,
            Id = Guid.NewGuid(), Function = "epoch-fence", Expression = "* * * * *",
            DefinitionRevision = 1, Request = [], CreatedAt = _fixture.FixedNow, UpdatedAt = _fixture.FixedNow
        };
        var occurrence = new CronTickerOccurrenceEntity<CronTickerEntity>
        {
            ApplicationNamespaceKey = partitionKey,
            Id = Guid.NewGuid(), CronTickerId = cron.Id, DefinitionRevision = 1,
            Status = TickerStatus.Idle, ExecutionTime = _fixture.FixedNow,
            CreatedAt = _fixture.FixedNow, UpdatedAt = _fixture.FixedNow
        };
        await _fixture.CronTickers.InsertOneAsync(cron);
        await _fixture.CronTickerOccurrences.InsertOneAsync(occurrence);
        return occurrence;
    }

    private IMongoCollection<BsonDocument> Metadata =>
        _fixture.Database.GetCollection<BsonDocument>("ticker_StoreMetadata");
}
