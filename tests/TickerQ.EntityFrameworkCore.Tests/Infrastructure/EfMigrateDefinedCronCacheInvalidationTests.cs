using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;

using NSubstitute;

using TickerQ.Utilities;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Models;

using Xunit;

namespace TickerQ.EntityFrameworkCore.Tests.Infrastructure;

/// <summary>
/// Finding 2: <c>MigrateDefinedCronTickers</c> updates/removes/retires cron expressions, so a successful
/// reconcile that can alter the visible cron definitions must invalidate the partitioned
/// <c>"cron:expressions:{partition}"</c> <see cref="IDistributedCache"/> key — exactly like Insert/Update/Delete
/// already do — independently of request cancellation after commit. A reconcile that fails must NOT touch
/// the cache (a stale reconcile must not silently drop the coherent cached view).
/// </summary>
public sealed class EfMigrateDefinedCronCacheInvalidationTests : IAsyncLifetime
{
    private static readonly string CronExpressionsCacheKey =
        $"cron:expressions:{TickerQRuntimePartition.LegacyGlobal.StorageKey}";

    private SqliteConnection _connection = null!;
    private TestableProvider _provider = null!;
    private IDistributedCache _distributedCache = null!;
    private IServiceProvider _serviceProvider = null!;
    private ITickerClock _clock = null!;
    private ITickerQRedisContext _redisContext = null!;
    private readonly DateTime _fixedNow = new(2025, 6, 15, 12, 0, 0, DateTimeKind.Utc);

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        await _connection.OpenAsync();

        _clock = Substitute.For<ITickerClock>();
        _clock.UtcNow.Returns(_fixedNow);

        _distributedCache = Substitute.For<IDistributedCache>();

        _redisContext = Substitute.For<ITickerQRedisContext>();
        _redisContext.HasRedisConnection.Returns(true);
        _redisContext.DistributedCache.Returns(_distributedCache);

        var dbOptions = new DbContextOptionsBuilder<TestTickerQDbContext>()
            .UseSqlite(_connection)
            .Options;

        await using (var seedContext = new TestTickerQDbContext(dbOptions))
            await seedContext.Database.EnsureCreatedAsync();

        var options = new SchedulerOptionsBuilder { NodeIdentifier = "cache-invalidation-node" };

        var services = new ServiceCollection();
        services.AddSingleton<IDbContextFactory<TestTickerQDbContext>>(
            new PooledDbContextFactory<TestTickerQDbContext>(dbOptions));
        _serviceProvider = services.BuildServiceProvider();

        _provider = new TestableProvider(_serviceProvider, _clock, options, _redisContext);
    }

    public async Task DisposeAsync() => await _connection.DisposeAsync();

    [Fact]
    public async Task SuccessfulReconcile_InvalidatesCronExpressionsCache_IndependentOfRequestCancellation()
    {
        using var cts = new CancellationTokenSource();

        await _provider.MigrateDefinedCronTickers(
            [new DefinedCronTickerSeed("cache-fn", "*/5 * * * *", 1, null)], cts.Token);

        await _distributedCache.Received(1).RemoveAsync(CronExpressionsCacheKey, CancellationToken.None);
    }

    [Fact]
    public async Task FailedReconcile_DoesNotInvalidateCronExpressionsCache()
    {
        var canceled = new CancellationToken(canceled: true);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            _provider.MigrateDefinedCronTickers(
                [new DefinedCronTickerSeed("cache-fn", "*/5 * * * *", 1, null)], canceled));

        await _distributedCache.DidNotReceive()
            .RemoveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CommittedCronMutations_SucceedEvenWhenRedisInvalidationFails(bool canceledCache)
    {
        _distributedCache.RemoveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(_ =>
            Task.FromException(canceledCache ? new OperationCanceledException("cache timeout") : new IOException("cache offline")));
        var cron = new CronTickerEntity { Function = "committed", Expression = "0 * * * * *" };
        Assert.Equal(1, await _provider.InsertCronTickers([cron], CancellationToken.None));
        Assert.Equal(cron.Id, Assert.Single(await _provider.GetCronTickers(x => x.Function == "committed", CancellationToken.None)).Id);
        cron.Expression = "0 */2 * * * *";
        Assert.Equal(1, await _provider.UpdateCronTickers([cron], CancellationToken.None));
        Assert.Equal(cron.Expression, Assert.Single(await _provider.GetCronTickers(x => x.Id == cron.Id, CancellationToken.None)).Expression);
        Assert.Equal(1, await _provider.RemoveCronTickers([cron.Id], CancellationToken.None));
        Assert.Empty(await _provider.GetCronTickers(x => x.Id == cron.Id, CancellationToken.None));
        await _provider.MigrateDefinedCronTickers([new DefinedCronTickerSeed("new-code-job", "0 * * * * *")], CancellationToken.None);
        Assert.Single(await _provider.GetCronTickers(x => x.Function == "new-code-job", CancellationToken.None));
    }

    [Fact]
    public async Task RequestCancellationAfterCommit_DoesNotCancelInvalidationOrMisreportInsert()
    {
        using var request = new CancellationTokenSource();
        _distributedCache.RemoveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            request.Cancel(); // Persistence has already completed by this point.
            call.ArgAt<CancellationToken>(1).ThrowIfCancellationRequested();
            return Task.CompletedTask;
        });
        var cron = new CronTickerEntity { Function = "committed", Expression = "0 * * * * *" };
        Assert.Equal(1, await _provider.InsertCronTickers([cron], request.Token));
        Assert.True(request.IsCancellationRequested);
        Assert.Single(await _provider.GetCronTickers(x => x.Id == cron.Id, CancellationToken.None));
    }

    [Fact]
    public async Task NamespacedReconcile_InvalidatesOnlyItsPartitionedCacheKey()
    {
        const string applicationNamespace = "cache-app";
        var options = new SchedulerOptionsBuilder { NodeIdentifier = "cache-app-node" };
        options.BindRuntimeActivationScope(applicationNamespace, 1, schedulerEnabled: false);
        var provider = new TestableProvider(_serviceProvider, _clock, options, _redisContext);

        await provider.MigrateDefinedCronTickers(
            new DefinedCronSeedManifest(applicationNamespace,
                [new DefinedCronTickerSeed("cache-app-fn", "*/5 * * * *", 1, null)]),
            CancellationToken.None);

        var partitionedKey = $"cron:expressions:{new TickerQRuntimePartition(applicationNamespace).StorageKey}";
        await _distributedCache.Received(1).RemoveAsync(partitionedKey, CancellationToken.None);
        await _distributedCache.DidNotReceive().RemoveAsync(CronExpressionsCacheKey, Arg.Any<CancellationToken>());
    }
}
