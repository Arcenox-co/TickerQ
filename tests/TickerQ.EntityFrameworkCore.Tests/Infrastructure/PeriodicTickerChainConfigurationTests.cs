using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TickerQ.EntityFrameworkCore.Configurations;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Enums;

namespace TickerQ.EntityFrameworkCore.Tests.Infrastructure;

/// <summary>
/// Test DbContext that maps PeriodicTickers so the ChainTemplate JSON column can be exercised end-to-end.
/// </summary>
public class PeriodicTestDbContext : DbContext
{
    public PeriodicTestDbContext(DbContextOptions<PeriodicTestDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new PeriodicTickerConfigurations<PeriodicTickerEntity>("ticker"));
        base.OnModelCreating(modelBuilder);
    }
}

public class PeriodicTickerChainConfigurationTests : IAsyncLifetime
{
    private SqliteConnection _connection;
    private PeriodicTestDbContext _context;

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        await _connection.OpenAsync();

        var options = new DbContextOptionsBuilder<PeriodicTestDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new PeriodicTestDbContext(options);
        await _context.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        await _context.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task ChainTemplate_RoundTrips_Through_JsonColumn()
    {
        var template = new[]
        {
            new PeriodicChainStep
            {
                Function = "ScheduledCalculations",
                RunCondition = RunCondition.OnSuccess,
                Retries = 2,
                RetryIntervals = new[] { 5, 10 },
                Request = new byte[] { 1, 2, 3 },
                Children = new System.Collections.Generic.List<PeriodicChainStep>
                {
                    new PeriodicChainStep { Function = "PublishReport", RunCondition = RunCondition.OnSuccess }
                }
            }
        };

        var ticker = new PeriodicTickerEntity
        {
            Id = Guid.NewGuid(),
            Function = "PollPort",
            Interval = TimeSpan.FromMinutes(1),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            ChainTemplate = template,
            ChainOverlapBehavior = ChainOverlapBehavior.Skip
        };

        _context.Set<PeriodicTickerEntity>().Add(ticker);
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();

        var fromDb = await _context.Set<PeriodicTickerEntity>()
            .AsNoTracking()
            .FirstAsync(e => e.Id == ticker.Id);

        Assert.NotNull(fromDb.ChainTemplate);
        Assert.Single(fromDb.ChainTemplate);
        Assert.Equal("ScheduledCalculations", fromDb.ChainTemplate[0].Function);
        Assert.Equal(RunCondition.OnSuccess, fromDb.ChainTemplate[0].RunCondition);
        Assert.Equal(2, fromDb.ChainTemplate[0].Retries);
        Assert.Equal(new[] { 5, 10 }, fromDb.ChainTemplate[0].RetryIntervals);
        Assert.Equal(new byte[] { 1, 2, 3 }, fromDb.ChainTemplate[0].Request);
        Assert.Single(fromDb.ChainTemplate[0].Children);
        Assert.Equal("PublishReport", fromDb.ChainTemplate[0].Children[0].Function);
        Assert.Equal(ChainOverlapBehavior.Skip, fromDb.ChainOverlapBehavior);
    }

    [Fact]
    public async Task ChainTemplate_Null_RoundTrips_AsNull()
    {
        var ticker = new PeriodicTickerEntity
        {
            Id = Guid.NewGuid(),
            Function = "PollPort",
            Interval = TimeSpan.FromMinutes(1),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            ChainTemplate = null
        };

        _context.Set<PeriodicTickerEntity>().Add(ticker);
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();

        var fromDb = await _context.Set<PeriodicTickerEntity>()
            .AsNoTracking()
            .FirstAsync(e => e.Id == ticker.Id);

        Assert.Null(fromDb.ChainTemplate);
        Assert.Equal(ChainOverlapBehavior.Allow, fromDb.ChainOverlapBehavior);
    }
}

