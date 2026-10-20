using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using TickerQ.Utilities;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Interfaces;

namespace TickerQ.EntityFrameworkCore.Tests.Infrastructure;

public sealed class EfGraphMutationRetryTests
{
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(false, true)]
    public async Task RolledBackMutation_ReplaysWithFreshContext_AndPersistsExactlyOnce(bool graph, bool configuredStrategy)
    {
        await using var fixture = await Fixture.CreateAsync(configuredStrategy);
        var attempts = 0;
        fixture.Provider.AfterRunnableAdmissionOperationForTestAsync = _ =>
        {
            if (++attempts == 1)
                throw configuredStrategy
                    ? new RetryableAdmissionTestException()
                    : new InvalidOperationException("provider wrapper", new DbUpdateException("EF wrapper", new TransientFailure()));
            return Task.CompletedTask;
        };

        Assert.Equal(1, await fixture.Provider.MutateAsync(graph));

        Assert.Equal(2, attempts);
        Assert.Equal(2, fixture.ContextIds.Count);
        Assert.Equal(2, fixture.ContextIds.Distinct().Count());
        Assert.Equal(1, await fixture.CountJobsAsync());
    }

    [Theory]
    [InlineData("application", 1)]
    [InlineData("constraint", 1)]
    [InlineData("configured-limit", 3)]
    [InlineData("cancel", 1)]
    public async Task NonRetryableFailure_DoesNotReplayOutsideConfiguredPolicy(string kind, int expectedAttempts)
    {
        await using var fixture = await Fixture.CreateAsync(configuredStrategy: kind == "configured-limit");
        using var cancellation = new CancellationTokenSource();
        var attempts = 0;
        fixture.Provider.AfterRunnableAdmissionOperationForTestAsync = _ =>
        {
            attempts++;
            if (kind == "cancel")
            {
                cancellation.Cancel();
                throw new OperationCanceledException(cancellation.Token);
            }
            throw kind switch
            {
                "configured-limit" => new RetryableAdmissionTestException(),
                "constraint" => new DbUpdateException("Permanent constraint", new PermanentFailure()),
                _ => new InvalidOperationException("Application failure")
            };
        };

        var error = await Assert.ThrowsAnyAsync<Exception>(() => fixture.Provider.MutateAsync(true, cancellation.Token));

        if (kind == "configured-limit") Assert.IsType<RetryLimitExceededException>(error);
        if (kind == "cancel") Assert.IsAssignableFrom<OperationCanceledException>(error);
        Assert.Equal(expectedAttempts, attempts);
        Assert.Equal(expectedAttempts, fixture.ContextIds.Count);
        Assert.Equal(0, await fixture.CountJobsAsync());
    }

    [Theory]
    [InlineData(false, null)]
    [InlineData(true, null)]
    [InlineData(false, "40003")]
    [InlineData(true, "40003")]
    public async Task AmbiguousCommitFailure_IsNotAutomaticallyReplayed(bool afterCommit, string? sqlState)
    {
        var fault = new CommitFailure(afterCommit, sqlState);
        await using var fixture = await Fixture.CreateAsync(interceptor: fault);

        await Assert.ThrowsAnyAsync<Exception>(() => fixture.Provider.MutateAsync(true));

        Assert.Single(fixture.ContextIds);
        Assert.Equal(1, fault.Failures);
        Assert.Equal(afterCommit ? 1 : 0, await fixture.CountJobsAsync());
    }

    [Fact]
    public async Task KnownSerializationRollbackAtCommit_ReplaysWholeTransaction()
    {
        var fault = new CommitFailure(afterCommit: false, sqlState: "40001");
        await using var fixture = await Fixture.CreateAsync(interceptor: fault);

        Assert.Equal(1, await fixture.Provider.MutateAsync(true));

        Assert.Equal(2, fixture.ContextIds.Count);
        Assert.Equal(2, fixture.ContextIds.Distinct().Count());
        Assert.Equal(1, fault.Failures);
        Assert.Equal(1, await fixture.CountJobsAsync());
    }

    private sealed class MutationProbeProvider(IServiceProvider services, ITickerClock clock)
        : TestableProvider(services, clock, new SchedulerOptionsBuilder(), Substitute.For<ITickerQRedisContext>())
    {
        internal Task<int> MutateAsync(bool graph, CancellationToken cancellationToken = default)
            => graph
                ? ExecuteTimeTickerGraphMutationAsync(false, -1, InsertAsync, cancellationToken)
                : ExecuteRunnableAdmissionAsync(-1, InsertAsync, cancellationToken);

        private static async Task<int> InsertAsync(TestTickerQDbContext db, CancellationToken ct)
        {
            // A fresh ID on every attempt makes an unsafe post-commit replay observable.
            db.Add(new TimeTickerEntity
            {
                Id = Guid.NewGuid(), Function = "atomic-mutation", ExecutionTime = DateTime.UtcNow.AddHours(1)
            });
            return await db.SaveChangesAsync(ct);
        }
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly ServiceProvider _services;
        private readonly DbContextOptions<TestTickerQDbContext> _verificationOptions;
        internal MutationProbeProvider Provider { get; }
        internal List<Guid> ContextIds { get; } = [];

        private Fixture(SqliteConnection connection, DbContextOptions<TestTickerQDbContext> options,
            DbContextOptions<TestTickerQDbContext> verificationOptions)
        {
            _connection = connection;
            _verificationOptions = verificationOptions;
            _services = new ServiceCollection()
                .AddSingleton<IDbContextFactory<TestTickerQDbContext>>(new NonPooledTestTickerQDbContextFactory(options))
                .BuildServiceProvider();
            var clock = Substitute.For<ITickerClock>();
            clock.UtcNow.Returns(_ => DateTime.UtcNow);
            Provider = new MutationProbeProvider(_services, clock);
            Provider.RunnableAdmissionContextCreatedForTest = ContextIds.Add;
        }

        internal static async Task<Fixture> CreateAsync(bool configuredStrategy = false, IInterceptor? interceptor = null)
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var builder = new DbContextOptionsBuilder<TestTickerQDbContext>().UseSqlite(connection);
            var verificationOptions = builder.Options;
            await using (var schema = new TestTickerQDbContext(verificationOptions))
                await schema.Database.EnsureCreatedAsync();
            if (interceptor != null) builder.AddInterceptors(interceptor);
            if (configuredStrategy)
                builder.ReplaceService<IExecutionStrategyFactory, RetryableAdmissionExecutionStrategyFactory>();
            return new Fixture(connection, builder.Options, verificationOptions);
        }

        internal async Task<int> CountJobsAsync()
        {
            await using var db = new TestTickerQDbContext(_verificationOptions);
            return await db.Set<TimeTickerEntity>().CountAsync();
        }

        public async ValueTask DisposeAsync()
        {
            await _services.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }

    private sealed class TransientFailure(string? sqlState = null) : DbException
    {
        public override bool IsTransient => true;
        public override string? SqlState => sqlState;
    }

    private sealed class PermanentFailure : DbException;

    private sealed class CommitFailure(bool afterCommit, string? sqlState) : DbTransactionInterceptor
    {
        internal int Failures { get; private set; }
        private void FailOnce()
        {
            if (Failures == 0)
            {
                Failures++;
                throw new InvalidOperationException("Commit result unavailable", new TransientFailure(sqlState));
            }
        }

        public override ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction transaction,
            TransactionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
        {
            if (!afterCommit) FailOnce();
            return ValueTask.FromResult(result);
        }

        public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData,
            CancellationToken cancellationToken = default)
        {
            if (afterCommit) FailOnce();
            return Task.CompletedTask;
        }
    }
}
