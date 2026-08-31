using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using TickerQ.Utilities;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Models;
using Xunit;

namespace TickerQ.EntityFrameworkCore.Tests.Infrastructure;

public sealed class EfCoreRetentionFocusedTests : IAsyncLifetime
{
    private readonly DateTime _now = new(2026, 8, 27, 12, 0, 0, DateTimeKind.Utc);
    private SqliteConnection _connection;
    private TestTickerQDbContext _context;
    private ITickerPersistenceProvider<TimeTickerEntity, CronTickerEntity> _provider;

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        await _connection.OpenAsync();
        var options = new DbContextOptionsBuilder<TestTickerQDbContext>().UseSqlite(_connection).Options;
        _context = new TestTickerQDbContext(options);
        await _context.Database.EnsureCreatedAsync();

        var clock = Substitute.For<ITickerClock>();
        clock.UtcNow.Returns(_now);
        var redis = Substitute.For<ITickerQRedisContext>();
        redis.HasRedisConnection.Returns(false);
        var services = new ServiceCollection();
        services.AddSingleton<IDbContextFactory<TestTickerQDbContext>>(new PooledDbContextFactory<TestTickerQDbContext>(options));
        _provider = new TestableProvider(services.BuildServiceProvider(), clock, new SchedulerOptionsBuilder(), redis);
    }

    public async Task DisposeAsync()
    {
        await _context.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task ExpiredSuccessfulTicker_IsDeleted_WhileRecentTickerRemains()
    {
        var expired = CreateDone(_now.AddMinutes(-10));
        var recent = CreateDone(_now.AddSeconds(-10));
        _context.Set<TimeTickerEntity>().AddRange(expired, recent);
        await _context.SaveChangesAsync();

        var result = await _provider.DeleteEligibleTimeTickerChainsAsync(
            new RetentionCutoffs(_now.AddMinutes(-1), null, null, null),
            batchSize: 10,
            RetentionCursor.Start);

        Assert.Equal(1, result.Deleted);
        Assert.False(await _context.Set<TimeTickerEntity>().AnyAsync(x => x.Id == expired.Id));
        Assert.True(await _context.Set<TimeTickerEntity>().AnyAsync(x => x.Id == recent.Id));
    }

    private TimeTickerEntity CreateDone(DateTime executedAt) => new()
    {
        Id = Guid.NewGuid(),
        Function = "RetentionProbe",
        ExecutionTime = executedAt,
        ExecutedAt = executedAt,
        Status = TickerStatus.Done,
        CreatedAt = executedAt,
        UpdatedAt = executedAt,
        Request = Array.Empty<byte>()
    };
}
