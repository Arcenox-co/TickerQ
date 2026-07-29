using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using TickerQ.EntityFrameworkCore.Entities;
using TickerQ.EntityFrameworkCore.Infrastructure;
using TickerQ.Utilities;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Models;

namespace TickerQ.EntityFrameworkCore.Tests.Infrastructure;

public sealed class EfCoreNodeFinalizationOutboxTests : IAsyncLifetime
{
    private readonly DateTime _now = new(2026, 7, 29, 12, 0, 0, DateTimeKind.Utc);
    private string _databasePath = null!;
    private DbContextOptions<TestTickerQDbContext> _options = null!;
    private ServiceProvider _services = null!;
    private TestableProvider _provider = null!;

    public async Task InitializeAsync()
    {
        _databasePath = Path.Combine(Path.GetTempPath(), $"tickerq-node-outbox-{Guid.NewGuid():N}.db");
        _options = new DbContextOptionsBuilder<TestTickerQDbContext>()
            .UseSqlite($"Data Source={_databasePath}")
            .Options;
        await using (var context = new TestTickerQDbContext(_options))
            await context.Database.EnsureCreatedAsync();
        _services = BuildServices();
        _provider = CreateProvider(_services, "outbox-node");
    }

    public async Task DisposeAsync()
    {
        await _services.DisposeAsync();
        if (File.Exists(_databasePath)) File.Delete(_databasePath);
    }

    [Fact]
    public void Model_maps_bounded_non_cascading_outbox_with_required_indices()
    {
        using var context = new TestTickerQDbContext(_options);
        var entity = context.Model.FindEntityType(typeof(NodeFinalizationOutboxEntity));

        Assert.NotNull(entity);
        Assert.Equal("NodeFinalizationOutbox", entity!.GetTableName());
        Assert.Empty(entity.GetForeignKeys());
        Assert.Equal(nameof(NodeFinalizationOutboxEntity.OutboxId), Assert.Single(entity.FindPrimaryKey()!.Properties).Name);
        Assert.Contains(entity.GetIndexes(), x => x.IsUnique && x.Properties.Select(p => p.Name).SequenceEqual(
            [nameof(NodeFinalizationOutboxEntity.TickerType), nameof(NodeFinalizationOutboxEntity.TickerId),
             nameof(NodeFinalizationOutboxEntity.AcquisitionToken), nameof(NodeFinalizationOutboxEntity.DispatchId),
             nameof(NodeFinalizationOutboxEntity.NodeEpoch)]));
        Assert.Contains(entity.GetIndexes(), x => x.Properties.Select(p => p.Name).SequenceEqual(
            [nameof(NodeFinalizationOutboxEntity.AvailableAtUtc), nameof(NodeFinalizationOutboxEntity.OutboxId)]));
        Assert.Equal(NodeFinalizationIntent.MaxExactBodyBytes,
            entity.FindProperty(nameof(NodeFinalizationOutboxEntity.ExactBody))!.GetMaxLength());
        Assert.Equal(typeof(long),
            entity.FindProperty(nameof(NodeFinalizationOutboxEntity.CreatedAtUtcTicks))!.ClrType);
        Assert.Equal(32,
            entity.FindProperty(nameof(NodeFinalizationOutboxEntity.TerminalMutationDigest))!.GetMaxLength());
        Assert.Equal(NodeFinalizationIntent.MaxUriLength,
            entity.FindProperty(nameof(NodeFinalizationOutboxEntity.FinalizeUri))!.GetMaxLength());
        Assert.Equal(NodeFinalizationClaim.MaxClaimedByLength,
            entity.FindProperty(nameof(NodeFinalizationOutboxEntity.ClaimedBy))!.GetMaxLength());
        Assert.Equal(NodeFinalizationOperationalState.MaxErrorCodeLength,
            entity.FindProperty(nameof(NodeFinalizationOutboxEntity.LastErrorCode))!.GetMaxLength());
        Assert.False(_provider.SupportsDurableNodeFinalizationOutbox);
    }

    [Fact]
    public async Task Readiness_probe_enables_outbox_only_after_model_and_schema_access_succeed()
    {
        var readiness = new EfCoreNodeFinalizationOutboxReadiness();
        await using var services = BuildServices(readiness);
        var provider = CreateProvider(services, "ready-node");
        var probe = new EfCoreNodeFinalizationOutboxReadinessProbe<TestTickerQDbContext>(services, readiness);

        Assert.False(provider.SupportsDurableNodeFinalizationOutbox);
        await probe.BootstrapAsync();
        Assert.True(provider.SupportsDurableNodeFinalizationOutbox);
    }

    [Fact]
    public async Task Missing_outbox_schema_logs_error_and_keeps_non_node_persistence_available()
    {
        var missingPath = Path.Combine(Path.GetTempPath(), $"tickerq-node-outbox-missing-{Guid.NewGuid():N}.db");
        try
        {
            var options = new DbContextOptionsBuilder<TestTickerQDbContext>()
                .UseSqlite($"Data Source={missingPath}").Options;
            var readiness = new EfCoreNodeFinalizationOutboxReadiness();
            var logger = new RecordingLogger<EfCoreNodeFinalizationOutboxReadinessProbe<TestTickerQDbContext>>();
            var services = new ServiceCollection()
                .AddSingleton<IDbContextFactory<TestTickerQDbContext>>(
                    new PooledDbContextFactory<TestTickerQDbContext>(options))
                .BuildServiceProvider();
            await using (services)
            {
                await using (var context = new TestTickerQDbContext(options))
                {
                    await context.Database.EnsureCreatedAsync();
                    await context.Database.ExecuteSqlRawAsync("DROP TABLE NodeFinalizationOutbox");
                }
                var probe = new EfCoreNodeFinalizationOutboxReadinessProbe<TestTickerQDbContext>(
                    services, readiness, logger);

                await probe.BootstrapAsync();

                Assert.False(readiness.IsReady);
                var error = Assert.Single(logger.Entries, x => x.Level == LogLevel.Error);
                Assert.Contains("NodeFinalizationOutbox", error.Message);
                Assert.Contains("migration", error.Message, StringComparison.OrdinalIgnoreCase);

                var provider = CreateProvider(services, "non-node-host");
                var ticker = new TimeTickerEntity
                {
                    Id = Guid.NewGuid(), Function = "Ordinary", Status = TickerStatus.Idle,
                    ExecutionTime = _now, Request = [], CreatedAt = _now, UpdatedAt = _now
                };
                await provider.AddTimeTickers([ticker], CancellationToken.None);
                await using (var verify = new TestTickerQDbContext(options))
                    Assert.Equal(ticker.Id, (await verify.Set<TimeTickerEntity>().SingleAsync()).Id);

                // The latch can recover if an external migration repairs the schema later.
                await using (var repair = new TestTickerQDbContext(options))
                {
                    await repair.Database.EnsureDeletedAsync();
                    await repair.Database.EnsureCreatedAsync();
                }
                await probe.BootstrapAsync();
                Assert.True(readiness.IsReady);
            }
        }
        finally
        {
            if (File.Exists(missingPath)) File.Delete(missingPath);
        }
    }

    [Fact]
    public async Task Partial_outbox_schema_logs_error_and_leaves_readiness_false()
    {
        var partialPath = Path.Combine(Path.GetTempPath(), $"tickerq-node-outbox-partial-{Guid.NewGuid():N}.db");
        try
        {
            var options = new DbContextOptionsBuilder<TestTickerQDbContext>()
                .UseSqlite($"Data Source={partialPath}").Options;
            var readiness = new EfCoreNodeFinalizationOutboxReadiness();
            var logger = new RecordingLogger<EfCoreNodeFinalizationOutboxReadinessProbe<TestTickerQDbContext>>();
            await using var services = new ServiceCollection()
                .AddSingleton<IDbContextFactory<TestTickerQDbContext>>(
                    new PooledDbContextFactory<TestTickerQDbContext>(options))
                .BuildServiceProvider();

            await using (var context = new TestTickerQDbContext(options))
            {
                await context.Database.EnsureCreatedAsync();
                await context.Database.ExecuteSqlRawAsync(
                    "ALTER TABLE NodeFinalizationOutbox DROP COLUMN TerminalMutationDigest");
            }

            var probe = new EfCoreNodeFinalizationOutboxReadinessProbe<TestTickerQDbContext>(
                services, readiness, logger);
            await probe.BootstrapAsync();

            Assert.False(readiness.IsReady);
            var error = Assert.Single(logger.Entries, x => x.Level == LogLevel.Error);
            Assert.Contains("NodeFinalizationOutbox", error.Message);
            Assert.Contains("migration", error.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (File.Exists(partialPath)) File.Delete(partialPath);
        }
    }

    [Fact]
    public async Task Missing_outbox_model_logs_error_and_leaves_readiness_false()
    {
        var options = new DbContextOptionsBuilder<MissingOutboxDbContext>()
            .UseSqlite("Data Source=:memory:").Options;
        var readiness = new EfCoreNodeFinalizationOutboxReadiness();
        var logger = new RecordingLogger<EfCoreNodeFinalizationOutboxReadinessProbe<MissingOutboxDbContext>>();
        await using var services = new ServiceCollection()
            .AddSingleton<IDbContextFactory<MissingOutboxDbContext>>(
                new PooledDbContextFactory<MissingOutboxDbContext>(options))
            .BuildServiceProvider();
        var probe = new EfCoreNodeFinalizationOutboxReadinessProbe<MissingOutboxDbContext>(
            services, readiness, logger);

        await probe.BootstrapAsync();

        Assert.False(readiness.IsReady);
        var error = Assert.Single(logger.Entries, x => x.Level == LogLevel.Error);
        Assert.Contains("model", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("NodeFinalizationOutbox", error.Message);
    }

    [Fact]
    public async Task Readiness_probe_propagates_cancellation_and_does_not_mark_ready()
    {
        var readiness = new EfCoreNodeFinalizationOutboxReadiness();
        await using var services = BuildServices(readiness);
        var probe = new EfCoreNodeFinalizationOutboxReadinessProbe<TestTickerQDbContext>(services, readiness);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            probe.BootstrapAsync(cancellation.Token));

        Assert.False(readiness.IsReady);
    }

    [Fact]
    public async Task Stale_generation_changes_neither_ticker_result_nor_outbox()
    {
        var ticker = await AddAndAcquireTimeTickerAsync();
        var stale = Guid.NewGuid();
        var intent = Intent(TickerType.TimeTicker, ticker.Id, stale);

        Assert.False(await _provider.CommitTerminalTickerAndEnqueueNodeFinalizationAsync(
            Success(TickerType.TimeTicker, ticker.Id, stale, Envelope("stale")), intent));

        await using var verify = new TestTickerQDbContext(_options);
        Assert.Equal(TickerStatus.InProgress, (await verify.Set<TimeTickerEntity>().SingleAsync()).Status);
        Assert.Empty(await verify.Set<TimeTickerResultEntity<TimeTickerEntity>>().ToListAsync());
        Assert.Empty(await verify.Set<NodeFinalizationOutboxEntity>().ToListAsync());
    }

    [Theory]
    [InlineData(TickerStatus.Done)]
    [InlineData(TickerStatus.Failed)]
    [InlineData(TickerStatus.Cancelled)]
    public async Task Accepted_time_terminal_status_and_outbox_commit_atomically(TickerStatus status)
    {
        var acquired = await AddAndAcquireTimeTickerAsync();
        var intent = Intent(TickerType.TimeTicker, acquired.Id, acquired.AcquisitionToken!.Value);
        var context = Terminal(TickerType.TimeTicker, acquired.Id, acquired.AcquisitionToken, status,
            status == TickerStatus.Done ? Envelope("result") : null);

        Assert.True(await _provider.CommitTerminalTickerAndEnqueueNodeFinalizationAsync(context, intent));

        await using var verify = new TestTickerQDbContext(_options);
        Assert.Equal(status, (await verify.Set<TimeTickerEntity>().SingleAsync()).Status);
        Assert.Single(await verify.Set<NodeFinalizationOutboxEntity>().ToListAsync());
        if (status == TickerStatus.Done)
            Assert.Equal("result", Encoding.UTF8.GetString((await verify.Set<TimeTickerResultEntity<TimeTickerEntity>>().SingleAsync()).Payload));
        else
            Assert.Empty(await verify.Set<TimeTickerResultEntity<TimeTickerEntity>>().ToListAsync());
    }

    [Fact]
    public async Task Exact_duplicate_is_idempotently_acknowledged_but_same_id_mismatch_fails_closed()
    {
        var acquired = await AddAndAcquireTimeTickerAsync();
        var intent = Intent(TickerType.TimeTicker, acquired.Id, acquired.AcquisitionToken!.Value);
        var context = Success(TickerType.TimeTicker, acquired.Id, acquired.AcquisitionToken, Envelope("once"));

        Assert.True(await _provider.CommitTerminalTickerAndEnqueueNodeFinalizationAsync(context, intent));
        Assert.True(await _provider.CommitTerminalTickerAndEnqueueNodeFinalizationAsync(context, intent));
        var mismatch = Intent(TickerType.TimeTicker, acquired.Id, acquired.AcquisitionToken.Value,
            intent.DispatchId, requestNonce: Guid.NewGuid());
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _provider.CommitTerminalTickerAndEnqueueNodeFinalizationAsync(context, mismatch));

        await using var verify = new TestTickerQDbContext(_options);
        Assert.Single(await verify.Set<NodeFinalizationOutboxEntity>().ToListAsync());
    }

    [Fact]
    public async Task Duplicate_outbox_rejects_different_terminal_status_or_result_mutation()
    {
        var acquired = await AddAndAcquireTimeTickerAsync();
        var intent = Intent(TickerType.TimeTicker, acquired.Id, acquired.AcquisitionToken!.Value);
        var original = Success(TickerType.TimeTicker, acquired.Id, acquired.AcquisitionToken, Envelope("original"));
        Assert.True(await _provider.CommitTerminalTickerAndEnqueueNodeFinalizationAsync(original, intent));

        var differentStatus = Terminal(TickerType.TimeTicker, acquired.Id, acquired.AcquisitionToken,
            TickerStatus.Cancelled, null);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _provider.CommitTerminalTickerAndEnqueueNodeFinalizationAsync(differentStatus, intent));

        var differentResult = Success(TickerType.TimeTicker, acquired.Id, acquired.AcquisitionToken,
            Envelope("different"));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _provider.CommitTerminalTickerAndEnqueueNodeFinalizationAsync(differentResult, intent));
    }

    [Fact]
    public async Task Ambiguous_commit_retry_preserves_non_microsecond_created_ticks_exactly()
    {
        var acquired = await AddAndAcquireTimeTickerAsync();
        var createdAt = _now.AddTicks(7);
        var intent = Intent(TickerType.TimeTicker, acquired.Id, acquired.AcquisitionToken!.Value,
            createdAtUtc: createdAt);
        var mutation = Success(TickerType.TimeTicker, acquired.Id, acquired.AcquisitionToken, Envelope("once"));

        Assert.True(await _provider.CommitTerminalTickerAndEnqueueNodeFinalizationAsync(mutation, intent));
        // Simulates an execution-strategy retry after the first transaction committed but its
        // acknowledgement was lost.
        Assert.True(await _provider.CommitTerminalTickerAndEnqueueNodeFinalizationAsync(mutation, intent));

        var claim = Assert.Single(await _provider.ClaimDueNodeFinalizationsAsync(
            "ticks-worker", 1, createdAt, createdAt.AddMinutes(1)));
        Assert.Equal(createdAt.Ticks, claim.Intent.CreatedAtUtc.Ticks);
        await using var verify = new TestTickerQDbContext(_options);
        Assert.Equal(createdAt.Ticks,
            (await verify.Set<NodeFinalizationOutboxEntity>().SingleAsync()).CreatedAtUtcTicks);
    }

    [Fact]
    public async Task Injected_failure_after_terminal_mutation_rolls_back_ticker_result_and_outbox()
    {
        var ticker = new TimeTickerEntity
        { Id = Guid.NewGuid(), Function = "Node", Status = TickerStatus.Idle, ExecutionTime = _now, Request = [], CreatedAt = _now, UpdatedAt = _now };
        await using (var seed = new TestTickerQDbContext(_options))
        { seed.Add(ticker); await seed.SaveChangesAsync(); }
        var faulting = new FaultingNodeOutboxProvider(_services, Clock(), Options("outbox-node"), Redis());
        var acquired = Assert.Single(await faulting.AcquireImmediateTimeTickersAsync([ticker.Id]));
        var intent = Intent(TickerType.TimeTicker, acquired.Id, acquired.AcquisitionToken!.Value);

        await Assert.ThrowsAsync<InjectedNodeOutboxFailureException>(() =>
            faulting.CommitTerminalTickerAndEnqueueNodeFinalizationAsync(
                Success(TickerType.TimeTicker, acquired.Id, acquired.AcquisitionToken, Envelope("rollback")), intent));

        await using var verify = new TestTickerQDbContext(_options);
        Assert.Equal(TickerStatus.InProgress, (await verify.Set<TimeTickerEntity>().SingleAsync()).Status);
        Assert.Empty(await verify.Set<TimeTickerResultEntity<TimeTickerEntity>>().ToListAsync());
        Assert.Empty(await verify.Set<NodeFinalizationOutboxEntity>().ToListAsync());
    }

    [Fact]
    public async Task Two_providers_claim_due_row_once_and_expired_lease_is_reclaimed()
    {
        var acquired = await AddAndAcquireTimeTickerAsync();
        var intent = Intent(TickerType.TimeTicker, acquired.Id, acquired.AcquisitionToken!.Value);
        Assert.True(await _provider.CommitTerminalTickerAndEnqueueNodeFinalizationAsync(
            Success(TickerType.TimeTicker, acquired.Id, acquired.AcquisitionToken, null), intent));
        var second = CreateProvider(BuildServices(), "outbox-node");

        var attempts = await Task.WhenAll(
            _provider.ClaimDueNodeFinalizationsAsync("worker-a", 1, _now, _now.AddMinutes(1)),
            second.ClaimDueNodeFinalizationsAsync("worker-b", 1, _now, _now.AddMinutes(1)));
        var firstClaim = Assert.Single(attempts.SelectMany(x => x));
        Assert.Equal(1, firstClaim.AttemptCount);

        var reclaimed = Assert.Single(await second.ClaimDueNodeFinalizationsAsync(
            "worker-c", 1, _now.AddMinutes(2), _now.AddMinutes(3)));
        Assert.Equal(2, reclaimed.AttemptCount);
        Assert.NotEqual(firstClaim.ClaimToken, reclaimed.ClaimToken);
        Assert.False(await _provider.CompleteNodeFinalizationAsync(firstClaim));
        Assert.False(await _provider.RescheduleNodeFinalizationAsync(firstClaim, _now.AddMinutes(4), "stale"));
        Assert.True(await second.RescheduleNodeFinalizationAsync(reclaimed, _now.AddMinutes(4), "retryable"));
    }

    [Fact]
    public async Task Exact_bytes_and_secret_free_fields_round_trip_and_ticker_deletion_keeps_outbox()
    {
        var acquired = await AddAndAcquireTimeTickerAsync();
        var intent = Intent(TickerType.TimeTicker, acquired.Id, acquired.AcquisitionToken!.Value);
        var expectedBody = intent.ExactBody;
        Assert.True(await _provider.CommitTerminalTickerAndEnqueueNodeFinalizationAsync(
            Success(TickerType.TimeTicker, acquired.Id, acquired.AcquisitionToken, null), intent));
        Assert.Equal(1, await _provider.RemoveTimeTickers([acquired.Id], CancellationToken.None));

        var claim = Assert.Single(await _provider.ClaimDueNodeFinalizationsAsync(
            "worker", 1, _now, _now.AddMinutes(1)));
        Assert.Equal(expectedBody, claim.Intent.ExactBody);
        claim.Intent.ExactBody[0] ^= 0xff;
        Assert.Equal(expectedBody, claim.Intent.ExactBody);
        Assert.DoesNotContain("secret", claim.Intent.FinalizeUri, StringComparison.OrdinalIgnoreCase);
        Assert.True(await _provider.CompleteNodeFinalizationAsync(claim));
    }

    [Fact]
    public async Task Cron_occurrence_terminal_commit_persists_result_and_outbox()
    {
        var cron = new CronTickerEntity { Id = Guid.NewGuid(), Function = "Cron", Expression = "* * * * *", Request = [], CreatedAt = _now, UpdatedAt = _now };
        var occurrence = new CronTickerOccurrenceEntity<CronTickerEntity>
        { Id = Guid.NewGuid(), CronTickerId = cron.Id, Status = TickerStatus.Idle, ExecutionTime = _now, CreatedAt = _now, UpdatedAt = _now };
        await using (var seed = new TestTickerQDbContext(_options))
        { seed.AddRange(cron, occurrence); await seed.SaveChangesAsync(); }
        var acquired = Assert.Single(await _provider.AcquireImmediateCronOccurrencesAsync([occurrence.Id]));
        var intent = Intent(TickerType.CronTickerOccurrence, acquired.Id, acquired.AcquisitionToken!.Value);

        Assert.True(await _provider.CommitTerminalTickerAndEnqueueNodeFinalizationAsync(
            Success(TickerType.CronTickerOccurrence, acquired.Id, acquired.AcquisitionToken, Envelope("cron")), intent));

        await using var verify = new TestTickerQDbContext(_options);
        Assert.Equal(TickerStatus.DueDone, (await verify.Set<CronTickerOccurrenceEntity<CronTickerEntity>>().SingleAsync()).Status);
        Assert.Single(await verify.Set<CronTickerOccurrenceResultEntity<CronTickerEntity>>().ToListAsync());
        Assert.Single(await verify.Set<NodeFinalizationOutboxEntity>().ToListAsync());
    }

    private async Task<TimeTickerEntity> AddAndAcquireTimeTickerAsync()
    {
        var ticker = new TimeTickerEntity
        { Id = Guid.NewGuid(), Function = "Node", Status = TickerStatus.Idle, ExecutionTime = _now, Request = [], CreatedAt = _now, UpdatedAt = _now };
        await using (var seed = new TestTickerQDbContext(_options))
        { seed.Add(ticker); await seed.SaveChangesAsync(); }
        return Assert.Single(await _provider.AcquireImmediateTimeTickersAsync([ticker.Id]));
    }

    private NodeFinalizationIntent Intent(TickerType type, Guid tickerId, Guid acquisitionToken,
        Guid? dispatchId = null, Guid? requestNonce = null, DateTime? createdAtUtc = null)
    {
        var dispatch = dispatchId ?? Guid.NewGuid();
        var epoch = Guid.NewGuid();
        var control = Guid.NewGuid();
        var body = JsonSerializer.SerializeToUtf8Bytes(new
        {
            tickerType = (int)type, tickerId, acquisitionToken, dispatchId = dispatch, nodeEpoch = epoch, controlNonce = control
        });
        return new NodeFinalizationIntent(NodeFinalizationIntent.CurrentSchemaVersion, dispatch, type, tickerId,
            acquisitionToken, dispatch, epoch, "https://node.example/finalize", "/finalize", false,
            requestNonce ?? Guid.NewGuid(), control, body, createdAtUtc ?? _now);
    }

    private static InternalFunctionContext Success(TickerType type, Guid id, Guid? token, TickerResultEnvelope? result)
        => Terminal(type, id, token, type == TickerType.CronTickerOccurrence ? TickerStatus.DueDone : TickerStatus.Done, result);

    private static InternalFunctionContext Terminal(TickerType type, Guid id, Guid? token, TickerStatus status, TickerResultEnvelope? result)
    {
        var context = new InternalFunctionContext { TickerId = id, Type = type, AcquisitionToken = token }
            .SetProperty(x => x.Status, status)
            .SetProperty(x => x.ExecutedAt, DateTime.UtcNow)
            .SetProperty(x => x.ReleaseLock, true);
        if (status is TickerStatus.Done or TickerStatus.DueDone)
            context.SetProperty(x => x.ResultEnvelope, result);
        return context;
    }

    private static TickerResultEnvelope Envelope(string value) => new(Encoding.UTF8.GetBytes(value), 1, "application/json");
    private ServiceProvider BuildServices(EfCoreNodeFinalizationOutboxReadiness? readiness = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IDbContextFactory<TestTickerQDbContext>>(new PooledDbContextFactory<TestTickerQDbContext>(_options));
        if (readiness != null)
            services.AddSingleton<IEfCoreNodeFinalizationOutboxReadiness>(readiness);
        return services.BuildServiceProvider();
    }
    private TestableProvider CreateProvider(IServiceProvider services, string node) => new(services, Clock(), Options(node), Redis());
    private ITickerClock Clock() { var clock = Substitute.For<ITickerClock>(); clock.UtcNow.Returns(_now); return clock; }
    private static SchedulerOptionsBuilder Options(string node) => new() { NodeIdentifier = node };
    private static ITickerQRedisContext Redis() { var redis = Substitute.For<ITickerQRedisContext>(); redis.HasRedisConnection.Returns(false); return redis; }
}

internal sealed class FaultingNodeOutboxProvider : TestableProvider
{
    public FaultingNodeOutboxProvider(IServiceProvider services, ITickerClock clock, SchedulerOptionsBuilder options, ITickerQRedisContext redis)
        : base(services, clock, options, redis) { }
    protected internal override Task OnSuccessfulStatusWrittenForTestAsync(TestTickerQDbContext dbContext,
        InternalFunctionContext functionContext, CancellationToken cancellationToken)
        => throw new InjectedNodeOutboxFailureException();
}

internal sealed class InjectedNodeOutboxFailureException : Exception;

internal sealed class MissingOutboxDbContext(DbContextOptions<MissingOutboxDbContext> options) : DbContext(options);

internal sealed class RecordingLogger<T> : ILogger<T>
{
    internal List<(LogLevel Level, string Message, Exception? Exception)> Entries { get; } = [];

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
        => Entries.Add((logLevel, formatter(state, exception), exception));
}
