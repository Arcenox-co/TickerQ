using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using TickerQ.Utilities;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Models;

namespace TickerQ.EntityFrameworkCore.Tests.Infrastructure;

public sealed class EfDefinedCronReconciliationRetryTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly DateTime _now = new(2026, 10, 18, 12, 0, 0, DateTimeKind.Utc);
    private readonly FaultInterceptor _faults = new();
    private readonly IDistributedCache _cache = Substitute.For<IDistributedCache>();
    private DbContextOptions<TestTickerQDbContext> _options = null!;
    private Guid _cronId;
    private Guid _occurrenceId;

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        _options = new DbContextOptionsBuilder<TestTickerQDbContext>().UseSqlite(_connection).Options;
        await using var db = new TestTickerQDbContext(_options);
        await db.Database.EnsureCreatedAsync();
        var seed = new DefinedCronTickerSeed("retry-seed", "0 */5 * * * *");
        var key = new DefinedCronSeedManifest([seed]).SeedKeyFor(seed);
        _cronId = CronSeedIdentity.DeterministicId(key);
        _occurrenceId = Guid.NewGuid();
        db.Add(new CronTickerEntity
        {
            Id = _cronId, Function = "retry-seed", Expression = "0 */5 * * * *",
            InitIdentifier = "MemoryTicker_Seeded_retry-seed", SeedKey = key, IsEnabled = true,
            DefinitionRevision = 1, SeedLastSeenAt = _now.AddDays(-1),
            CreatedAt = _now.AddDays(-1), UpdatedAt = _now.AddDays(-1)
        });
        db.Add(new CronTickerOccurrenceEntity<CronTickerEntity>
        {
            Id = _occurrenceId, CronTickerId = _cronId, DefinitionRevision = 1,
            ExecutionTime = _now.AddMinutes(5), Status = TickerStatus.Idle
        });
        await db.SaveChangesAsync();
    }

    public Task DisposeAsync() => _connection.DisposeAsync().AsTask();

    [Theory]
    [InlineData("begin", false)]
    [InlineData("read", false)]
    [InlineData("write", false)]
    [InlineData("cleanup", false)]
    [InlineData("commit", false)]
    [InlineData("committed", false)]
    [InlineData("read", true)]
    [InlineData("begin", true)]
    [InlineData("write", true)]
    [InlineData("cleanup", true)]
    [InlineData("commit", true)]
    [InlineData("committed", true)]
    public async Task Retry_ReplaysEntireTransactionWithFreshContext_AndInvalidatesCacheOnce(
        string stage, bool configuredStrategy)
    {
        _faults.Stage = stage;
        _faults.Failure = configuredStrategy
            ? () => new RetryableAdmissionTestException()
            : () => new InvalidOperationException("provider wrapper",
                new DbUpdateException("write wrapper", new TransientStoreException()));
        var (services, provider, factory) = CreateProvider(configuredStrategy);
        await using var ownedServices = services;

        await provider.MigrateDefinedCronTickers([ChangedSeed()]);

        Assert.Equal(1, _faults.Failures);
        Assert.Equal(3, factory.ContextIds.Count); // strategy context + two transaction attempts
        Assert.Equal(3, factory.ContextIds.Distinct().Count());
        await using var verify = new TestTickerQDbContext(_options);
        var cron = await verify.Set<CronTickerEntity>().SingleAsync();
        Assert.Equal(_cronId, cron.Id);
        Assert.Equal("0 */7 * * * *", cron.Expression);
        Assert.Equal(2, cron.DefinitionRevision); // even if the first commit succeeded ambiguously
        Assert.Equal(_now, cron.SeedLastSeenAt);
        Assert.Equal(TickerStatus.Skipped, (await verify.Set<CronTickerOccurrenceEntity<CronTickerEntity>>()
            .SingleAsync(x => x.Id == _occurrenceId)).Status);
        await _cache.Received(1).RemoveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("application", 1)]
    [InlineData("constraint", 5)]
    [InlineData("provider-limit", 3)]
    public async Task PermanentFailure_PropagatesAndDoesNotInvalidateCache(string kind, int attempts)
    {
        _faults.Stage = kind == "application" ? "read" : "write";
        _faults.Repeat = true;
        _faults.Failure = kind switch
        {
            "constraint" => () => new DbUpdateException("permanent constraint", new PermanentStoreException()),
            "provider-limit" => () => new RetryableAdmissionTestException(),
            _ => () => new InvalidOperationException("application failure")
        };
        var (services, provider, factory) = CreateProvider(kind == "provider-limit");
        await using var ownedServices = services;

        var error = await Assert.ThrowsAnyAsync<Exception>(() => provider.MigrateDefinedCronTickers([ChangedSeed()]));
        if (kind == "provider-limit") Assert.IsType<RetryLimitExceededException>(error);
        else if (kind == "constraint") Assert.IsType<DbUpdateException>(error);
        else Assert.IsType<InvalidOperationException>(error);
        Assert.Equal(attempts, _faults.Failures);
        Assert.Equal(attempts + 1, factory.ContextIds.Count);
        await _cache.DidNotReceive().RemoveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await using var verify = new TestTickerQDbContext(_options);
        Assert.Equal(1, (await verify.Set<CronTickerEntity>().SingleAsync()).DefinitionRevision);
        Assert.Equal(TickerStatus.Idle, (await verify.Set<CronTickerOccurrenceEntity<CronTickerEntity>>().SingleAsync()).Status);
    }

    [Fact]
    public async Task CancellationDuringAttempt_StopsBeforeAnotherAttempt()
    {
        using var cancellation = new CancellationTokenSource();
        _faults.Stage = "read";
        _faults.Failure = () =>
        {
            cancellation.Cancel();
            return new OperationCanceledException(cancellation.Token);
        };
        var (services, provider, factory) = CreateProvider(false);
        await using var ownedServices = services;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            provider.MigrateDefinedCronTickers([ChangedSeed()], cancellation.Token));
        Assert.Equal(2, factory.ContextIds.Count);
        await _cache.DidNotReceive().RemoveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OlderStartup_DoesNotMoveHeartbeatBackwards()
    {
        await using (var db = new TestTickerQDbContext(_options))
            await db.Set<CronTickerEntity>().ExecuteUpdateAsync(s => s.SetProperty(x => x.SeedLastSeenAt, _now.AddMinutes(1)));
        var (services, provider, _) = CreateProvider(false);
        await using var ownedServices = services;
        await provider.MigrateDefinedCronTickers([ChangedSeed()]);
        await using var verify = new TestTickerQDbContext(_options);
        Assert.Equal(_now.AddMinutes(1), (await verify.Set<CronTickerEntity>().SingleAsync()).SeedLastSeenAt);
    }

    private static DefinedCronTickerSeed ChangedSeed() => new("retry-seed", "0 */7 * * * *");

    private (ServiceProvider, TestableProvider, TrackingFactory) CreateProvider(bool configuredStrategy)
    {
        var builder = new DbContextOptionsBuilder<TestTickerQDbContext>(_options).AddInterceptors(_faults);
        if (configuredStrategy)
            builder.ReplaceService<IExecutionStrategyFactory, RetryableAdmissionExecutionStrategyFactory>();
        var factory = new TrackingFactory(builder.Options);
        var services = new ServiceCollection().AddSingleton<IDbContextFactory<TestTickerQDbContext>>(factory).BuildServiceProvider();
        var clock = Substitute.For<ITickerClock>();
        clock.UtcNow.Returns(_now);
        var redis = Substitute.For<ITickerQRedisContext>();
        redis.HasRedisConnection.Returns(true);
        redis.DistributedCache.Returns(_cache);
        return (services, new TestableProvider(services, clock, new SchedulerOptionsBuilder(), redis), factory);
    }

    private sealed class TrackingFactory(DbContextOptions<TestTickerQDbContext> options) : IDbContextFactory<TestTickerQDbContext>
    {
        public List<Guid> ContextIds { get; } = [];
        public TestTickerQDbContext CreateDbContext()
        {
            var context = new TestTickerQDbContext(options);
            ContextIds.Add(context.ContextId.InstanceId);
            return context;
        }
        public Task<TestTickerQDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(CreateDbContext());
    }

    private sealed class TransientStoreException : DbException
    {
        public override bool IsTransient => true;
    }
    private sealed class PermanentStoreException : DbException;

    private sealed class FaultInterceptor : DbCommandInterceptor, IDbTransactionInterceptor
    {
        public string Stage { get; set; } = "none";
        public Func<Exception> Failure { get; set; } = () => new TransientStoreException();
        public bool Repeat { get; set; }
        public int Failures { get; private set; }

        private void Fail(string stage)
        {
            if (stage == Stage && (Failures == 0 || Repeat))
            {
                Failures++;
                throw Failure();
            }
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("\"CronTickers\""))
                Fail(command.CommandText.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase) ? "read" : "write");
            return ValueTask.FromResult(result);
        }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("\"CronTickerOccurrences\"")) Fail("cleanup");
            return ValueTask.FromResult(result);
        }

        public ValueTask<InterceptionResult<DbTransaction>> TransactionStartingAsync(DbConnection connection,
            TransactionStartingEventData eventData, InterceptionResult<DbTransaction> result,
            CancellationToken cancellationToken = default)
        {
            Fail("begin");
            return ValueTask.FromResult(result);
        }

        public ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction transaction,
            TransactionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
        {
            Fail("commit");
            return ValueTask.FromResult(result);
        }

        public Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData,
            CancellationToken cancellationToken = default)
        {
            Fail("committed");
            return Task.CompletedTask;
        }
    }
}
