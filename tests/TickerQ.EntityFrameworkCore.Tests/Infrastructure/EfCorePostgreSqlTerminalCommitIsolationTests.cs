using System.Data.Common;
using DotNet.Testcontainers.Builders;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using NSubstitute;
using Testcontainers.PostgreSql;
using TickerQ.EntityFrameworkCore.Entities;
using TickerQ.Utilities;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Models;

namespace TickerQ.EntityFrameworkCore.Tests.Infrastructure;

/// <summary>
/// PostgreSQL terminal commits run at Read Committed. Each guard is a conditional write that
/// PostgreSQL re-checks against the latest committed row under its row lock, so ownership stays
/// exact while unrelated completions no longer abort each other.
/// </summary>
[Collection("PostgreSql MVCC")]
public sealed class EfCorePostgreSqlTerminalCommitIsolationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase("tickerq")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .WithWaitStrategy(Wait.ForUnixContainer().UntilCommandIsCompleted("pg_isready -h 127.0.0.1 -U postgres -d tickerq"))
        .Build();

    private readonly SerializationFailureCounter _failures = new();
    private DbContextOptions<TestTickerQDbContext> _options = null!;
    private ServiceProvider _services = null!;
    private TestableProvider _provider = null!;
    private readonly DateTime _now = new(2026, 7, 29, 9, 0, 0, DateTimeKind.Utc);

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        _options = new DbContextOptionsBuilder<TestTickerQDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .AddInterceptors(_failures.Commands, _failures.Transactions)
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
            _services, clock, new SchedulerOptionsBuilder { NodeIdentifier = "postgres-isolation" }, redis);
    }

    public async Task DisposeAsync()
    {
        await _services.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    [PostgreSqlRaceFact]
    public async Task StaleCommit_WaitingOnTheRowLock_LosesToRecoveryThatReownedTheRow()
    {
        var acquired = await AddAcquiredAsync();
        var recoveredToken = Guid.NewGuid();

        await using var recovery = new TestTickerQDbContext(_options);
        await using var transaction = await recovery.Database.BeginTransactionAsync();
        Assert.Equal(1, await recovery.Set<TimeTickerEntity>()
            .Where(x => x.Id == acquired.Id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.AcquisitionToken, recoveredToken)
                .SetProperty(x => x.LockHolder, "other-node")));

        var staleCommit = _provider.CommitTerminalTickerAsync(
            Done(acquired).SetProperty(x => x.ResultEnvelope, null));
        await Task.Delay(500);
        Assert.False(staleCommit.IsCompleted, "The stale commit did not wait for the recovery's row lock.");
        await transaction.CommitAsync();

        Assert.False(await staleCommit.WaitAsync(TimeSpan.FromSeconds(10)));
        var stored = await ReadAsync(acquired.Id);
        Assert.Equal(TickerStatus.InProgress, stored.Status);
        Assert.Equal(recoveredToken, stored.AcquisitionToken);
        Assert.Equal("other-node", stored.LockHolder);
    }

    [PostgreSqlRaceFact]
    public async Task ConcurrentDuplicateCommits_PublishExactlyOneOutcomeAndResult()
    {
        for (var round = 0; round < 20; round++)
        {
            var acquired = await AddAcquiredAsync();
            var outcomes = await Task.WhenAll(Enumerable.Range(0, 4).Select(value =>
                _provider.CommitSuccessfulTickerAsync(Done(acquired)
                    .SetProperty(x => x.ResultEnvelope,
                        new TickerResultEnvelope([(byte)value], 1, "application/octet-stream")))));

            Assert.Single(outcomes, committed => committed);
            await using var context = new TestTickerQDbContext(_options);
            Assert.Equal(1, await context.Set<TimeTickerResultEntity<TimeTickerEntity>>()
                .CountAsync(x => x.TickerId == acquired.Id));
            Assert.Equal(TickerStatus.Done, (await ReadAsync(acquired.Id)).Status);
        }
    }

    [PostgreSqlRaceFact]
    public async Task ConcurrentCompletionsOfDifferentRows_CommitWithoutRollbacks()
    {
        var acquired = new List<TimeTickerEntity>();
        for (var i = 0; i < 64; i++) acquired.Add(await AddAcquiredAsync());
        _failures.Reset();

        var outcomes = await Task.WhenAll(acquired.Select(row =>
            _provider.CommitSuccessfulTickerAsync(Done(row).SetProperty(x => x.ResultEnvelope, null))));

        Assert.All(outcomes, Assert.True);
        Assert.Equal(0, _failures.Count);
        await using var context = new TestTickerQDbContext(_options);
        Assert.Equal(64, await context.Set<TimeTickerEntity>().CountAsync(x => x.Status == TickerStatus.Done));
    }

    private async Task<TimeTickerEntity> AddAcquiredAsync()
    {
        var ticker = new TimeTickerEntity
        {
            Id = Guid.NewGuid(), Function = "isolation-test", Request = [],
            ExecutionTime = _now, Status = TickerStatus.Idle,
            CreatedAt = _now.AddMinutes(-1), UpdatedAt = _now.AddMinutes(-1)
        };
        await _provider.AddTimeTickers([ticker], CancellationToken.None);
        var acquired = Assert.Single(await _provider.AcquireImmediateTimeTickersAsync([ticker.Id]));
        Assert.Equal(TickerStatus.InProgress, (await ReadAsync(ticker.Id)).Status);
        return acquired;
    }

    private static InternalFunctionContext Done(TimeTickerEntity acquired)
        => new InternalFunctionContext
        {
            TickerId = acquired.Id, Type = TickerType.TimeTicker,
            AcquisitionToken = acquired.AcquisitionToken,
            ChainRootId = acquired.ChainRootId, ChainGeneration = acquired.ChainGeneration
        }.SetProperty(x => x.Status, TickerStatus.Done)
         .SetProperty(x => x.ExecutedAt, DateTime.UtcNow)
         .SetProperty(x => x.ReleaseLock, true);

    private async Task<TimeTickerEntity> ReadAsync(Guid id)
    {
        await using var context = new TestTickerQDbContext(_options);
        return await context.Set<TimeTickerEntity>().AsNoTracking().SingleAsync(x => x.Id == id);
    }

    private sealed class SerializationFailureCounter
    {
        private int _count;
        public int Count => Volatile.Read(ref _count);
        public void Reset() => Interlocked.Exchange(ref _count, 0);
        public CommandFailures Commands { get; }
        public TransactionFailures Transactions { get; }

        public SerializationFailureCounter()
        {
            Commands = new CommandFailures(this);
            Transactions = new TransactionFailures(this);
        }

        private void Observe(Exception exception)
        {
            for (var current = exception; current != null; current = current.InnerException)
                if (current is PostgresException { SqlState: PostgresErrorCodes.SerializationFailure })
                {
                    Interlocked.Increment(ref _count);
                    return;
                }
        }

        public sealed class CommandFailures(SerializationFailureCounter owner) : DbCommandInterceptor
        {
            public override Task CommandFailedAsync(DbCommand command, CommandErrorEventData eventData,
                CancellationToken cancellationToken = default)
            {
                owner.Observe(eventData.Exception);
                return Task.CompletedTask;
            }
        }

        public sealed class TransactionFailures(SerializationFailureCounter owner) : DbTransactionInterceptor
        {
            public override Task TransactionFailedAsync(DbTransaction transaction, TransactionErrorEventData eventData,
                CancellationToken cancellationToken = default)
            {
                owner.Observe(eventData.Exception);
                return Task.CompletedTask;
            }
        }
    }
}
