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
/// already do — with the caller's cancellation token forwarded. A reconcile that fails must NOT touch
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
    public async Task SuccessfulReconcile_InvalidatesCronExpressionsCache_ForwardingToken()
    {
        using var cts = new CancellationTokenSource();

        await _provider.MigrateDefinedCronTickers(
            [new DefinedCronTickerSeed("cache-fn", "*/5 * * * *", 1, null)], cts.Token);

        await _distributedCache.Received(1).RemoveAsync(CronExpressionsCacheKey, cts.Token);
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
