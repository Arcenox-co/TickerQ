using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.StackExchangeRedis;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NSubstitute;
using StackExchange.Redis;
using Testcontainers.Redis;
using TickerQ.Caching.StackExchangeRedis.DependencyInjection;
using RedisOptions = TickerQ.Caching.StackExchangeRedis.DependencyInjection.ServiceExtension.TickerQRedisOptionBuilder;
using TickerQ.DependencyInjection;
using TickerQ.EntityFrameworkCore.Customizer;
using TickerQ.EntityFrameworkCore.DependencyInjection;
using TickerQ.Utilities;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Models;

namespace TickerQ.Caching.StackExchangeRedis.Tests;

// Always isolated: these tests never connect to, or flush, an application Redis instance.
public sealed class ArrayCacheRedisFixture : IAsyncLifetime
{
    public RedisContainer Container { get; } = new RedisBuilder("redis:7").Build();
    public IConnectionMultiplexer Connection { get; private set; } = null!;
    public async Task InitializeAsync()
    {
        await Container.StartAsync();
        Connection = await ConnectionMultiplexer.ConnectAsync(Container.GetConnectionString());
    }
    public async Task DisposeAsync()
    {
        if (Connection != null) await Connection.DisposeAsync();
        await Container.DisposeAsync();
    }
}

public sealed class RedisArrayCacheIntegrationTests(ArrayCacheRedisFixture fixture) : IClassFixture<ArrayCacheRedisFixture>
{
    private const string Key = "cron:expressions:integration";
    private RedisCache CreateCache(string prefix) => new(Options.Create(new RedisCacheOptions
    {
        Configuration = fixture.Container.GetConnectionString(), InstanceName = prefix
    }));
    private static TickerQRedisContext CreateContext(IDistributedCache cache) => new(cache,
        new SchedulerOptionsBuilder(), new RedisOptions(),
        Substitute.For<ITickerQNotificationHubSender>());
    private static CronTickerEntity[] Rows(string function) => [new() { Function = function, Expression = "* * * * *" }];

    [Fact]
    public async Task RealRedis_ExpiresPayloadAndRefillsFromPersistence()
    {
        var prefix = $"cache-ttl:{Guid.NewGuid():N}:";
        using var cache = CreateCache(prefix);
        var context = CreateContext(cache);
        await context.GetOrSetArrayAsync(Key, _ => Task.FromResult(Rows("old")), TimeSpan.FromSeconds(2));
        var ttl = await fixture.Connection.GetDatabase().KeyTimeToLiveAsync(prefix + Key);
        Assert.NotNull(ttl);
        Assert.InRange(ttl.Value.TotalMilliseconds, 1, 2000);
        Assert.Equal("old", Assert.Single(await context.GetOrSetArrayAsync<CronTickerEntity>(Key,
            _ => throw new Exception("must be a cache hit"))).Function);
        await Task.Delay(TimeSpan.FromMilliseconds(2200));
        Assert.Null(await cache.GetAsync(Key));
        Assert.Equal("fresh", Assert.Single(await context.GetOrSetArrayAsync(Key,
            _ => Task.FromResult(Rows("fresh")))).Function);
    }

    [Fact]
    public async Task RealRedis_RepairsImmortalLegacyEntry_AndInvalidatesAcrossReplicas()
    {
        var prefix = $"cache-upgrade:{Guid.NewGuid():N}:";
        using var cache = CreateCache(prefix);
        using var replicaCache = CreateCache(prefix);
        await cache.SetAsync(Key, JsonSerializer.SerializeToUtf8Bytes(Rows("legacy")));
        Assert.Null(await fixture.Connection.GetDatabase().KeyTimeToLiveAsync(prefix + Key));
        var first = CreateContext(cache);
        var second = CreateContext(replicaCache);
        Assert.Equal("current", Assert.Single(await first.GetOrSetArrayAsync(Key,
            _ => Task.FromResult(Rows("current")))).Function);
        Assert.NotNull(await fixture.Connection.GetDatabase().KeyTimeToLiveAsync(prefix + Key));
        await second.DistributedCache.RemoveAsync(Key);
        Assert.Equal("after-deploy", Assert.Single(await first.GetOrSetArrayAsync(Key,
            _ => Task.FromResult(Rows("after-deploy")))).Function);
    }

    public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options);

    [Fact]
    public async Task EfStartupReconciliation_RefreshesRealRedis_ForAddedChangedAndRetiredDefinitions()
    {
        using var cache = CreateCache($"cache-ef:{Guid.NewGuid():N}:");
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(Substitute.For<IConfiguration>());
        services.AddSingleton(Substitute.For<IHostEnvironment>());
        services.AddDbContext<AppDbContext>(db => db.UseSqlite(connection));
        services.AddTickerQ(options =>
        {
            options.DisableBackgroundServices();
            options.UseApplicationNamespace("cache-deploy");
            options.ConfigureScheduler(s => s.DefinedCronRetirementGracePeriod = TimeSpan.Zero);
            options.AddOperationalStore(ef => ef.UseApplicationDbContext<AppDbContext>(ConfigurationType.UseModelCustomizer));
        });
        services.AddSingleton<ITickerQRedisContext>(CreateContext(cache));
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.EnsureCreatedAsync();
        var persistence = provider.GetRequiredService<ITickerPersistenceProvider<TimeTickerEntity, CronTickerEntity>>();
        var initial = new DefinedCronSeedManifest("cache-deploy",
            [new("a", "0 * * * * *"), new("b", "0 * * * * *")]);
        await persistence.MigrateDefinedCronTickers(initial, CancellationToken.None);
        var before = await persistence.GetAllCronTickerExpressions(CancellationToken.None);
        Assert.Equal(["a", "b"], before.Select(x => x.Function).Order().ToArray());
        var oldId = before.Single(x => x.Function == "b").Id;
        var next = new DefinedCronSeedManifest("cache-deploy",
            [new("a", "0 */2 * * * *"), new("c", "0 * * * * *")]);
        await persistence.MigrateDefinedCronTickers(next, CancellationToken.None);
        // Retirement is two-phase, even with no grace period: request, then retire.
        await persistence.MigrateDefinedCronTickers(next, CancellationToken.None);
        var after = await persistence.GetAllCronTickerExpressions(CancellationToken.None);
        Assert.Equal(["a", "c"], after.Select(x => x.Function).Order().ToArray());
        Assert.Equal("0 */2 * * * *", after.Single(x => x.Function == "a").Expression);
        Assert.True(after.Single(x => x.Function == "a").DefinitionRevision > before.Single(x => x.Function == "a").DefinitionRevision);
        Assert.False((await db.Set<CronTickerEntity>().AsNoTracking().SingleAsync(x => x.Id == oldId)).IsEnabled);
    }
}
