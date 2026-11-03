using MongoDB.Driver;
using Testcontainers.MongoDb;
using TickerQ.MongoDB.DependencyInjection;
using TickerQ.Tests.Shared.DashboardApi;
using TickerQ.Utilities;
using TickerQ.Utilities.Entities;

namespace TickerQ.MongoDB.Tests;

/// <summary>A MongoDB replica set in Docker; each test gets its own database, dropped afterwards.</summary>
public sealed class MongoDashboardApiServer : IDashboardApiServer, IAsyncLifetime
{
    private MongoDbContainer _container;

    public async Task InitializeAsync()
    {
        if (!DashboardApiFactAttribute.Enabled) return;
        // Pending chain cancellation and other graph writes need transactions, so a replica set.
        _container = new MongoDbBuilder("mongo:7").WithReplicaSet().Build();
        await _container.StartAsync();
    }

    public async Task DisposeAsync()
    {
        if (_container != null) await _container.DisposeAsync();
    }

    public Task<DashboardApiStore> CreateStoreAsync()
        => Task.FromResult<DashboardApiStore>(
            new MongoDashboardApiStore(_container.GetConnectionString(), "dashboard_" + Guid.NewGuid().ToString("N")[..12]));

    private sealed class MongoDashboardApiStore(string connectionString, string database) : DashboardApiStore
    {
        public override void Configure(TickerOptionsBuilder<TimeTickerEntity, CronTickerEntity> tickerq)
            => tickerq.AddOperationalStore(mongo => mongo.UseTickerQMongoClient(connectionString, database));

        public override async ValueTask DisposeAsync()
        {
            try
            {
                await new MongoClient(connectionString).DropDatabaseAsync(database);
            }
            catch
            {
                // Best effort: a leftover database must not mask the test result.
            }
        }
    }
}

[CollectionDefinition("MongoDB Dashboard API", DisableParallelization = true)]
public sealed class MongoDashboardApiCollection : ICollectionFixture<MongoDashboardApiServer>;

[Collection("MongoDB Dashboard API")]
public sealed class MongoDashboardApiTests(MongoDashboardApiServer server) : DashboardApiContract(server);
