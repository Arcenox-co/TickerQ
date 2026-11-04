using StackExchange.Redis;
using Testcontainers.Redis;
using TickerQ.Caching.StackExchangeRedis.DependencyInjection;
using TickerQ.Tests.Shared.DashboardApi;
using TickerQ.Utilities;
using TickerQ.Utilities.Entities;

namespace TickerQ.Caching.StackExchangeRedis.Tests;

/// <summary>Redis in Docker as the operational store; each test starts from an empty server.</summary>
public sealed class RedisDashboardApiServer : IDashboardApiServer, IAsyncLifetime
{
    private RedisContainer _container;

    public async Task InitializeAsync()
    {
        if (!DashboardApiFactAttribute.Enabled) return;
        _container = new RedisBuilder("redis:7").Build();
        await _container.StartAsync();
    }

    public async Task DisposeAsync()
    {
        if (_container != null) await _container.DisposeAsync();
    }

    // Tests in this assembly never run in parallel, so emptying the server isolates each one.
    public async Task<DashboardApiStore> CreateStoreAsync()
    {
        var connection = _container.GetConnectionString();
        await using var multiplexer = await ConnectionMultiplexer.ConnectAsync(connection + ",allowAdmin=true");
        await multiplexer.GetServer(multiplexer.GetEndPoints()[0]).FlushAllDatabasesAsync();
        return new RedisDashboardApiStore(connection);
    }

    private sealed class RedisDashboardApiStore(string connection) : DashboardApiStore
    {
        public override void Configure(TickerOptionsBuilder<TimeTickerEntity, CronTickerEntity> tickerq)
            => tickerq.AddStackExchangeRedis(redis => redis.Configuration = connection);
    }
}

[CollectionDefinition("Redis Dashboard API", DisableParallelization = true)]
public sealed class RedisDashboardApiCollection : ICollectionFixture<RedisDashboardApiServer>;

[Collection("Redis Dashboard API")]
public sealed class RedisDashboardApiTests(RedisDashboardApiServer server) : DashboardApiContract(server);
