using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

using NSubstitute;

using TickerQ.Tests.Shared.ProviderReliability;
using TickerQ.Utilities;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Interfaces;

namespace TickerQ.EntityFrameworkCore.Tests.Infrastructure;

/// <summary>
/// Runs the shared provider reliability contract against the EF Core provider over a
/// real SQLite (in-memory) database. Setup mirrors <see cref="EfCorePersistenceProviderTests"/>
/// and reuses its <c>TestTickerQDbContext</c>/<c>TestableProvider</c> seams so no
/// expensive setup is duplicated; each test starts from a fresh schema.
/// </summary>
public sealed class EfProviderReliabilityContractTests : ProviderReliabilityContractTests, IAsyncLifetime
{
    private SqliteConnection _connection = null!;
    private DbContextOptions<TestTickerQDbContext> _dbOptions = null!;
    private TestableProvider _provider = null!;
    private SchedulerOptionsBuilder _options = null!;
    private readonly DateTime _fixedNow = new(2025, 6, 15, 12, 0, 0, DateTimeKind.Utc);

    protected override ITickerPersistenceProvider<TimeTickerEntity, CronTickerEntity> Provider => _provider;
    protected override DateTime Now => _fixedNow;
    protected override string OwnerId => _options.ExecutionOwnerId;
    protected override SchedulerOptionsBuilder Options => _options;

    // SQLite in-memory shares a single connection across pooled contexts, which serializes
    // writes; two concurrent acquirers are enough to prove the atomic CAS picks one winner.
    protected override int RaceParallelism => 2;

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        await _connection.OpenAsync();

        var clock = Substitute.For<ITickerClock>();
        clock.UtcNow.Returns(_fixedNow);

        var redisContext = Substitute.For<ITickerQRedisContext>();
        redisContext.HasRedisConnection.Returns(false);
        redisContext.GetOrSetArrayAsync(
            Arg.Any<string>(),
            Arg.Any<Func<CancellationToken, Task<CronTickerEntity[]>>>(),
            Arg.Any<TimeSpan?>(),
            Arg.Any<CancellationToken>()
        ).Returns(callInfo =>
        {
            var factory = callInfo.ArgAt<Func<CancellationToken, Task<CronTickerEntity[]>>>(1);
            return factory(CancellationToken.None);
        });

        _dbOptions = new DbContextOptionsBuilder<TestTickerQDbContext>()
            .UseSqlite(_connection)
            .Options;

        await using (var seedContext = new TestTickerQDbContext(_dbOptions))
            await seedContext.Database.EnsureCreatedAsync();

        _options = new SchedulerOptionsBuilder { NodeIdentifier = "contract-node" };

        var services = new ServiceCollection();
        services.AddSingleton<IDbContextFactory<TestTickerQDbContext>>(
            new PooledDbContextFactory<TestTickerQDbContext>(_dbOptions));
        var serviceProvider = services.BuildServiceProvider();

        _provider = new TestableProvider(serviceProvider, clock, _options, redisContext);
    }

    public async Task DisposeAsync()
    {
        await _connection.DisposeAsync();
    }
}
