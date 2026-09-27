using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using TickerQ.EntityFrameworkCore.Entities;
using TickerQ.Utilities;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Managers;
using TickerQ.Utilities.Models;

namespace TickerQ.EntityFrameworkCore.Tests.Infrastructure;

internal sealed class RetryableAdmissionTestException : Exception;

internal sealed class RetryableAdmissionExecutionStrategy(ExecutionStrategyDependencies dependencies)
    : ExecutionStrategy(dependencies, 2, TimeSpan.Zero)
{
    protected override bool ShouldRetryOn(Exception exception)
        => exception is RetryableAdmissionTestException;
}

internal sealed class RetryableAdmissionExecutionStrategyFactory(ExecutionStrategyDependencies dependencies)
    : IExecutionStrategyFactory
{
    public IExecutionStrategy Create() => new RetryableAdmissionExecutionStrategy(dependencies);
}

internal sealed class NonPooledTestTickerQDbContextFactory(DbContextOptions<TestTickerQDbContext> options)
    : IDbContextFactory<TestTickerQDbContext>
{
    public TestTickerQDbContext CreateDbContext() => new(options);
    public Task<TestTickerQDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(new TestTickerQDbContext(options));
}

public sealed class EfReconciliationActivationEpochTests : IAsyncDisposable
{
    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"tickerq-epoch-{Guid.NewGuid():N}.db");
    private readonly ServiceProvider _services;
    private readonly List<ServiceProvider> _scopedServices = [];
    private readonly TestableProvider _provider;
    private readonly DbContextOptions<TestTickerQDbContext> _options;
    private readonly SchedulerOptionsBuilder _schedulerOptions = new() { ReconciliationEpoch = 1 };

    public EfReconciliationActivationEpochTests()
    {
        _options = new DbContextOptionsBuilder<TestTickerQDbContext>()
            .UseSqlite($"Data Source={_databasePath};Default Timeout=30;Pooling=False")
            .Options;
        using (var context = new TestTickerQDbContext(_options))
            context.Database.EnsureCreated();

        var services = new ServiceCollection();
        services.AddSingleton<IDbContextFactory<TestTickerQDbContext>>(
            new PooledDbContextFactory<TestTickerQDbContext>(_options));
        _services = services.BuildServiceProvider();
        var clock = Substitute.For<ITickerClock>();
        clock.UtcNow.Returns(new DateTime(2026, 7, 30, 12, 0, 0, DateTimeKind.Utc));
        var redis = Substitute.For<ITickerQRedisContext>();
        redis.HasRedisConnection.Returns(false);
        _schedulerOptions.BindRuntimeActivationScope("ef-reconciliation-tests", 1, true);
        _provider = new TestableProvider(_services, clock, _schedulerOptions, redis);
    }

    [Fact]
    public async Task Fresh_store_is_pending_and_provider_advertises_epoch_support()
    {
        Assert.True(_provider.SupportsReconciliationActivationEpoch);
        Assert.True(_provider.SupportsAuthoritativeCronReconciliation);
        var state = await _provider.GetReconciliationActivationStateAsync();
        Assert.Equal(0, state.Epoch);
        Assert.Equal(ActivationEpochPhase.Pending, state.Phase);
        Assert.Null(state.Checkpoint);
    }

    [Fact]
    public async Task Begin_checkpoint_and_commit_are_durable_and_idempotent()
    {
        var begun = await _provider.BeginReconciliationActivationEpochAsync(7);
        Assert.Equal(7, begun.Epoch);
        Assert.Equal(ActivationEpochPhase.Activating, begun.Phase);

        var checkpoint = await _provider.AdvanceReconciliationCheckpointAsync(7, "bootstrap-complete");
        Assert.Equal("bootstrap-complete", checkpoint.Checkpoint);
        Assert.Equal("bootstrap-complete", (await _provider
            .AdvanceReconciliationCheckpointAsync(7, "bootstrap-complete")).Checkpoint);

        using (var oldReader = new TestTickerQDbContext(_options))
        {
            var before = await oldReader.Set<TickerQStoreMetadata>().AsNoTracking().SingleAsync();
            Assert.Equal(ActivationEpochPhase.Activating, before.ActivationPhase);
        }

        var committed = await _provider.CommitReconciliationActivationEpochAsync(7);
        Assert.True(committed.IsActivatedFor(7));
        Assert.True((await _provider.CommitReconciliationActivationEpochAsync(7)).IsActivatedFor(7));
        Assert.True((await _provider.GetReconciliationActivationStateAsync()).IsActivatedFor(7));
    }

    [Fact]
    public async Task Restart_resumes_checkpoint_and_epochs_never_regress()
    {
        await _provider.BeginReconciliationActivationEpochAsync(5);
        await _provider.AdvanceReconciliationCheckpointAsync(5, "repair-complete");

        var restarted = new TestableProvider(_services, Substitute.For<ITickerClock>(),
            _schedulerOptions, Substitute.For<ITickerQRedisContext>());
        var resumed = await restarted.BeginReconciliationActivationEpochAsync(5);
        Assert.Equal("repair-complete", resumed.Checkpoint);

        await restarted.CommitReconciliationActivationEpochAsync(5);
        var lowerBegin = await restarted.BeginReconciliationActivationEpochAsync(4);
        var lowerCheckpoint = await restarted.AdvanceReconciliationCheckpointAsync(4, "must-not-write");
        var lowerCommit = await restarted.CommitReconciliationActivationEpochAsync(4);
        Assert.All(new[] { lowerBegin, lowerCheckpoint, lowerCommit }, state =>
        {
            Assert.Equal(5, state.Epoch);
            Assert.Equal(ActivationEpochPhase.Activated, state.Phase);
            Assert.Equal("repair-complete", state.Checkpoint);
        });

        var higher = await restarted.BeginReconciliationActivationEpochAsync(6);
        Assert.Equal(6, higher.Epoch);
        Assert.Equal(ActivationEpochPhase.Activating, higher.Phase);
        Assert.Null(higher.Checkpoint);
    }

    [Fact]
    public async Task Sixteen_concurrent_starters_and_duplicate_checkpoint_writers_converge()
    {
        var starts = await Task.WhenAll(Enumerable.Range(0, 16)
            .Select(_ => _provider.BeginReconciliationActivationEpochAsync(11)));
        Assert.All(starts, state => Assert.Equal(11, state.Epoch));
        Assert.All(starts, state => Assert.Equal(ActivationEpochPhase.Activating, state.Phase));

        var checkpoints = await Task.WhenAll(Enumerable.Range(0, 16)
            .Select(_ => _provider.AdvanceReconciliationCheckpointAsync(11, "repair-complete")));
        Assert.All(checkpoints, state => Assert.Equal("repair-complete", state.Checkpoint));

        var row = await ReadMetadataAsync();
        Assert.Equal(11, row.ActivationEpoch);
        Assert.Equal(ActivationEpochPhase.Activating, row.ActivationPhase);
        Assert.Equal("repair-complete", row.ActivationCheckpoint);
        Assert.True(row.Version > 0);
    }

    [Fact]
    public async Task Startup_checkpoint_sequence_cannot_regress_under_delayed_writer()
    {
        await _provider.BeginReconciliationActivationEpochAsync(17);
        await _provider.AdvanceReconciliationCheckpointAsync(17, "04-finalization-complete");
        var delayed = await _provider.AdvanceReconciliationCheckpointAsync(17, "01-bootstrap-complete");
        Assert.Equal("04-finalization-complete", delayed.Checkpoint);
    }

    [Fact]
    public async Task Cancelled_operation_before_commit_does_not_publish_activation()
    {
        await _provider.BeginReconciliationActivationEpochAsync(13);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            _provider.CommitReconciliationActivationEpochAsync(13, cancellation.Token));
        Assert.Equal(ActivationEpochPhase.Activating,
            (await _provider.GetReconciliationActivationStateAsync()).Phase);
    }

    [Fact]
    public async Task Runnable_admission_requires_exact_activated_configured_epoch()
    {
        var occurrence = await SeedRunnableOccurrenceAsync();

        await _provider.BeginReconciliationActivationEpochAsync(2);
        Assert.Empty(await _provider.AcquireImmediateCronOccurrencesAsync([occurrence.Id]));

        await _provider.CommitReconciliationActivationEpochAsync(2);
        Assert.Empty(await _provider.AcquireImmediateCronOccurrencesAsync([occurrence.Id]));

        Assert.Throws<InvalidOperationException>(() => _schedulerOptions.ReconciliationEpoch = 2);
        Assert.Empty(await _provider.AcquireImmediateCronOccurrencesAsync([occurrence.Id]));
    }

    [Fact]
    public async Task Startup_capability_manifest_and_public_cron_crud_are_exactly_admitted()
    {
        const string applicationNamespace = "ef-startup-crud";
        var scope = new ReconciliationActivationScope(applicationNamespace);
        var provider = CreateScopedProvider(applicationNamespace);
        await provider.BeginReconciliationActivationEpochAsync(scope, 1);
        var cron = new CronTickerEntity
        {
            Id = Guid.NewGuid(), Function = "ef-public-cron", Expression = "*/5 * * * *",
            Request = [], CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        };

        Assert.Equal(0, await provider.InsertCronTickers([cron], CancellationToken.None));
        using (StartupSeederAdmissionContext.Enter(new ReconciliationActivationScope("wrong").ScopeKey, 1))
            Assert.Equal(0, await provider.InsertCronTickers([cron], CancellationToken.None));
        using (StartupSeederAdmissionContext.Enter(scope.ScopeKey, 1))
            Assert.Equal(1, await provider.InsertCronTickers([cron], CancellationToken.None));
        Assert.Equal(1, cron.DefinitionRevision);

        var seed = new DefinedCronTickerSeed("ef-manifest", "*/7 * * * *");
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
            Status = TickerStatus.Idle, ExecutionTime = DateTime.UtcNow.AddMinutes(1),
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        };
        Assert.Equal(1, await provider.InsertCronTickerOccurrences([occurrence], CancellationToken.None));
        cron.Expression = "*/9 * * * *";
        Assert.Equal(1, await provider.UpdateCronTickers([cron], CancellationToken.None));
        Assert.Equal(2, cron.DefinitionRevision);
        Assert.Equal(TickerStatus.Skipped, (await ReadOccurrenceAsync(occurrence.Id)).Status);

        await provider.BeginReconciliationActivationEpochAsync(scope, 2);
        Assert.Equal(0, await provider.RemoveCronTickers([cron.Id], CancellationToken.None));
    }

    [Fact]
    public async Task Configured_scheduler_manager_resolved_before_host_start_cannot_discover_work()
    {
        var provider = CreateScopedProvider("manager-before-host-start", out var scheduler);
        var ticker = await SeedRunnableTimeTickerAsync(applicationNamespace: "manager-before-host-start");
        var clock = Substitute.For<ITickerClock>();
        clock.UtcNow.Returns(new DateTime(2026, 7, 30, 12, 0, 0, DateTimeKind.Utc));
        var manager = new InternalTickerManager<TimeTickerEntity, CronTickerEntity>(
            provider, clock, Substitute.For<ITickerQNotificationHubSender>(), scheduler);

        var beforeActivation = await manager.GetNextTickers();

        Assert.Equal(Timeout.InfiniteTimeSpan, beforeActivation.TimeRemaining);
        Assert.Empty(beforeActivation.Functions);
        await using var verify = new TestTickerQDbContext(_options);
        Assert.Equal(TickerStatus.Idle, (await verify.Set<TimeTickerEntity>()
            .AsNoTracking().SingleAsync(x => x.Id == ticker.Id)).Status);
    }

    [Fact]
    public async Task Runnable_admission_retry_uses_fresh_context_and_original_queue_timestamp_cas()
    {
        var retryOptions = new DbContextOptionsBuilder<TestTickerQDbContext>()
            .UseSqlite($"Data Source={_databasePath};Default Timeout=30;Pooling=False")
            .ReplaceService<IExecutionStrategyFactory, RetryableAdmissionExecutionStrategyFactory>()
            .Options;
        var services = new ServiceCollection()
            .AddSingleton<IDbContextFactory<TestTickerQDbContext>>(
                new NonPooledTestTickerQDbContextFactory(retryOptions))
            .BuildServiceProvider();
        _scopedServices.Add(services);
        var firstNow = new DateTime(2026, 7, 30, 12, 1, 0, DateTimeKind.Utc);
        var secondNow = firstNow.AddSeconds(1);
        var clock = Substitute.For<ITickerClock>();
        clock.UtcNow.Returns(firstNow, secondNow, secondNow);
        var retryScheduler = new SchedulerOptionsBuilder();
        retryScheduler.BindRuntimeActivationScope("ef-reconciliation-tests", 1, true);
        var provider = new TestableProvider(services, clock, retryScheduler,
            Substitute.For<ITickerQRedisContext>());
        await _provider.BeginReconciliationActivationEpochAsync(1);
        await _provider.CommitReconciliationActivationEpochAsync(1);
        var ticker = await SeedRunnableTimeTickerAsync();
        var contexts = new List<Guid>();
        provider.RunnableAdmissionContextCreatedForTest = contexts.Add;
        var attempts = 0;
        provider.AfterRunnableAdmissionOperationForTestAsync = _ =>
            Interlocked.Increment(ref attempts) == 1
                ? Task.FromException(new RetryableAdmissionTestException())
                : Task.CompletedTask;

        var queued = new List<TimeTickerEntity>();
        await foreach (var row in provider.QueueTimeTickers([ticker], CancellationToken.None))
            queued.Add(row);

        Assert.Single(queued);
        Assert.Equal(2, attempts);
        Assert.Equal(2, contexts.Distinct().Count());
        await using var verify = new TestTickerQDbContext(_options);
        var persisted = await verify.Set<TimeTickerEntity>().AsNoTracking().SingleAsync(x => x.Id == ticker.Id);
        Assert.Equal(TickerStatus.Queued, persisted.Status);
        Assert.Equal(secondNow, persisted.UpdatedAt);
    }

    [Fact]
    public async Task Namespaced_queue_only_provider_bypasses_activation_metadata_after_protocol_call()
    {
        const string applicationNamespace = "queue-only-application";
        var scheduler = new SchedulerOptionsBuilder();
        scheduler.BindRuntimeActivationScope(applicationNamespace, 7, false);
        var provider = new TestableProvider(_services, Substitute.For<ITickerClock>(), scheduler,
            Substitute.For<ITickerQRedisContext>());
        var scope = new ReconciliationActivationScope(applicationNamespace);
        var ticker = await SeedRunnableTimeTickerAsync(applicationNamespace: applicationNamespace);
        await provider.BeginReconciliationActivationEpochAsync(scope, 7);
        provider.AfterRunnableAdmissionFenceForTestAsync = _ =>
            Task.FromException(new InvalidOperationException("queue-only must not touch activation admission"));

        var queued = new List<TimeTickerEntity>();
        await foreach (var row in provider.QueueTimeTickers([ticker], CancellationToken.None))
            queued.Add(row);

        Assert.Single(queued);
        var state = await provider.GetReconciliationActivationStateAsync(scope);
        Assert.Equal(ActivationEpochPhase.Activating, state.Phase);
        Assert.Equal(7, state.Epoch);
    }

    [Fact]
    public async Task Scheduler_enabled_without_runtime_scope_binding_fails_closed_without_metadata_write()
    {
        var scheduler = new SchedulerOptionsBuilder();
        typeof(SchedulerOptionsBuilder).GetProperty("RuntimeSchedulerEnabled",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(scheduler, true);
        var provider = new TestableProvider(_services, Substitute.For<ITickerClock>(), scheduler,
            Substitute.For<ITickerQRedisContext>());
        var occurrence = await SeedRunnableOccurrenceAsync();

        Assert.Empty(await provider.AcquireImmediateCronOccurrencesAsync([occurrence.Id]));
        await using var verify = new TestTickerQDbContext(_options);
        Assert.Empty(await verify.Set<TickerQStoreMetadata>().AsNoTracking().ToArrayAsync());
    }

    [Fact]
    public async Task Scoped_get_before_begin_cannot_redirect_construction_bound_runtime_admission()
    {
        const string configuredNamespace = "application-a";
        var provider = CreateScopedProvider(configuredNamespace);
        var configuredScope = new ReconciliationActivationScope(configuredNamespace);
        var otherScope = new ReconciliationActivationScope("application-b");
        var occurrence = await SeedRunnableOccurrenceAsync(applicationNamespace: configuredNamespace);

        Assert.Equal(ActivationEpochState.PreEpoch,
            await provider.GetReconciliationActivationStateAsync(otherScope));
        Assert.Empty(await provider.AcquireImmediateCronOccurrencesAsync([occurrence.Id]));
        await provider.BeginReconciliationActivationEpochAsync(configuredScope, 1);
        await provider.CommitReconciliationActivationEpochAsync(configuredScope, 1);
        Assert.Single(await provider.AcquireImmediateCronOccurrencesAsync([occurrence.Id]));

        await using var verify = new TestTickerQDbContext(_options);
        Assert.Contains(await verify.Set<TickerQStoreMetadata>().AsNoTracking().ToListAsync(),
            x => x.Id == configuredScope.ScopeKey && x.ActivationPhase == ActivationEpochPhase.Activated);
        Assert.DoesNotContain(await verify.Set<TickerQStoreMetadata>().AsNoTracking().ToListAsync(),
            x => x.Id == otherScope.ScopeKey);
    }

    [Fact]
    public async Task Beginning_other_scope_before_configured_scope_never_redirects_runtime_admission()
    {
        const string configuredNamespace = "application-a";
        var provider = CreateScopedProvider(configuredNamespace);
        var configuredScope = new ReconciliationActivationScope(configuredNamespace);
        var otherScope = new ReconciliationActivationScope("application-b");
        var occurrence = await SeedRunnableOccurrenceAsync(applicationNamespace: configuredNamespace);

        await provider.BeginReconciliationActivationEpochAsync(otherScope, 1);
        Assert.Empty(await provider.AcquireImmediateCronOccurrencesAsync([occurrence.Id]));
        await provider.BeginReconciliationActivationEpochAsync(configuredScope, 1);
        await provider.CommitReconciliationActivationEpochAsync(configuredScope, 1);
        Assert.Single(await provider.AcquireImmediateCronOccurrencesAsync([occurrence.Id]));

        await using var verify = new TestTickerQDbContext(_options);
        var rows = await verify.Set<TickerQStoreMetadata>().AsNoTracking().ToDictionaryAsync(x => x.Id);
        Assert.Equal(ActivationEpochPhase.Activating, rows[otherScope.ScopeKey].ActivationPhase);
        Assert.Equal(ActivationEpochPhase.Activated, rows[configuredScope.ScopeKey].ActivationPhase);
    }

    [Fact]
    public async Task Partition_chain_repair_preserves_cross_application_graph_write()
    {
        var repairProvider = CreateScopedProvider("application-a");
        var writerProvider = CreateScopedProvider("application-b");
        var writerScope = new ReconciliationActivationScope("application-b");
        await writerProvider.BeginReconciliationActivationEpochAsync(writerScope, 1);
        await writerProvider.CommitReconciliationActivationEpochAsync(writerScope, 1);
        var root = await SeedRunnableTimeTickerAsync(applicationNamespace: "application-a");
        var child = new TimeTickerEntity
        {
            ApplicationNamespaceKey = new TickerQRuntimePartition("application-a").StorageKey,
            Id = Guid.NewGuid(), ParentId = root.Id, ChainRootId = Guid.NewGuid(),
            Function = "repair-child", Request = [], Status = TickerStatus.Idle,
            CreatedAt = root.CreatedAt, UpdatedAt = root.UpdatedAt
        };
        await using (var seed = new TestTickerQDbContext(_options))
        {
            seed.Add(child);
            await seed.SaveChangesAsync();
            await seed.Set<TimeTickerEntity>().Where(x => x.Id == root.Id)
                .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.ChainRootId, Guid.NewGuid()));
        }

        var fenceReached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFence = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        repairProvider.AfterTimeTickerGraphMutationFenceForTestAsync = async _ =>
        {
            fenceReached.TrySetResult();
            await releaseFence.Task;
        };

        var repair = Task.Run(() => repairProvider.RepairTimeTickerChainsAsync());
        await fenceReached.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var added = new TimeTickerEntity
        {
            ApplicationNamespaceKey = new TickerQRuntimePartition("application-b").StorageKey,
            Id = Guid.NewGuid(), Function = "cross-app-writer", Request = [], Status = TickerStatus.Idle,
            ExecutionTime = root.ExecutionTime, CreatedAt = root.CreatedAt, UpdatedAt = root.UpdatedAt
        };
        var write = Task.Run(() => writerProvider.AddTimeTickers([added], CancellationToken.None));
        await Task.Delay(100);
        Assert.False(write.IsCompleted);

        releaseFence.TrySetResult();
        Assert.Equal(2, (await repair).RepairedRows);
        Assert.Equal(1, await write.WaitAsync(TimeSpan.FromSeconds(2)));
        repairProvider.AfterTimeTickerGraphMutationFenceForTestAsync = null;

        await using var verify = new TestTickerQDbContext(_options);
        var rows = await verify.Set<TimeTickerEntity>().AsNoTracking().ToDictionaryAsync(x => x.Id);
        Assert.Equal(root.Id, rows[root.Id].ChainRootId);
        Assert.Equal(root.Id, rows[child.Id].ChainRootId);
        Assert.Equal(added.Id, rows[added.Id].ChainRootId);
    }

    [Fact]
    public async Task Bound_scheduler_with_absent_metadata_fails_closed_without_canonicalizing_admission()
    {
        var occurrence = await SeedRunnableOccurrenceAsync();

        Assert.Empty(await _provider.AcquireImmediateCronOccurrencesAsync([occurrence.Id]));
        await using var verify = new TestTickerQDbContext(_options);
        Assert.Empty(await verify.Set<TickerQStoreMetadata>().AsNoTracking().ToArrayAsync());
    }

    [Fact]
    public async Task Activation_transition_and_acquisition_share_one_serializable_admission_boundary()
    {
        var occurrence = await SeedRunnableOccurrenceAsync();
        await _provider.BeginReconciliationActivationEpochAsync(1);
        await _provider.CommitReconciliationActivationEpochAsync(1);

        var fenceReached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFence = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _provider.AfterRunnableAdmissionFenceForTestAsync = async _ =>
        {
            fenceReached.TrySetResult();
            await releaseFence.Task;
        };

        var acquire = _provider.AcquireImmediateCronOccurrencesAsync([occurrence.Id]);
        await fenceReached.Task;
        var transition = Task.Run(() => _provider.BeginReconciliationActivationEpochAsync(2));
        await Task.Delay(100);
        Assert.False(transition.IsCompleted);

        releaseFence.TrySetResult();
        Assert.Single(await acquire);
        Assert.Equal(ActivationEpochPhase.Activating, (await transition).Phase);
        _provider.AfterRunnableAdmissionFenceForTestAsync = null;
        Assert.Equal(TickerStatus.InProgress, (await ReadOccurrenceAsync(occurrence.Id)).Status);
    }

    [Fact]
    public async Task Activation_transition_and_time_queue_share_one_serializable_admission_boundary()
    {
        var ticker = await SeedRunnableTimeTickerAsync();
        await _provider.BeginReconciliationActivationEpochAsync(1);
        await _provider.CommitReconciliationActivationEpochAsync(1);

        var fenceReached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFence = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _provider.AfterRunnableAdmissionFenceForTestAsync = async _ =>
        {
            fenceReached.TrySetResult();
            await releaseFence.Task;
        };

        var queue = Task.Run(async () =>
        {
            var queued = new List<TimeTickerEntity>();
            await foreach (var item in _provider.QueueTimeTickers([ticker], CancellationToken.None))
                queued.Add(item);
            return queued;
        });
        await fenceReached.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var transition = Task.Run(() => _provider.BeginReconciliationActivationEpochAsync(2));
        await Task.Delay(100);
        Assert.False(transition.IsCompleted);

        releaseFence.TrySetResult();
        Assert.Single(await queue);
        Assert.Equal(ActivationEpochPhase.Activating, (await transition).Phase);
        _provider.AfterRunnableAdmissionFenceForTestAsync = null;
    }

    [Fact]
    public async Task Activation_transition_and_timed_out_time_acquisition_share_one_serializable_admission_boundary()
    {
        await SeedRunnableTimeTickerAsync(TimeSpan.FromSeconds(-2));
        await _provider.BeginReconciliationActivationEpochAsync(1);
        await _provider.CommitReconciliationActivationEpochAsync(1);

        var fenceReached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFence = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _provider.AfterRunnableAdmissionFenceForTestAsync = async _ =>
        {
            fenceReached.TrySetResult();
            await releaseFence.Task;
        };

        var acquisition = Task.Run(async () =>
        {
            var acquired = new List<TimeTickerEntity>();
            await foreach (var item in _provider.QueueTimedOutTimeTickers(CancellationToken.None))
                acquired.Add(item);
            return acquired;
        });
        await fenceReached.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var transition = Task.Run(() => _provider.BeginReconciliationActivationEpochAsync(2));
        await Task.Delay(100);
        Assert.False(transition.IsCompleted);

        releaseFence.TrySetResult();
        Assert.Single(await acquisition);
        Assert.Equal(ActivationEpochPhase.Activating, (await transition).Phase);
        _provider.AfterRunnableAdmissionFenceForTestAsync = null;
    }

    [Fact]
    public async Task Activation_transition_and_timed_out_occurrence_acquisition_share_one_serializable_admission_boundary()
    {
        await SeedRunnableOccurrenceAsync(TimeSpan.FromSeconds(-2));
        await _provider.BeginReconciliationActivationEpochAsync(1);
        await _provider.CommitReconciliationActivationEpochAsync(1);

        var fenceReached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFence = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _provider.AfterRunnableAdmissionFenceForTestAsync = async _ =>
        {
            fenceReached.TrySetResult();
            await releaseFence.Task;
        };

        var acquisition = Task.Run(async () =>
        {
            var acquired = new List<CronTickerOccurrenceEntity<CronTickerEntity>>();
            await foreach (var item in _provider.QueueTimedOutCronTickerOccurrences(CancellationToken.None))
                acquired.Add(item);
            return acquired;
        });
        await fenceReached.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var transition = Task.Run(() => _provider.BeginReconciliationActivationEpochAsync(2));
        await Task.Delay(100);
        Assert.False(transition.IsCompleted);

        releaseFence.TrySetResult();
        Assert.Single(await acquisition);
        Assert.Equal(ActivationEpochPhase.Activating, (await transition).Phase);
        _provider.AfterRunnableAdmissionFenceForTestAsync = null;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Activation_transition_and_cron_publication_share_one_serializable_admission_boundary(
        bool requeueExisting)
    {
        var occurrence = await SeedRunnableOccurrenceAsync();
        if (!requeueExisting)
        {
            await using var cleanup = new TestTickerQDbContext(_options);
            cleanup.Remove(occurrence);
            await cleanup.SaveChangesAsync();
        }
        await _provider.BeginReconciliationActivationEpochAsync(1);
        await _provider.CommitReconciliationActivationEpochAsync(1);

        var fenceReached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFence = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _provider.AfterRunnableAdmissionFenceForTestAsync = async _ =>
        {
            fenceReached.TrySetResult();
            await releaseFence.Task;
        };
        var item = new InternalManagerContext(occurrence.CronTickerId)
        {
            FunctionName = "epoch-fence",
            Expression = "* * * * *",
            DefinitionRevision = 1,
            NextCronOccurrence = requeueExisting
                ? new NextCronOccurrence(occurrence.Id, occurrence.CreatedAt)
                : null
        };
        var publication = Task.Run(async () =>
        {
            var rows = new List<CronTickerOccurrenceEntity<CronTickerEntity>>();
            await foreach (var row in _provider.QueueCronTickerOccurrences(
                               (occurrence.ExecutionTime, [item]), CancellationToken.None))
                rows.Add(row);
            return rows;
        });
        await fenceReached.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var transition = Task.Run(() => _provider.BeginReconciliationActivationEpochAsync(2));
        await Task.Delay(100);
        Assert.False(transition.IsCompleted);

        releaseFence.TrySetResult();
        Assert.Single(await publication);
        Assert.Equal(ActivationEpochPhase.Activating, (await transition).Phase);
        _provider.AfterRunnableAdmissionFenceForTestAsync = null;
    }

    [Fact]
    public async Task Activation_transition_and_chain_replacement_share_one_serializable_admission_boundary()
    {
        var oldRoot = await SeedRunnableTimeTickerAsync();
        await _provider.BeginReconciliationActivationEpochAsync(1);
        await _provider.CommitReconciliationActivationEpochAsync(1);
        var replacement = new TimeTickerEntity
        {
            Id = Guid.NewGuid(), Function = "replacement", Request = [], Status = TickerStatus.Idle,
            ExecutionTime = oldRoot.ExecutionTime, CreatedAt = oldRoot.CreatedAt, UpdatedAt = oldRoot.UpdatedAt
        };
        var fenceReached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFence = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _provider.AfterRunnableAdmissionFenceForTestAsync = async _ =>
        {
            fenceReached.TrySetResult();
            await releaseFence.Task;
        };

        var replace = Task.Run(() => _provider.ReplaceTimeTickerChainAsync(oldRoot.Id, replacement));
        await fenceReached.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var transition = Task.Run(() => _provider.BeginReconciliationActivationEpochAsync(2));
        await Task.Delay(100);
        Assert.False(transition.IsCompleted);

        releaseFence.TrySetResult();
        Assert.Equal(1, await replace);
        Assert.Equal(ActivationEpochPhase.Activating, (await transition).Phase);
        _provider.AfterRunnableAdmissionFenceForTestAsync = null;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Activation_transition_and_exact_acquired_release_share_one_serializable_boundary(bool cron)
    {
        await _provider.BeginReconciliationActivationEpochAsync(1);
        await _provider.CommitReconciliationActivationEpochAsync(1);
        Guid id;
        if (cron)
        {
            var occurrence = await SeedRunnableOccurrenceAsync();
            id = Assert.Single(await _provider.AcquireImmediateCronOccurrencesAsync([occurrence.Id])).Id;
        }
        else
        {
            var ticker = await SeedRunnableTimeTickerAsync();
            id = Assert.Single(await _provider.AcquireImmediateTimeTickersAsync([ticker.Id])).Id;
        }

        var fenceReached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFence = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _provider.AfterRunnableAdmissionFenceForTestAsync = async _ =>
        {
            fenceReached.TrySetResult();
            await releaseFence.Task;
        };
        var release = Task.Run(() => cron
            ? _provider.ReleaseAcquiredCronTickerOccurrences([id])
            : _provider.ReleaseAcquiredTimeTickers([id], CancellationToken.None));
        await fenceReached.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var transition = Task.Run(() => _provider.BeginReconciliationActivationEpochAsync(2));
        await Task.Delay(100);
        Assert.False(transition.IsCompleted);

        releaseFence.TrySetResult();
        await release;
        Assert.Equal(ActivationEpochPhase.Activating, (await transition).Phase);
        _provider.AfterRunnableAdmissionFenceForTestAsync = null;
    }

    [Fact]
    public async Task Activation_transition_and_dead_node_time_release_share_one_serializable_admission_boundary()
    {
        var ticker = await SeedRunnableTimeTickerAsync();
        await using (var context = new TestTickerQDbContext(_options))
        {
            await context.Set<TimeTickerEntity>().Where(x => x.Id == ticker.Id)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(x => x.Status, TickerStatus.Queued)
                    .SetProperty(x => x.LockHolder, "dead-node")
                    .SetProperty(x => x.LockedAt, DateTime.UtcNow)
                    .SetProperty(x => x.AcquisitionToken, Guid.NewGuid()));
        }
        await _provider.BeginReconciliationActivationEpochAsync(1);
        await _provider.CommitReconciliationActivationEpochAsync(1);

        var fenceReached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFence = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _provider.AfterRunnableAdmissionFenceForTestAsync = async _ =>
        {
            fenceReached.TrySetResult();
            await releaseFence.Task;
        };

        var release = Task.Run(() =>
            _provider.ReleaseDeadNodeTimeTickerResources("dead-node", CancellationToken.None));
        await fenceReached.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var transition = Task.Run(() => _provider.BeginReconciliationActivationEpochAsync(2));
        await Task.Delay(100);
        Assert.False(transition.IsCompleted);

        releaseFence.TrySetResult();
        await release;
        Assert.Equal(ActivationEpochPhase.Activating, (await transition).Phase);
        _provider.AfterRunnableAdmissionFenceForTestAsync = null;
        await using var verify = new TestTickerQDbContext(_options);
        var released = await verify.Set<TimeTickerEntity>().AsNoTracking().SingleAsync(x => x.Id == ticker.Id);
        Assert.Null(released.LockHolder);
        Assert.Null(released.AcquisitionToken);
    }

    [Fact]
    public async Task Unified_idle_write_is_denied_during_next_activation_and_uses_graph_boundary()
    {
        await _provider.BeginReconciliationActivationEpochAsync(1);
        await _provider.CommitReconciliationActivationEpochAsync(1);
        var ticker = await SeedRunnableTimeTickerAsync();
        var acquired = Assert.Single(await _provider.AcquireImmediateTimeTickersAsync([ticker.Id]));
        var graphLocks = 0;
        _provider.AfterTimeTickerGraphMutationFenceForTestAsync = _ =>
        {
            Interlocked.Increment(ref graphLocks);
            return Task.CompletedTask;
        };
        await _provider.BeginReconciliationActivationEpochAsync(2);

        var idle = new InternalFunctionContext
        {
            TickerId = ticker.Id,
            AcquisitionToken = acquired.AcquisitionToken,
            Type = TickerType.TimeTicker
        }.SetProperty(x => x.Status, TickerStatus.Idle);
        await _provider.UpdateTimeTickersWithUnifiedContext([ticker.Id], idle, CancellationToken.None);

        Assert.Equal(1, graphLocks);
        await using var verify = new TestTickerQDbContext(_options);
        var persisted = await verify.Set<TimeTickerEntity>().AsNoTracking().SingleAsync(x => x.Id == ticker.Id);
        Assert.Equal(TickerStatus.InProgress, persisted.Status);
        Assert.Equal(acquired.AcquisitionToken, persisted.AcquisitionToken);
    }

    [Fact]
    public async Task Dead_node_cleanup_does_not_release_descendant_from_old_root_generation()
    {
        var root = await SeedRunnableTimeTickerAsync();
        var oldGeneration = Guid.NewGuid();
        var currentGeneration = Guid.NewGuid();
        var child = new TimeTickerEntity
        {
            ApplicationNamespaceKey = new TickerQRuntimePartition("ef-reconciliation-tests").StorageKey,
            Id = Guid.NewGuid(), ParentId = root.Id, ChainRootId = root.Id, ChainGeneration = oldGeneration,
            Function = "old-generation-child", Request = [], Status = TickerStatus.Queued,
            LockHolder = "dead-node", LockedAt = DateTime.UtcNow, AcquisitionToken = Guid.NewGuid(),
            CreatedAt = root.CreatedAt, UpdatedAt = root.UpdatedAt
        };
        await using (var seed = new TestTickerQDbContext(_options))
        {
            seed.Add(child);
            await seed.SaveChangesAsync();
            await seed.Set<TimeTickerEntity>().Where(x => x.Id == root.Id)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(x => x.ChainRootId, root.Id)
                    .SetProperty(x => x.ChainGeneration, currentGeneration));
        }
        await _provider.BeginReconciliationActivationEpochAsync(1);
        await _provider.CommitReconciliationActivationEpochAsync(1);
        var graphLocks = 0;
        _provider.AfterTimeTickerGraphMutationFenceForTestAsync = _ =>
        {
            Interlocked.Increment(ref graphLocks);
            return Task.CompletedTask;
        };

        await _provider.ReleaseDeadNodeTimeTickerResources("dead-node", CancellationToken.None);

        Assert.Equal(1, graphLocks);
        await using var verify = new TestTickerQDbContext(_options);
        var persisted = await verify.Set<TimeTickerEntity>().AsNoTracking().SingleAsync(x => x.Id == child.Id);
        Assert.Equal(TickerStatus.Queued, persisted.Status);
        Assert.Equal("dead-node", persisted.LockHolder);
        Assert.NotNull(persisted.AcquisitionToken);
    }

    [Fact]
    public async Task Activation_transition_and_dead_node_occurrence_release_share_one_serializable_admission_boundary()
    {
        var occurrence = await SeedRunnableOccurrenceAsync();
        await using (var context = new TestTickerQDbContext(_options))
        {
            await context.Set<CronTickerOccurrenceEntity<CronTickerEntity>>().Where(x => x.Id == occurrence.Id)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(x => x.Status, TickerStatus.Queued)
                    .SetProperty(x => x.LockHolder, "dead-node")
                    .SetProperty(x => x.LockedAt, DateTime.UtcNow)
                    .SetProperty(x => x.AcquisitionToken, Guid.NewGuid()));
        }
        await _provider.BeginReconciliationActivationEpochAsync(1);
        await _provider.CommitReconciliationActivationEpochAsync(1);

        var fenceReached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFence = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _provider.AfterRunnableAdmissionFenceForTestAsync = async _ =>
        {
            fenceReached.TrySetResult();
            await releaseFence.Task;
        };

        var release = Task.Run(() =>
            _provider.ReleaseDeadNodeOccurrenceResources("dead-node", CancellationToken.None));
        await fenceReached.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var transition = Task.Run(() => _provider.BeginReconciliationActivationEpochAsync(2));
        await Task.Delay(100);
        Assert.False(transition.IsCompleted);

        releaseFence.TrySetResult();
        await release;
        Assert.Equal(ActivationEpochPhase.Activating, (await transition).Phase);
        _provider.AfterRunnableAdmissionFenceForTestAsync = null;
        await using var verify = new TestTickerQDbContext(_options);
        var released = await verify.Set<CronTickerOccurrenceEntity<CronTickerEntity>>()
            .AsNoTracking().SingleAsync(x => x.Id == occurrence.Id);
        Assert.Null(released.LockHolder);
        Assert.Null(released.AcquisitionToken);
    }

    [Fact]
    public async Task Activation_transition_and_stale_runnable_release_share_one_serializable_admission_boundary()
    {
        var ticker = await SeedRunnableTimeTickerAsync();
        var now = new DateTime(2026, 7, 30, 12, 0, 0, DateTimeKind.Utc);
        await using (var context = new TestTickerQDbContext(_options))
        {
            await context.Set<TimeTickerEntity>().Where(x => x.Id == ticker.Id)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(x => x.Status, TickerStatus.Queued)
                    .SetProperty(x => x.LockHolder, "stale-node")
                    .SetProperty(x => x.LockedAt, now.AddHours(-1)));
        }
        await _provider.BeginReconciliationActivationEpochAsync(1);
        await _provider.CommitReconciliationActivationEpochAsync(1);

        var fenceReached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFence = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _provider.AfterRunnableAdmissionFenceForTestAsync = async _ =>
        {
            fenceReached.TrySetResult();
            await releaseFence.Task;
        };

        var recovery = Task.Run(() => _provider.RecoverStaleTickers(1, CancellationToken.None));
        await fenceReached.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var transition = Task.Run(() => _provider.BeginReconciliationActivationEpochAsync(2));
        await Task.Delay(100);
        Assert.False(transition.IsCompleted);

        releaseFence.TrySetResult();
        await recovery;
        Assert.Equal(ActivationEpochPhase.Activating, (await transition).Phase);
        _provider.AfterRunnableAdmissionFenceForTestAsync = null;
    }

    private async Task<CronTickerOccurrenceEntity<CronTickerEntity>> SeedRunnableOccurrenceAsync(
        TimeSpan? executionOffset = null, string applicationNamespace = "ef-reconciliation-tests")
    {
        var now = new DateTime(2026, 7, 30, 12, 0, 0, DateTimeKind.Utc);
        var partitionKey = new TickerQRuntimePartition(applicationNamespace).StorageKey;
        var cron = new CronTickerEntity
        {
            ApplicationNamespaceKey = partitionKey,
            Id = Guid.NewGuid(), Function = "epoch-fence", Expression = "* * * * *",
            DefinitionRevision = 1, Request = [], CreatedAt = now, UpdatedAt = now
        };
        var occurrence = new CronTickerOccurrenceEntity<CronTickerEntity>
        {
            ApplicationNamespaceKey = partitionKey,
            Id = Guid.NewGuid(), CronTickerId = cron.Id, DefinitionRevision = 1,
            Status = TickerStatus.Idle, ExecutionTime = now.Add(executionOffset ?? TimeSpan.Zero), CreatedAt = now, UpdatedAt = now
        };
        await using var context = new TestTickerQDbContext(_options);
        context.AddRange(cron, occurrence);
        await context.SaveChangesAsync();
        return occurrence;
    }

    private async Task<TimeTickerEntity> SeedRunnableTimeTickerAsync(
        TimeSpan? executionOffset = null, string applicationNamespace = "ef-reconciliation-tests")
    {
        var now = new DateTime(2026, 7, 30, 12, 0, 0, DateTimeKind.Utc);
        var ticker = new TimeTickerEntity
        {
            ApplicationNamespaceKey = new TickerQRuntimePartition(applicationNamespace).StorageKey,
            Id = Guid.NewGuid(), Function = "epoch-fence", Request = [],
            Status = TickerStatus.Idle, ExecutionTime = now.Add(executionOffset ?? TimeSpan.Zero), CreatedAt = now, UpdatedAt = now
        };
        await using var context = new TestTickerQDbContext(_options);
        context.Add(ticker);
        await context.SaveChangesAsync();
        return ticker;
    }

    private async Task<CronTickerOccurrenceEntity<CronTickerEntity>> ReadOccurrenceAsync(Guid id)
    {
        await using var context = new TestTickerQDbContext(_options);
        return await context.Set<CronTickerOccurrenceEntity<CronTickerEntity>>().AsNoTracking()
            .SingleAsync(x => x.Id == id);
    }

    private async Task<TickerQStoreMetadata> ReadMetadataAsync()
    {
        await using var context = new TestTickerQDbContext(_options);
        return await context.Set<TickerQStoreMetadata>().AsNoTracking().SingleAsync();
    }

    private TestableProvider CreateScopedProvider(string applicationNamespace)
        => CreateScopedProvider(applicationNamespace, out _);

    private TestableProvider CreateScopedProvider(
        string applicationNamespace, out SchedulerOptionsBuilder scheduler)
    {
        scheduler = new SchedulerOptionsBuilder();
        var executionContext = new TickerExecutionContext();
        var tickerOptions = new TickerOptionsBuilder<TimeTickerEntity, CronTickerEntity>(executionContext, scheduler);
        tickerOptions.UseDefinedCronApplicationNamespace(applicationNamespace)
            .UseReconciliationEpoch(1);
        scheduler.ReconciliationEpoch = 1;
        scheduler.BindRuntimeActivationScope(applicationNamespace, 1, true);
        var services = new ServiceCollection()
            .AddSingleton<IDbContextFactory<TestTickerQDbContext>>(
                new PooledDbContextFactory<TestTickerQDbContext>(_options))
            .AddSingleton(executionContext)
            .BuildServiceProvider();
        _scopedServices.Add(services);
        var clock = Substitute.For<ITickerClock>();
        clock.UtcNow.Returns(new DateTime(2026, 7, 30, 12, 0, 0, DateTimeKind.Utc));
        var redis = Substitute.For<ITickerQRedisContext>();
        redis.HasRedisConnection.Returns(false);
        return new TestableProvider(services, clock, scheduler, redis);
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var services in _scopedServices)
            await services.DisposeAsync();
        await _services.DisposeAsync();
        SqliteConnection.ClearAllPools();
        if (File.Exists(_databasePath)) File.Delete(_databasePath);
    }
}
