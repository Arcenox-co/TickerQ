using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using StackExchange.Redis;
using System.Net;
using TickerQ.BackgroundServices;
using TickerQ.Caching.StackExchangeRedis.Helpers;
using TickerQ.Caching.StackExchangeRedis.Infrastructure;
using static TickerQ.Caching.StackExchangeRedis.DependencyInjection.ServiceExtension;
using TickerQ.Utilities;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Interfaces.Managers;
using TickerQ.Utilities.Models;

namespace TickerQ.Caching.StackExchangeRedis.Tests.Infrastructure;

[Collection("RedisRealScript")]
public sealed class RedisReconciliationActivationEpochTests
{
    private static readonly DateTime Now = new(2026, 7, 30, 12, 0, 0, DateTimeKind.Utc);
    private readonly RedisRealScriptFixture _fixture;
    private readonly IDatabase _db;
    private readonly ITickerClock _clock;
    private readonly SchedulerOptionsBuilder _options;
    private readonly TickerQRedisOptionBuilder _redisOptions;
    private readonly TickerRedisPersistenceProvider<TimeTickerEntity, CronTickerEntity> _provider;

    public RedisReconciliationActivationEpochTests(RedisRealScriptFixture fixture)
    {
        _fixture = fixture;
        _db = fixture.Db;
        _db.Execute("FLUSHALL");
        _clock = Substitute.For<ITickerClock>();
        _clock.UtcNow.Returns(Now);
        _options = new SchedulerOptionsBuilder { NodeIdentifier = "activation-node" };
        _redisOptions = new TickerQRedisOptionBuilder { JsonSerializerContext = TestJsonSerializerContext.Default };
        _provider = NewProvider("activation-node");
    }

    private TickerRedisPersistenceProvider<TimeTickerEntity, CronTickerEntity> NewProvider(
        string node, long epoch = 1, string runtimeScope = null)
    {
        var options = new SchedulerOptionsBuilder { NodeIdentifier = node, ReconciliationEpoch = epoch };
        if (runtimeScope != null)
            options.BindRuntimeActivationScope(runtimeScope, epoch, schedulerEnabled: true);
        return new TickerRedisPersistenceProvider<TimeTickerEntity, CronTickerEntity>(
            _db, _clock, options, _redisOptions,
            NullLogger<TickerRedisPersistenceProvider<TimeTickerEntity, CronTickerEntity>>.Instance);
    }

    private static TimeTickerEntity NewTimeTicker(TickerStatus status = TickerStatus.Idle) => new()
    {
        Id = Guid.NewGuid(), Function = "epoch-fence", ExecutionTime = Now.AddMinutes(-1),
        Status = status, CreatedAt = Now.AddHours(-1), UpdatedAt = Now.AddHours(-1), Request = []
    };

    private static CronTickerEntity NewCron() => new()
    {
        Id = Guid.NewGuid(), Function = "epoch-cron", Expression = "*/5 * * * *",
        DefinitionRevision = 1, CreatedAt = Now.AddHours(-1), UpdatedAt = Now.AddHours(-1), Request = []
    };

    [Fact]
    public async Task Fresh_begin_checkpoint_commit_and_restart_are_durable_and_idempotent()
    {
        ITickerPersistenceProvider<TimeTickerEntity, CronTickerEntity> provider = _provider;
        Assert.True(provider.SupportsReconciliationActivationEpoch);
        Assert.True(provider.SupportsAuthoritativeCronReconciliation);
        Assert.Equal(ActivationEpochPhase.Pending,
            (await provider.GetReconciliationActivationStateAsync()).Phase);

        var begun = await provider.BeginReconciliationActivationEpochAsync(7);
        Assert.Equal((7, ActivationEpochPhase.Activating, null),
            (begun.Epoch, begun.Phase, begun.Checkpoint));
        Assert.Equal("01-bootstrap-complete",
            (await provider.AdvanceReconciliationCheckpointAsync(7, "01-bootstrap-complete")).Checkpoint);

        ITickerPersistenceProvider<TimeTickerEntity, CronTickerEntity> restarted = NewProvider("restart-node");
        Assert.Equal("01-bootstrap-complete",
            (await restarted.BeginReconciliationActivationEpochAsync(7)).Checkpoint);
        Assert.True((await restarted.CommitReconciliationActivationEpochAsync(7)).IsActivatedFor(7));
        Assert.True((await restarted.CommitReconciliationActivationEpochAsync(7)).IsActivatedFor(7));
        Assert.True((await provider.GetReconciliationActivationStateAsync()).IsActivatedFor(7));
    }

    [Fact]
    public async Task Sixteen_concurrent_starters_and_checkpoint_writers_converge_without_regression()
    {
        var providers = Enumerable.Range(0, 16).Select(i => NewProvider($"node-{i}")).ToArray();
        var starts = await Task.WhenAll(providers.Select(p => p.BeginReconciliationActivationEpochAsync(11)));
        Assert.All(starts, state => Assert.Equal((11, ActivationEpochPhase.Activating), (state.Epoch, state.Phase)));

        var checkpoints = await Task.WhenAll(providers.Select((p, i) =>
            p.AdvanceReconciliationCheckpointAsync(11, i % 2 == 0
                ? "04-finalization-complete"
                : "01-bootstrap-complete")));
        Assert.All(checkpoints, state => Assert.Contains(state.Checkpoint,
            new[] { "01-bootstrap-complete", "04-finalization-complete" }));
        Assert.Equal("04-finalization-complete",
            (await _provider.GetReconciliationActivationStateAsync()).Checkpoint);

        var delayed = await _provider.AdvanceReconciliationCheckpointAsync(11, "01-bootstrap-complete");
        Assert.Equal("04-finalization-complete", delayed.Checkpoint);
    }

    [Fact]
    public async Task Lower_epochs_are_noops_and_higher_begin_resets_checkpoint()
    {
        await _provider.BeginReconciliationActivationEpochAsync(17);
        await _provider.AdvanceReconciliationCheckpointAsync(17, "04-finalization-complete");
        await _provider.CommitReconciliationActivationEpochAsync(17);

        var lower = await Task.WhenAll(
            _provider.BeginReconciliationActivationEpochAsync(16),
            _provider.AdvanceReconciliationCheckpointAsync(16, "07-provider-startup-actions-complete"),
            _provider.CommitReconciliationActivationEpochAsync(16));
        Assert.All(lower, state => Assert.True(state.IsActivatedFor(17)));
        Assert.All(lower, state => Assert.Equal("04-finalization-complete", state.Checkpoint));

        var higher = await _provider.BeginReconciliationActivationEpochAsync(18);
        Assert.Equal((18, ActivationEpochPhase.Activating, null),
            (higher.Epoch, higher.Phase, higher.Checkpoint));
    }

    [Fact]
    public async Task Cancellation_before_Lua_preserves_state_and_after_Lua_does_not_report_false_cancellation()
    {
        await _provider.BeginReconciliationActivationEpochAsync(21);
        using (var before = new CancellationTokenSource())
        {
            before.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                _provider.CommitReconciliationActivationEpochAsync(21, before.Token));
        }
        Assert.Equal(ActivationEpochPhase.Activating,
            (await _provider.GetReconciliationActivationStateAsync()).Phase);

        using var after = new CancellationTokenSource();
        _provider.AfterActivationMutationScriptEvaluatedAsync = () =>
        {
            after.Cancel();
            return Task.CompletedTask;
        };
        var committed = await _provider.CommitReconciliationActivationEpochAsync(21, after.Token);
        Assert.True(committed.IsActivatedFor(21));
    }

    [Fact]
    public async Task Corrupt_or_wrong_type_metadata_fails_before_mutation()
    {
        await _provider.BeginReconciliationActivationEpochAsync(31);
        await _db.HashSetAsync(RedisKeyBuilder.ReconciliationActivationMetadataKey,
            "phase", "not-a-phase");
        var corruptBefore = await _db.HashGetAllAsync(RedisKeyBuilder.ReconciliationActivationMetadataKey);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            _provider.AdvanceReconciliationCheckpointAsync(31, "01-bootstrap-complete"));
        Assert.Equal(corruptBefore.OrderBy(x => x.Name.ToString()),
            (await _db.HashGetAllAsync(RedisKeyBuilder.ReconciliationActivationMetadataKey))
                .OrderBy(x => x.Name.ToString()));

        await _db.KeyDeleteAsync(RedisKeyBuilder.ReconciliationActivationMetadataKey);
        await _db.StringSetAsync(RedisKeyBuilder.ReconciliationActivationMetadataKey, "wrong-type");
        await Assert.ThrowsAsync<RedisServerException>(() =>
            _provider.BeginReconciliationActivationEpochAsync(32));
        Assert.Equal("wrong-type",
            (string?)await _db.StringGetAsync(RedisKeyBuilder.ReconciliationActivationMetadataKey));
    }

    [Fact]
    public async Task Protocol_reads_and_acquisition_are_closed_while_activating_then_publish_on_commit()
    {
        var scope = new ReconciliationActivationScope("redis-activation-runtime");
        var provider = NewProvider("activation-node", 41, scope.ApplicationNamespace);
        var ticker = new TimeTickerEntity
        {
            Id = Guid.NewGuid(), Function = "ActivationFn", ExecutionTime = Now.AddMinutes(-1),
            Status = TickerStatus.Idle, CreatedAt = Now.AddHours(-1), UpdatedAt = Now.AddHours(-1), Request = []
        };
        var scopedKeys = new RedisKeyBuilder(new TickerQRuntimePartition(scope.ApplicationNamespace));
        await _db.StringSetAsync(scopedKeys.TimeTicker(ticker.Id),
            System.Text.Json.JsonSerializer.Serialize(ticker, TestJsonSerializerContext.Default.TimeTickerEntity));
        await _db.SetAddAsync(scopedKeys.TimeTickerIds, ticker.Id.ToString());
        await _db.SortedSetAddAsync(scopedKeys.TimeTickerPending, ticker.Id.ToString(), ticker.ExecutionTime!.Value.Ticks);
        await provider.BeginReconciliationActivationEpochAsync(scope, 41);

        Assert.Empty(await provider.GetEarliestTimeTickers());
        Assert.Empty(await provider.AcquireImmediateTimeTickersAsync([ticker.Id]));

        var racingAcquires = Enumerable.Range(0, 16)
            .Select(_ => NewProvider(Guid.NewGuid().ToString("N"), 41, scope.ApplicationNamespace)
                .AcquireImmediateTimeTickersAsync([ticker.Id]))
            .ToArray();
        var commit = provider.CommitReconciliationActivationEpochAsync(scope, 41);
        await Task.WhenAll(racingAcquires.Cast<Task>().Append(commit));

        Assert.True((await commit).IsActivatedFor(41));
        var winners = racingAcquires.Sum(task => task.Result.Length);
        if (winners == 0)
            winners = (await provider.AcquireImmediateTimeTickersAsync([ticker.Id])).Length;
        Assert.Equal(1, winners);
    }

    [Fact]
    public async Task Runnable_admissions_require_exact_activated_epoch_while_pre_epoch_setup_remains_compatible()
    {
        var scope = new ReconciliationActivationScope("redis-exact-runtime");
        var oldHost = NewProvider("old-host", 1, scope.ApplicationNamespace);
        var exactHost = NewProvider("exact-host", 2, scope.ApplicationNamespace);
        await oldHost.BeginReconciliationActivationEpochAsync(scope, 1);
        await oldHost.CommitReconciliationActivationEpochAsync(scope, 1);
        var immediate = NewTimeTicker();
        var onDemand = NewTimeTicker(TickerStatus.Done);
        await oldHost.AddTimeTickers([immediate, onDemand]);
        var cron = NewCron();
        await oldHost.InsertCronTickers([cron], CancellationToken.None);
        var raw = new CronTickerOccurrenceEntity<CronTickerEntity>
        {
            Id = Guid.NewGuid(), CronTickerId = cron.Id, DefinitionRevision = 1,
            ExecutionTime = Now.AddMinutes(5), Status = TickerStatus.Idle,
            CreatedAt = Now, UpdatedAt = Now
        };

        await exactHost.BeginReconciliationActivationEpochAsync(scope, 2);
        Assert.Empty(await oldHost.AcquireImmediateTimeTickersAsync([immediate.Id]));
        Assert.Empty(await exactHost.AcquireImmediateTimeTickersAsync([immediate.Id]));
        Assert.Null(await exactHost.AcquireTimeTickerOnDemandAsync(onDemand.Id, Now));
        Assert.Equal(0, await exactHost.InsertCronTickerOccurrences([raw], CancellationToken.None));
        Assert.Empty(await CollectAsync(exactHost.QueueCronTickerOccurrences(
            (Now.AddMinutes(6), [new InternalManagerContext(cron.Id)]), CancellationToken.None)));

        await exactHost.CommitReconciliationActivationEpochAsync(scope, 2);
        Assert.Empty(await oldHost.AcquireImmediateTimeTickersAsync([immediate.Id]));
        Assert.Null(await oldHost.AcquireTimeTickerOnDemandAsync(onDemand.Id, Now));
        Assert.Single(await exactHost.AcquireImmediateTimeTickersAsync([immediate.Id]));
        Assert.NotNull(await exactHost.AcquireTimeTickerOnDemandAsync(onDemand.Id, Now));
        Assert.Equal(1, await exactHost.InsertCronTickerOccurrences([raw], CancellationToken.None));
        Assert.Single(await CollectAsync(exactHost.QueueCronTickerOccurrences(
            (Now.AddMinutes(6), [new InternalManagerContext(cron.Id)]), CancellationToken.None)));
    }

    [Fact]
    public async Task Configured_runtime_scope_cannot_be_prebound_by_another_application()
    {
        var appA = new ReconciliationActivationScope("redis-scope-a");
        var appB = new ReconciliationActivationScope("redis-scope-b");
        ITickerPersistenceProvider<TimeTickerEntity, CronTickerEntity> provider =
            NewProvider("scoped", 5, appA.ApplicationNamespace);
        var ticker = NewTimeTicker();

        await provider.BeginReconciliationActivationEpochAsync(appB, 5);
        await provider.CommitReconciliationActivationEpochAsync(appB, 5);

        Assert.Equal(0, await provider.AddTimeTickers([ticker]));

        await provider.BeginReconciliationActivationEpochAsync(appA, 5);
        await provider.CommitReconciliationActivationEpochAsync(appA, 5);

        Assert.Equal(1, await provider.AddTimeTickers([ticker]));
        Assert.True((await provider.GetReconciliationActivationStateAsync(appA)).IsActivatedFor(5));
        Assert.True((await provider.GetReconciliationActivationStateAsync(appB)).IsActivatedFor(5));
        Assert.Single(await provider.AcquireImmediateTimeTickersAsync([ticker.Id]));
        Assert.False(await _db.KeyExistsAsync(RedisKeyBuilder.ReconciliationActivationMetadataKey));
    }

    [Fact]
    public async Task Runtime_epoch_is_immutable_after_provider_construction()
    {
        var options = new SchedulerOptionsBuilder { NodeIdentifier = "immutable", ReconciliationEpoch = 5 };
        var provider = new TickerRedisPersistenceProvider<TimeTickerEntity, CronTickerEntity>(
            _db, _clock, options, _redisOptions,
            NullLogger<TickerRedisPersistenceProvider<TimeTickerEntity, CronTickerEntity>>.Instance);
        await provider.BeginReconciliationActivationEpochAsync(5);
        await provider.CommitReconciliationActivationEpochAsync(5);
        options.ReconciliationEpoch = 6;
        var ticker = NewTimeTicker();

        Assert.Equal(1, await provider.AddTimeTickers([ticker]));
        Assert.Single(await provider.AcquireImmediateTimeTickersAsync([ticker.Id]));
    }

    [Fact]
    public async Task Queue_only_provider_is_not_rebound_by_scoped_protocol_calls()
    {
        var scope = new ReconciliationActivationScope("redis-protocol-only-scope");
        var provider = NewProvider("queue-only", 5);
        await provider.BeginReconciliationActivationEpochAsync(scope, 5);
        var ticker = NewTimeTicker();

        Assert.Equal(1, await provider.AddTimeTickers([ticker]));
        Assert.Equal(RedisKeyBuilder.ReconciliationActivationMetadataKey,
            provider.RuntimeActivationMetadataKeyForTest);
        Assert.NotNull(await provider.GetTimeTickerById(ticker.Id));
    }

    [Fact]
    public async Task Namespaced_queue_only_runtime_bypasses_all_activation_metadata()
    {
        var options = new SchedulerOptionsBuilder { NodeIdentifier = "namespaced-producer", ReconciliationEpoch = 5 };
        options.BindRuntimeActivationScope("redis-namespaced-producer", 5, schedulerEnabled: false);
        var provider = new TickerRedisPersistenceProvider<TimeTickerEntity, CronTickerEntity>(
            _db, _clock, options, _redisOptions,
            NullLogger<TickerRedisPersistenceProvider<TimeTickerEntity, CronTickerEntity>>.Instance);
        var ticker = NewTimeTicker();

        await provider.BeginReconciliationActivationEpochAsync(
            new ReconciliationActivationScope("redis-namespaced-producer"), 99);
        await provider.BeginReconciliationActivationEpochAsync(77);

        Assert.Equal(1, await provider.AddTimeTickers([ticker]));
        Assert.NotNull(await provider.GetTimeTickerById(ticker.Id));
    }

    [Fact]
    public async Task Scheduler_enabled_without_runtime_scope_fails_closed()
    {
        var options = new SchedulerOptionsBuilder { NodeIdentifier = "missing-scope", ReconciliationEpoch = 5 };
        Assert.Throws<InvalidOperationException>(() =>
            options.BindRuntimeActivationScope(null, 5, schedulerEnabled: true));
    }

    [Fact]
    public async Task Scoped_acquire_before_begin_rejects_without_mutating_real_lua()
    {
        var scope = new ReconciliationActivationScope("redis-before-begin");
        var provider = NewProvider("before-begin", 7, scope.ApplicationNamespace);
        var ticker = NewTimeTicker();
        var keys = new RedisKeyBuilder(new TickerQRuntimePartition(scope.ApplicationNamespace));
        await _db.StringSetAsync(keys.TimeTicker(ticker.Id),
            System.Text.Json.JsonSerializer.Serialize(ticker, TestJsonSerializerContext.Default.TimeTickerEntity));

        Assert.Empty(await provider.AcquireImmediateTimeTickersAsync([ticker.Id]));
        var persisted = await provider.GetTimeTickerById(ticker.Id);
        Assert.Equal(TickerStatus.Idle, persisted.Status);
        Assert.Null(persisted.AcquisitionToken);
    }

    [Fact]
    public async Task Nonterminal_cas_requires_exact_activated_runtime_epoch_real_lua()
    {
        var scope = new ReconciliationActivationScope("redis-cas-admission");
        var provider = NewProvider("cas-admission", 7, scope.ApplicationNamespace);
        var ticker = NewTimeTicker();
        // This test seeds raw JSON, bypassing the provider's fixed-width DateTime converter.
        // A non-zero final tick keeps System.Text.Json and the Lua CAS "O" comparand byte-identical.
        ticker.UpdatedAt = ticker.UpdatedAt.AddTicks(1);
        var keys = new RedisKeyBuilder(new TickerQRuntimePartition(scope.ApplicationNamespace));
        await _db.StringSetAsync(keys.TimeTicker(ticker.Id),
            System.Text.Json.JsonSerializer.Serialize(ticker, TestJsonSerializerContext.Default.TimeTickerEntity));
        var update = new InternalFunctionContext { TickerId = ticker.Id, Type = TickerType.TimeTicker }
            .SetProperty(x => x.RetryCount, 42);

        Assert.Equal(0, await provider.UpdateTimeTicker(update));
        await provider.BeginReconciliationActivationEpochAsync(scope, 7);
        Assert.Equal(0, await provider.UpdateTimeTicker(update));
        await provider.CommitReconciliationActivationEpochAsync(scope, 7);
        Assert.Equal(1, await provider.UpdateTimeTicker(update));
        await provider.BeginReconciliationActivationEpochAsync(scope, 8);
        await provider.CommitReconciliationActivationEpochAsync(scope, 8);
        Assert.Equal(0, await provider.UpdateTimeTicker(
            new InternalFunctionContext { TickerId = ticker.Id, Type = TickerType.TimeTicker }
                .SetProperty(x => x.RetryCount, 43)));
    }

    [Fact]
    public async Task Public_time_ticker_update_is_atomically_admitted_and_cleans_result()
    {
        var scope = new ReconciliationActivationScope("redis-public-update");
        var provider = NewProvider("public-update", 7, scope.ApplicationNamespace);
        var ticker = NewTimeTicker();
        var keys = new RedisKeyBuilder(new TickerQRuntimePartition(scope.ApplicationNamespace));
        await _db.StringSetAsync(keys.TimeTicker(ticker.Id),
            System.Text.Json.JsonSerializer.Serialize(ticker, TestJsonSerializerContext.Default.TimeTickerEntity));
        await _db.SetAddAsync(keys.TimeTickerIds, ticker.Id.ToString());
        await _db.StringSetAsync(keys.TimeTickerResult(ticker.Id), "stale-result");
        ticker.ExecutionTime = Now.AddMinutes(5);

        Assert.Equal(0, await provider.UpdateTimeTickers([ticker], CancellationToken.None));
        await provider.BeginReconciliationActivationEpochAsync(scope, 7);
        Assert.Equal(0, await provider.UpdateTimeTickers([ticker], CancellationToken.None));
        Assert.True(await _db.KeyExistsAsync(keys.TimeTickerResult(ticker.Id)));

        await provider.CommitReconciliationActivationEpochAsync(scope, 7);
        Assert.Equal(1, await provider.UpdateTimeTickers([ticker], CancellationToken.None));
        Assert.False(await _db.KeyExistsAsync(keys.TimeTickerResult(ticker.Id)));
        Assert.Equal(RedisKeyBuilder.ToScore(ticker.ExecutionTime.Value),
            await _db.SortedSetScoreAsync(keys.TimeTickerPending, ticker.Id.ToString()));
    }

    [Fact]
    public async Task Scoped_cron_mutation_denies_absent_and_activating_metadata_then_allows_activated()
    {
        var scope = new ReconciliationActivationScope("redis-scoped-cron-mutation");
        var provider = NewProvider("scoped-cron", 7, scope.ApplicationNamespace);
        var cron = NewCron();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            provider.InsertCronTickers([cron], CancellationToken.None));
        await provider.BeginReconciliationActivationEpochAsync(scope, 7);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            provider.InsertCronTickers([cron], CancellationToken.None));
        await provider.CommitReconciliationActivationEpochAsync(scope, 7);
        Assert.Equal(1, await provider.InsertCronTickers([cron], CancellationToken.None));
    }

    [Fact]
    public async Task Startup_capability_manifest_and_public_cron_revision_are_exactly_admitted()
    {
        const string applicationNamespace = "redis-startup-crud";
        var scope = new ReconciliationActivationScope(applicationNamespace);
        var provider = NewProvider("startup-crud", 1, applicationNamespace);
        await provider.BeginReconciliationActivationEpochAsync(scope, 1);
        var cron = NewCron();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            provider.InsertCronTickers([cron], CancellationToken.None));
        using (StartupSeederAdmissionContext.Enter(new ReconciliationActivationScope("wrong").ScopeKey, 1))
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                provider.InsertCronTickers([cron], CancellationToken.None));
        using (StartupSeederAdmissionContext.Enter(scope.ScopeKey, 1))
            Assert.Equal(1, await provider.InsertCronTickers([cron], CancellationToken.None));

        var seed = new DefinedCronTickerSeed("redis-manifest", "*/7 * * * *");
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
            Status = TickerStatus.Idle, ExecutionTime = Now.AddMinutes(1),
            CreatedAt = Now, UpdatedAt = Now
        };
        Assert.Equal(1, await provider.InsertCronTickerOccurrences([occurrence], CancellationToken.None));
        cron.Expression = "*/9 * * * *";
        Assert.Equal(1, await provider.UpdateCronTickers([cron], CancellationToken.None));
        Assert.Equal(2, cron.DefinitionRevision);
        Assert.Equal(TickerStatus.Skipped,
            (await provider.GetAllCronTickerOccurrences(x => x.Id == occurrence.Id)).Single().Status);

        await provider.BeginReconciliationActivationEpochAsync(scope, 2);
        Assert.Equal(0, await provider.RemoveCronTickers([cron.Id], CancellationToken.None));
    }

    [Fact]
    public async Task Invalid_scheduler_mode_rejects_public_cron_without_writing()
    {
        var options = new SchedulerOptionsBuilder { ReconciliationEpoch = 1 };
        Assert.Throws<InvalidOperationException>(() =>
            options.BindRuntimeActivationScope(null, 1, schedulerEnabled: true));
    }

    [Fact]
    public async Task Scoped_occurrence_publication_denies_absent_and_activating_metadata_then_allows_activated()
    {
        var scope = new ReconciliationActivationScope("redis-scoped-occurrence-publication");
        var provider = NewProvider("scoped-occurrence", 7, scope.ApplicationNamespace);
        var cron = NewCron();
        var keys = new RedisKeyBuilder(new TickerQRuntimePartition(scope.ApplicationNamespace));
        await _db.StringSetAsync(keys.Cron(cron.Id),
            System.Text.Json.JsonSerializer.Serialize(cron, TestJsonSerializerContext.Default.CronTickerEntity));
        var occurrence = new CronTickerOccurrenceEntity<CronTickerEntity>
        {
            Id = Guid.NewGuid(), CronTickerId = cron.Id, DefinitionRevision = cron.DefinitionRevision,
            ExecutionTime = Now.AddMinutes(5), Status = TickerStatus.Idle,
            CreatedAt = Now, UpdatedAt = Now
        };

        Assert.Equal(0, await provider.InsertCronTickerOccurrences([occurrence], CancellationToken.None));
        await provider.BeginReconciliationActivationEpochAsync(scope, 7);
        Assert.Equal(0, await provider.InsertCronTickerOccurrences([occurrence], CancellationToken.None));
        await provider.CommitReconciliationActivationEpochAsync(scope, 7);
        Assert.Equal(1, await provider.InsertCronTickerOccurrences([occurrence], CancellationToken.None));
    }

    [Fact]
    public async Task Scoped_queries_begin_and_legacy_calls_cannot_redirect_bound_runtime_scope()
    {
        var appA = new ReconciliationActivationScope("redis-runtime-a");
        var appB = new ReconciliationActivationScope("redis-query-b");
        ITickerPersistenceProvider<TimeTickerEntity, CronTickerEntity> provider =
            NewProvider("runtime-scope", 5, appA.ApplicationNamespace);
        var ticker = NewTimeTicker();

        await provider.BeginReconciliationActivationEpochAsync(appA, 5);
        await provider.CommitReconciliationActivationEpochAsync(appA, 5);
        await provider.AddTimeTickers([ticker]);
        _ = await provider.GetReconciliationActivationStateAsync(appB);
        await provider.BeginReconciliationActivationEpochAsync(appB, 5);
        await provider.BeginReconciliationActivationEpochAsync(5);

        Assert.Single(await provider.AcquireImmediateTimeTickersAsync([ticker.Id]));
        Assert.Equal(ActivationEpochPhase.Activating,
            (await provider.GetReconciliationActivationStateAsync(appB)).Phase);
    }

    [Fact]
    public async Task Acquired_terminal_commit_survives_activation_epoch_change_but_stale_generation_still_loses()
    {
        var scope = new ReconciliationActivationScope("redis-terminal-scope");
        ITickerPersistenceProvider<TimeTickerEntity, CronTickerEntity> provider =
            NewProvider("terminal-owner", 7, scope.ApplicationNamespace);
        await provider.BeginReconciliationActivationEpochAsync(scope, 7);
        await provider.CommitReconciliationActivationEpochAsync(scope, 7);
        var ticker = NewTimeTicker();
        await provider.AddTimeTickers([ticker]);
        var acquired = Assert.Single(await provider.AcquireImmediateTimeTickersAsync([ticker.Id]));

        await provider.BeginReconciliationActivationEpochAsync(scope, 8);
        var stale = new InternalFunctionContext()
            .SetProperty(x => x.TickerId, ticker.Id)
            .SetProperty(x => x.Type, TickerType.TimeTicker)
            .SetProperty(x => x.AcquisitionToken, Guid.NewGuid())
            .SetProperty(x => x.ResultEnvelope, (TickerResultEnvelope)null)
            .SetProperty(x => x.Status, TickerStatus.Done);
        stale.RuntimePartitionKey = scope.RuntimePartition.StorageKey;
        Assert.False(await provider.CommitTerminalTickerAsync(stale));

        var winning = new InternalFunctionContext()
            .SetProperty(x => x.TickerId, ticker.Id)
            .SetProperty(x => x.Type, TickerType.TimeTicker)
            .SetProperty(x => x.AcquisitionToken, acquired.AcquisitionToken)
            .SetProperty(x => x.ResultEnvelope, (TickerResultEnvelope)null)
            .SetProperty(x => x.Status, TickerStatus.Done);
        winning.RuntimePartitionKey = scope.RuntimePartition.StorageKey;
        Assert.True(await provider.CommitTerminalTickerAsync(winning));
        Assert.Equal(TickerStatus.Done, (await provider.GetTimeTickerById(ticker.Id)).Status);
    }

    [Fact]
    public async Task Release_is_rejected_atomically_when_selected_scope_is_activating()
    {
        var scope = new ReconciliationActivationScope("redis-release-scope");
        ITickerPersistenceProvider<TimeTickerEntity, CronTickerEntity> provider =
            NewProvider("release-owner", 9, scope.ApplicationNamespace);
        await provider.BeginReconciliationActivationEpochAsync(scope, 9);
        await provider.CommitReconciliationActivationEpochAsync(scope, 9);
        var ticker = NewTimeTicker();
        await provider.AddTimeTickers([ticker]);
        Assert.Single(await provider.AcquireImmediateTimeTickersAsync([ticker.Id]));

        await provider.BeginReconciliationActivationEpochAsync(scope, 10);
        await provider.ReleaseAcquiredTimeTickers([ticker.Id]);

        Assert.Equal(TickerStatus.InProgress, (await provider.GetTimeTickerById(ticker.Id)).Status);
    }

    private static async Task<CronTickerOccurrenceEntity<CronTickerEntity>[]> CollectAsync(
        IAsyncEnumerable<CronTickerOccurrenceEntity<CronTickerEntity>> source)
    {
        var rows = new List<CronTickerOccurrenceEntity<CronTickerEntity>>();
        await foreach (var row in source) rows.Add(row);
        return rows.ToArray();
    }

    [Fact]
    public async Task Initializer_activation_publication_failure_leaves_local_gate_closed()
    {
        const string applicationNamespace = "redis-initializer-failure";
        var scope = new ReconciliationActivationScope(applicationNamespace);
        var activationKey = RedisKeyBuilder.ReconciliationActivationMetadataKeyForScope(scope.ScopeKey);
        await _db.StringSetAsync(activationKey, "wrong-type");
        var manager = Substitute.For<IInternalTickerManager>();
        manager.SupportsReconciliationActivationEpoch.Returns(true);
        manager.SupportsAuthoritativeCronReconciliation.Returns(true);
        manager.BeginReconciliationActivationEpochAsync(
                Arg.Any<ReconciliationActivationScope>(), 1, Arg.Any<CancellationToken>())
            .Returns(call => _provider.BeginReconciliationActivationEpochAsync(
                call.Arg<ReconciliationActivationScope>(), 1, call.Arg<CancellationToken>()));
        var gate = new TickerQActivationGate();
        var context = new TickerExecutionContext();
        new TickerOptionsBuilder<TimeTickerEntity, CronTickerEntity>(context, new SchedulerOptionsBuilder())
            .UseDefinedCronApplicationNamespace(applicationNamespace)
            .UseReconciliationEpoch(1);
        var configuration = Substitute.For<IConfiguration>();
        var services = new ServiceCollection();
        services.AddSingleton(context);
        services.AddSingleton(configuration);
        services.AddSingleton(manager);
        services.AddSingleton(new SchedulerOptionsBuilder());
        services.AddSingleton<ITickerQActivationGate>(gate);
        var initializer = new TickerQInitializerHostedService(
            context, services.BuildServiceProvider(), configuration)
        {
            InitializationRequested = true
        };

        await Assert.ThrowsAsync<RedisServerException>(() => initializer.StartAsync(CancellationToken.None));
        Assert.False(gate.IsActivated);
        Assert.Equal("wrong-type",
            (string?)await _db.StringGetAsync(activationKey));
    }

    [Fact]
    public async Task Redis_Cluster_fails_before_activation_claim_with_precise_cross_slot_error()
    {
        var db = Substitute.For<IDatabase>();
        var mux = Substitute.For<IConnectionMultiplexer>();
        var server = Substitute.For<IServer>();
        var endpoint = new DnsEndPoint("cluster.example", 6379);
        db.Multiplexer.Returns(mux);
        mux.GetEndPoints(Arg.Any<bool>()).Returns([endpoint]);
        mux.GetServer(endpoint, Arg.Any<object>()).Returns(server);
        server.IsConnected.Returns(true);
        server.ServerType.Returns(ServerType.Cluster);
        var provider = new TickerRedisPersistenceProvider<TimeTickerEntity, CronTickerEntity>(
            db, _clock, new SchedulerOptionsBuilder { ReconciliationEpoch = 1 }, _redisOptions,
            NullLogger<TickerRedisPersistenceProvider<TimeTickerEntity, CronTickerEntity>>.Instance);

        var error = await Assert.ThrowsAsync<NotSupportedException>(() =>
            provider.BeginReconciliationActivationEpochAsync(1));
        Assert.Contains("do not share a Redis Cluster hash slot", error.Message, StringComparison.Ordinal);
        Assert.Contains("Activation was not claimed", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(db.ReceivedCalls(), call =>
            call.GetMethodInfo().Name == nameof(IDatabase.ScriptEvaluateAsync));
    }

    [Fact]
    public async Task Queue_only_cluster_enqueue_uses_explicit_non_atomic_producer_path()
    {
        var db = Substitute.For<IDatabase>();
        var mux = Substitute.For<IConnectionMultiplexer>();
        var server = Substitute.For<IServer>();
        var endpoint = new DnsEndPoint("cluster-producer.example", 6379);
        db.Multiplexer.Returns(mux);
        mux.GetEndPoints(Arg.Any<bool>()).Returns([endpoint]);
        mux.GetServer(endpoint, Arg.Any<object>()).Returns(server);
        server.IsConnected.Returns(true);
        server.ServerType.Returns(ServerType.Cluster);
        db.StringSetAsync(Arg.Any<RedisKey>(), Arg.Any<RedisValue>(), Arg.Any<TimeSpan?>(),
            Arg.Any<bool>(), Arg.Any<When>(), Arg.Any<CommandFlags>()).Returns(true);
        var provider = new TickerRedisPersistenceProvider<TimeTickerEntity, CronTickerEntity>(
            db, _clock, new SchedulerOptionsBuilder { ReconciliationEpoch = 1 }, _redisOptions,
            NullLogger<TickerRedisPersistenceProvider<TimeTickerEntity, CronTickerEntity>>.Instance);
        var ticker = NewTimeTicker();

        Assert.Equal(1, await provider.AddTimeTickers([ticker]));
        Assert.DoesNotContain(db.ReceivedCalls(), call =>
            call.GetMethodInfo().Name == nameof(IDatabase.ScriptEvaluateAsync));
        Assert.Contains(db.ReceivedCalls(), call => call.GetMethodInfo().Name == nameof(IDatabase.StringSetAsync));
        Assert.Contains(db.ReceivedCalls(), call => call.GetMethodInfo().Name == nameof(IDatabase.SetAddAsync));
        Assert.Contains(db.ReceivedCalls(), call => call.GetMethodInfo().Name == nameof(IDatabase.SortedSetAddAsync));
    }

    [Fact]
    public async Task Queue_only_cluster_cron_insert_uses_cluster_safe_single_key_commands()
    {
        var db = Substitute.For<IDatabase>();
        var mux = Substitute.For<IConnectionMultiplexer>();
        var server = Substitute.For<IServer>();
        var endpoint = new DnsEndPoint("cluster-cron-producer.example", 6379);
        db.Multiplexer.Returns(mux);
        mux.GetEndPoints(Arg.Any<bool>()).Returns([endpoint]);
        mux.GetServer(endpoint, Arg.Any<object>()).Returns(server);
        server.IsConnected.Returns(true);
        server.ServerType.Returns(ServerType.Cluster);
        db.StringSetAsync(Arg.Any<RedisKey>(), Arg.Any<RedisValue>(), Arg.Any<TimeSpan?>(),
            Arg.Any<bool>(), Arg.Any<When>(), Arg.Any<CommandFlags>()).Returns(true);
        var options = new SchedulerOptionsBuilder { ReconciliationEpoch = 1 };
        options.BindRuntimeActivationScope("cluster-producer", 1, schedulerEnabled: false);
        var provider = new TickerRedisPersistenceProvider<TimeTickerEntity, CronTickerEntity>(
            db, _clock, options, _redisOptions,
            NullLogger<TickerRedisPersistenceProvider<TimeTickerEntity, CronTickerEntity>>.Instance);

        Assert.Equal(1, await provider.InsertCronTickers([NewCron()], CancellationToken.None));
        Assert.DoesNotContain(db.ReceivedCalls(), call =>
            call.GetMethodInfo().Name == nameof(IDatabase.ScriptEvaluateAsync));
        Assert.Contains(db.ReceivedCalls(), call => call.GetMethodInfo().Name == nameof(IDatabase.StringSetAsync));
        Assert.Contains(db.ReceivedCalls(), call => call.GetMethodInfo().Name == nameof(IDatabase.SetAddAsync));
    }

    [Fact]
    public async Task Queue_only_cluster_cron_update_and_remove_use_explicit_cluster_safe_commands()
    {
        var db = Substitute.For<IDatabase>();
        var mux = Substitute.For<IConnectionMultiplexer>();
        var server = Substitute.For<IServer>();
        var endpoint = new DnsEndPoint("cluster-cron-mutations.example", 6379);
        db.Multiplexer.Returns(mux);
        mux.GetEndPoints(Arg.Any<bool>()).Returns([endpoint]);
        mux.GetServer(endpoint, Arg.Any<object>()).Returns(server);
        server.IsConnected.Returns(true);
        server.ServerType.Returns(ServerType.Cluster);
        var cron = NewCron();
        db.StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>()).Returns(
            System.Text.Json.JsonSerializer.Serialize(cron, TestJsonSerializerContext.Default.CronTickerEntity));
        db.StringSetAsync(Arg.Any<RedisKey>(), Arg.Any<RedisValue>(), Arg.Any<TimeSpan?>(),
            Arg.Any<bool>(), Arg.Any<When>(), Arg.Any<CommandFlags>()).Returns(true);
        db.KeyDeleteAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>()).Returns(true);
        db.ScriptEvaluateAsync(Arg.Any<string>(), Arg.Any<RedisKey[]>(), Arg.Any<RedisValue[]>(),
            Arg.Any<CommandFlags>()).Returns(RedisResult.Create((RedisValue)1));
        var options = new SchedulerOptionsBuilder { ReconciliationEpoch = 1 };
        options.BindRuntimeActivationScope("cluster-producer", 1, schedulerEnabled: false);
        var provider = new TickerRedisPersistenceProvider<TimeTickerEntity, CronTickerEntity>(
            db, _clock, options, _redisOptions,
            NullLogger<TickerRedisPersistenceProvider<TimeTickerEntity, CronTickerEntity>>.Instance);

        cron.Expression = "*/9 * * * *";
        Assert.Equal(1, await provider.UpdateCronTickers([cron], CancellationToken.None));
        Assert.Equal(2, cron.DefinitionRevision);
        Assert.Equal(1, await provider.RemoveCronTickers([cron.Id], CancellationToken.None));

        var scriptCall = Assert.Single(db.ReceivedCalls().Where(call =>
            call.GetMethodInfo().Name == nameof(IDatabase.ScriptEvaluateAsync)));
        Assert.Single((RedisKey[])scriptCall.GetArguments()[1]!);
        Assert.Contains(db.ReceivedCalls(), call => call.GetMethodInfo().Name == nameof(IDatabase.KeyDeleteAsync));
        Assert.Contains(db.ReceivedCalls(), call => call.GetMethodInfo().Name == nameof(IDatabase.SetRemoveAsync));
    }

    [Fact]
    public async Task Unknown_or_disconnected_topology_fails_closed_before_any_script()
    {
        var db = Substitute.For<IDatabase>();
        var mux = Substitute.For<IConnectionMultiplexer>();
        var server = Substitute.For<IServer>();
        var endpoint = new DnsEndPoint("unknown.example", 6379);
        db.Multiplexer.Returns(mux);
        mux.GetEndPoints(Arg.Any<bool>()).Returns([endpoint]);
        mux.GetServer(endpoint, Arg.Any<object>()).Returns(server);
        server.IsConnected.Returns(false);
        server.ServerType.Returns(ServerType.Standalone);
        var provider = new TickerRedisPersistenceProvider<TimeTickerEntity, CronTickerEntity>(
            db, _clock, new SchedulerOptionsBuilder { ReconciliationEpoch = 9 }, _redisOptions,
            NullLogger<TickerRedisPersistenceProvider<TimeTickerEntity, CronTickerEntity>>.Instance);

        await Assert.ThrowsAsync<NotSupportedException>(() =>
            provider.AcquireImmediateTimeTickersAsync([Guid.NewGuid()]));
        Assert.DoesNotContain(db.ReceivedCalls(), call =>
            call.GetMethodInfo().Name == nameof(IDatabase.ScriptEvaluateAsync));
    }
}
