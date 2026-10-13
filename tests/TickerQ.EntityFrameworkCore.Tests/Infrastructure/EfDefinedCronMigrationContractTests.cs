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
/// Runs the shared <see cref="DefinedCronMigrationContractTests"/> against the EF Core provider over a
/// fresh in-memory SQLite database, mirroring <see cref="EfProviderReliabilityContractTests"/>.
/// </summary>
public sealed class EfDefinedCronMigrationContractTests : DefinedCronMigrationContractTests, IAsyncLifetime
{
    private SqliteConnection _connection = null!;
    private TestableProvider _provider = null!;
    private readonly DateTime _fixedNow = new(2025, 6, 15, 12, 0, 0, DateTimeKind.Utc);

    protected override ITickerPersistenceProvider<TimeTickerEntity, CronTickerEntity> Provider => _provider;
    protected override DateTime Now => _fixedNow;

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

        var dbOptions = new DbContextOptionsBuilder<TestTickerQDbContext>()
            .UseSqlite(_connection)
            .Options;

        await using (var seedContext = new TestTickerQDbContext(dbOptions))
            await seedContext.Database.EnsureCreatedAsync();

        var options = new SchedulerOptionsBuilder { NodeIdentifier = "cron-migration-node" };

        var services = new ServiceCollection();
        services.AddSingleton<IDbContextFactory<TestTickerQDbContext>>(
            new PooledDbContextFactory<TestTickerQDbContext>(dbOptions));
        var serviceProvider = services.BuildServiceProvider();

        _provider = new TestableProvider(serviceProvider, clock, options, redisContext);
    }

    public async Task DisposeAsync() => await _connection.DisposeAsync();
}
