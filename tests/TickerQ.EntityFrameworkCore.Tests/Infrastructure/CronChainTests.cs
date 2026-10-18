using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using TickerQ.Tests.Shared;
using TickerQ.Utilities;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Interfaces;

namespace TickerQ.EntityFrameworkCore.Tests.Infrastructure;

public sealed class CronChainTests : CronChainContract<TimeTickerEntity, CronTickerEntity>, IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private ServiceProvider _services = null!;
    private TestableProvider _provider = null!;
    protected override ITickerPersistenceProvider<TimeTickerEntity, CronTickerEntity> Provider => _provider;
    protected override DateTime Now => new(2026, 10, 17, 12, 0, 0, DateTimeKind.Utc);
    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        var options = new DbContextOptionsBuilder<TestTickerQDbContext>().UseSqlite(_connection).Options;
        await using var db = new TestTickerQDbContext(options);
        await db.Database.EnsureCreatedAsync();
        _services = new ServiceCollection().AddSingleton<IDbContextFactory<TestTickerQDbContext>>(new PooledDbContextFactory<TestTickerQDbContext>(options)).BuildServiceProvider();
        var clock = Substitute.For<ITickerClock>(); clock.UtcNow.Returns(Now);
        var cache = Substitute.For<ITickerQRedisContext>();
        cache.GetOrSetArrayAsync(Arg.Any<string>(), Arg.Any<Func<CancellationToken, Task<CronTickerEntity[]>>>(),
                Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>())
            .Returns(call => call.ArgAt<Func<CancellationToken, Task<CronTickerEntity[]>>>(1)(call.ArgAt<CancellationToken>(3)));
        _provider = new TestableProvider(_services, clock, new SchedulerOptionsBuilder(), cache);
    }
    public async Task DisposeAsync() { await _services.DisposeAsync(); await _connection.DisposeAsync(); }

}
