using DotNet.Testcontainers.Builders;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Testcontainers.PostgreSql;
using TickerQ.EntityFrameworkCore.Entities;
using TickerQ.Utilities;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Models;

namespace TickerQ.EntityFrameworkCore.Tests.Infrastructure;

[AttributeUsage(AttributeTargets.Method)]
public sealed class PostgreSqlRaceFactAttribute : FactAttribute
{
    public PostgreSqlRaceFactAttribute()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("TICKERQ_POSTGRES_RACE_TESTS"),
                "1", StringComparison.Ordinal))
            Skip = "Set TICKERQ_POSTGRES_RACE_TESTS=1 to run the PostgreSQL MVCC race test.";
    }
}

[Collection("PostgreSql MVCC")]
public sealed class EfCorePostgreSqlGenerationRaceTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:17-alpine")
        .WithDatabase("tickerq")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .WithWaitStrategy(Wait.ForUnixContainer().UntilCommandIsCompleted("pg_isready -U postgres -d tickerq"))
        .Build();

    private DbContextOptions<TestTickerQDbContext> _options = null!;
    private ServiceProvider _services = null!;
    private TestableProvider _provider = null!;
    private readonly DateTime _now = new(2026, 7, 29, 9, 0, 0, DateTimeKind.Utc);

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        _options = new DbContextOptionsBuilder<TestTickerQDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;
        await using (var context = new TestTickerQDbContext(_options))
            await context.Database.EnsureCreatedAsync();

        var clock = Substitute.For<ITickerClock>();
        clock.UtcNow.Returns(_now);
        var redis = Substitute.For<ITickerQRedisContext>();
        redis.HasRedisConnection.Returns(false);
        var services = new ServiceCollection();
        services.AddSingleton<IDbContextFactory<TestTickerQDbContext>>(
            new PooledDbContextFactory<TestTickerQDbContext>(_options));
        _services = services.BuildServiceProvider();
        _provider = new TestableProvider(
            _services, clock, new SchedulerOptionsBuilder { NodeIdentifier = "postgres-race" }, redis);
    }

    public async Task DisposeAsync()
    {
        await _services.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    [PostgreSqlRaceFact]
    public async Task ChildMutations_SerializeBeforeConcurrentRootReacquisition_OnMvccDatabase()
    {
        foreach (var mutation in new[] { "lifecycle", "terminal-status", "terminal-result" })
            await AssertChildWinsRootLockRaceAsync(mutation);
    }

    [PostgreSqlRaceFact]
    public async Task OutboxAmbiguousRetryPreservesNonMicrosecondCreatedTicks()
    {
        await using (var cleanup = new TestTickerQDbContext(_options))
        {
            await cleanup.Set<NodeFinalizationOutboxEntity>().ExecuteDeleteAsync();
            await cleanup.Set<TimeTickerResultEntity<TimeTickerEntity>>().ExecuteDeleteAsync();
            await cleanup.Set<TimeTickerEntity>().ExecuteDeleteAsync();
        }

        var ticker = NewTicker(TickerStatus.Idle);
        await _provider.AddTimeTickers([ticker], CancellationToken.None);
        var acquired = Assert.Single(await _provider.AcquireImmediateTimeTickersAsync([ticker.Id]));
        var dispatch = Guid.NewGuid();
        var epoch = Guid.NewGuid();
        var control = Guid.NewGuid();
        var createdAt = _now.AddTicks(7); // PostgreSQL timestamp cannot represent these ticks exactly.
        var body = JsonSerializer.SerializeToUtf8Bytes(new
        {
            tickerType = (int)TickerType.TimeTicker, tickerId = ticker.Id,
            acquisitionToken = acquired.AcquisitionToken, dispatchId = dispatch,
            nodeEpoch = epoch, controlNonce = control
        });
        var intent = new NodeFinalizationIntent(
            NodeFinalizationIntent.CurrentSchemaVersion, dispatch, TickerType.TimeTicker, ticker.Id,
            acquired.AcquisitionToken!.Value, dispatch, epoch, "https://node.example/finalize", "/finalize",
            false, Guid.NewGuid(), control, body, createdAt);
        var mutation = new InternalFunctionContext
        {
            TickerId = ticker.Id, Type = TickerType.TimeTicker,
            AcquisitionToken = acquired.AcquisitionToken
        }.SetProperty(x => x.Status, TickerStatus.Done)
         .SetProperty(x => x.ExecutedAt, _now)
         .SetProperty(x => x.ReleaseLock, true)
         .SetProperty(x => x.ResultEnvelope, new TickerResultEnvelope([1, 2, 3], 1, "application/octet-stream"));

        Assert.True(await _provider.CommitTerminalTickerAndEnqueueNodeFinalizationAsync(mutation, intent));
        // Simulated execution-strategy replay after commit acknowledgement loss.
        Assert.True(await _provider.CommitTerminalTickerAndEnqueueNodeFinalizationAsync(mutation, intent));

        var claim = Assert.Single(await _provider.ClaimDueNodeFinalizationsAsync(
            "postgres-ticks", 1, createdAt.AddSeconds(1), createdAt.AddMinutes(1)));
        Assert.Equal(createdAt.Ticks, claim.Intent.CreatedAtUtc.Ticks);
    }

    private async Task AssertChildWinsRootLockRaceAsync(string mutation)
    {
        await using (var cleanup = new TestTickerQDbContext(_options))
        {
            await cleanup.Set<TimeTickerResultEntity<TimeTickerEntity>>().ExecuteDeleteAsync();
            await cleanup.Set<TimeTickerEntity>().ExecuteDeleteAsync();
        }

        var generation = Guid.NewGuid();
        var root = NewTicker(TickerStatus.Done);
        root.ChainRootId = root.Id;
        root.ChainGeneration = generation;
        var child = NewTicker(TickerStatus.Idle);
        child.ExecutionTime = null;
        child.ParentId = root.Id;
        child.ChainRootId = root.Id;
        child.ChainGeneration = generation;
        await using (var seed = new TestTickerQDbContext(_options))
        {
            seed.AddRange(root, child);
            await seed.SaveChangesAsync();
            await seed.Database.ExecuteSqlRawAsync($$"""
                CREATE OR REPLACE FUNCTION ticker.delay_test_child_update()
                RETURNS trigger LANGUAGE plpgsql AS $fn$
                BEGIN
                    IF NEW."Id" = '{{child.Id}}'::uuid THEN
                        PERFORM pg_sleep(1.5);
                    END IF;
                    RETURN NEW;
                END;
                $fn$;
                DROP TRIGGER IF EXISTS delay_test_child_update ON ticker."TimeTickers";
                CREATE TRIGGER delay_test_child_update
                BEFORE UPDATE ON ticker."TimeTickers"
                FOR EACH ROW EXECUTE FUNCTION ticker.delay_test_child_update();
                """);
        }

        var context = new InternalFunctionContext
        {
            TickerId = child.Id,
            ParentId = root.Id,
            ChainRootId = root.Id,
            ChainGeneration = generation,
            Type = TickerType.TimeTicker
        };
        if (mutation == "lifecycle")
            context.SetProperty(x => x.Status, TickerStatus.InProgress);
        else
            context.SetProperty(x => x.Status, TickerStatus.Done);
        if (mutation == "terminal-result")
            context.SetProperty(x => x.ResultEnvelope,
                new TickerResultEnvelope([42], 1, "application/octet-stream"));

        var completionOrder = 0;
        var childOrder = 0;
        var reacquireOrder = 0;
        Task childWrite = mutation == "terminal-result"
            ? _provider.CommitSuccessfulTickerAsync(context).ContinueWith(t =>
            {
                Assert.True(t.GetAwaiter().GetResult());
                childOrder = Interlocked.Increment(ref completionOrder);
            }, TaskScheduler.Default)
            : _provider.UpdateTimeTicker(context, CancellationToken.None).ContinueWith(t =>
            {
                Assert.Equal(1, t.GetAwaiter().GetResult());
                childOrder = Interlocked.Increment(ref completionOrder);
            }, TaskScheduler.Default);

        await Task.Delay(250);
        var reacquire = _provider.AcquireTimeTickerOnDemandAsync(root.Id, _now, CancellationToken.None)
            .ContinueWith(t =>
            {
                Assert.NotNull(t.GetAwaiter().GetResult());
                reacquireOrder = Interlocked.Increment(ref completionOrder);
            }, TaskScheduler.Default);

        await Task.Delay(500);
        Assert.False(reacquire.IsCompleted,
            $"Root reacquisition bypassed the child generation lock for {mutation}.");
        await Task.WhenAll(childWrite, reacquire).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(1, childOrder);
        Assert.Equal(2, reacquireOrder);
    }

    private TimeTickerEntity NewTicker(TickerStatus status)
        => new()
        {
            Id = Guid.NewGuid(),
            Function = "race-test",
            Request = [],
            ExecutionTime = _now,
            Status = status,
            CreatedAt = _now.AddMinutes(-1),
            UpdatedAt = _now.AddMinutes(-1)
        };
}
