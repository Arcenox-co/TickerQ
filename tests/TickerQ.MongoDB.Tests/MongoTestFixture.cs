using MongoDB.Driver;
using NSubstitute;
using Testcontainers.MongoDb;
using TickerQ.MongoDB.Indexes;
using TickerQ.MongoDB.Infrastructure;
using TickerQ.MongoDB.Serialization;
using TickerQ.Utilities;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Interfaces;

namespace TickerQ.MongoDB.Tests;

/// <summary>
/// Shared MongoDB container for the whole test class — spinning a container per test is too slow.
/// Each test should call <see cref="DropAllAsync"/> in its setup to start from a clean DB.
/// </summary>
public class MongoTestFixture : IAsyncLifetime
{
    public MongoDbContainer Container { get; } = new MongoDbBuilder()
        .WithImage("mongo:7")
        .Build();

    public IMongoClient Client { get; private set; } = null!;
    public IMongoDatabase Database { get; private set; } = null!;
    public IMongoCollection<TimeTickerEntity> TimeTickers => _context.TimeTickers;
    public IMongoCollection<CronTickerEntity> CronTickers => _context.CronTickers;
    public IMongoCollection<CronTickerOccurrenceEntity<CronTickerEntity>> CronTickerOccurrences => _context.CronTickerOccurrences;
    public ITickerPersistenceProvider<TimeTickerEntity, CronTickerEntity> Provider { get; private set; } = null!;
    public ITickerClock Clock { get; private set; } = null!;
    public DateTime FixedNow { get; } = new(2025, 6, 15, 12, 0, 0, DateTimeKind.Utc);
    public const string NodeId = "test-node-1";

    private ITickerMongoContext<TimeTickerEntity, CronTickerEntity> _context = null!;

    public async Task InitializeAsync()
    {
        await Container.StartAsync();

        TickerClassMaps.RegisterOnce<TimeTickerEntity, CronTickerEntity>();

        Client = new MongoClient(Container.GetConnectionString());
        Database = Client.GetDatabase("tickerq_test");
        _context = new TickerMongoContext<TimeTickerEntity, CronTickerEntity>(Database, "ticker_");

        Clock = Substitute.For<ITickerClock>();
        Clock.UtcNow.Returns(FixedNow);

        var options = new SchedulerOptionsBuilder { NodeIdentifier = NodeId };
        Provider = new TickerMongoPersistenceProvider<TimeTickerEntity, CronTickerEntity>(_context, Clock, options);

        var provisioner = new TickerIndexProvisioner<TimeTickerEntity, CronTickerEntity>(_context);
        await provisioner.StartAsync(CancellationToken.None);
    }

    public async Task DisposeAsync()
    {
        await Container.DisposeAsync();
    }

    public async Task DropAllAsync()
    {
        await TimeTickers.DeleteManyAsync(Builders<TimeTickerEntity>.Filter.Empty);
        await CronTickers.DeleteManyAsync(Builders<CronTickerEntity>.Filter.Empty);
        await CronTickerOccurrences.DeleteManyAsync(Builders<CronTickerOccurrenceEntity<CronTickerEntity>>.Filter.Empty);
    }

    /// <summary>Internal helper to instantiate a fresh provisioner for idempotency tests.</summary>
    internal TickerIndexProvisioner<TimeTickerEntity, CronTickerEntity> NewProvisioner()
        => new(_context);
}
