using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using TickerQ.Utilities.Entities;

namespace TickerQ.EntityFrameworkCore.Tests.Infrastructure;

public class CronTickerConfigurationTests : IAsyncLifetime
{
    private SqliteConnection _connection;
    private DbContextOptions<TestTickerQDbContext> _options;
    private TestTickerQDbContext _context;

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        await _connection.OpenAsync();

        _options = new DbContextOptionsBuilder<TestTickerQDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new TestTickerQDbContext(_options);
        await _context.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        await _context.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public void IsEnabled_Has_No_Database_Default()
    {
        var entityType = _context.Model.FindEntityType(typeof(CronTickerEntity))!;
        var property = entityType.FindProperty(nameof(CronTickerEntity.IsEnabled))!;

        // No database default — the CLR property initializer (= true) handles the default.
        // This avoids any provider-specific SQL translation concerns.
        Assert.Equal(ValueGenerated.Never, property.ValueGenerated);
        Assert.Null(property.GetDefaultValueSql());
    }

    [Fact]
    public void IsEnabled_CLR_Default_Is_True()
    {
        var entity = new CronTickerEntity();
        Assert.True(entity.IsEnabled);
    }

    [Fact]
    public void IsEnabled_Is_Required()
    {
        var entityType = _context.Model.FindEntityType(typeof(CronTickerEntity))!;
        var property = entityType.FindProperty(nameof(CronTickerEntity.IsEnabled))!;

        Assert.False(property.IsNullable);
    }

    [Fact]
    public async Task Insert_CronTicker_Without_IsEnabled_Gets_Default_True()
    {
        // The C# property initializer sets IsEnabled = true;
        // EF Core always includes it in the INSERT — no DB default needed.
        var ticker = new CronTickerEntity
        {
            Id = Guid.NewGuid(),
            Function = "TestFunc",
            Expression = "* * * * *",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            Request = Array.Empty<byte>()
        };

        _context.Set<CronTickerEntity>().Add(ticker);
        await _context.SaveChangesAsync();

        // Detach and re-read from DB to verify
        _context.ChangeTracker.Clear();
        var fromDb = await _context.Set<CronTickerEntity>()
            .AsNoTracking()
            .FirstAsync(e => e.Id == ticker.Id);

        Assert.True(fromDb.IsEnabled);
    }

    [Fact]
    public async Task Insert_CronTicker_With_IsEnabled_False_Persists()
    {
        var ticker = new CronTickerEntity
        {
            Id = Guid.NewGuid(),
            Function = "TestFunc",
            Expression = "* * * * *",
            IsEnabled = false,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            Request = Array.Empty<byte>()
        };

        _context.Set<CronTickerEntity>().Add(ticker);
        await _context.SaveChangesAsync();

        _context.ChangeTracker.Clear();
        var fromDb = await _context.Set<CronTickerEntity>()
            .AsNoTracking()
            .FirstAsync(e => e.Id == ticker.Id);

        Assert.False(fromDb.IsEnabled);
    }

    [Fact]
    public async Task Toggle_IsEnabled_RoundTrips_Correctly()
    {
        var ticker = new CronTickerEntity
        {
            Id = Guid.NewGuid(),
            Function = "TestFunc",
            Expression = "* * * * *",
            IsEnabled = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            Request = Array.Empty<byte>()
        };

        _context.Set<CronTickerEntity>().Add(ticker);
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();

        // Toggle to false
        var entity = await _context.Set<CronTickerEntity>().FirstAsync(e => e.Id == ticker.Id);
        entity.IsEnabled = false;
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();

        var fromDb = await _context.Set<CronTickerEntity>()
            .AsNoTracking()
            .FirstAsync(e => e.Id == ticker.Id);
        Assert.False(fromDb.IsEnabled);

        // Toggle back to true
        var entity2 = await _context.Set<CronTickerEntity>().FirstAsync(e => e.Id == ticker.Id);
        entity2.IsEnabled = true;
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();

        var fromDb2 = await _context.Set<CronTickerEntity>()
            .AsNoTracking()
            .FirstAsync(e => e.Id == ticker.Id);
        Assert.True(fromDb2.IsEnabled);
    }

    [Fact]
    public async Task Where_IsEnabled_Filter_Returns_Only_Enabled()
    {
        var enabled = new CronTickerEntity
        {
            Id = Guid.NewGuid(),
            Function = "Enabled",
            Expression = "* * * * *",
            IsEnabled = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            Request = Array.Empty<byte>()
        };
        var disabled = new CronTickerEntity
        {
            Id = Guid.NewGuid(),
            Function = "Disabled",
            Expression = "* * * * *",
            IsEnabled = false,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            Request = Array.Empty<byte>()
        };

        _context.Set<CronTickerEntity>().AddRange(enabled, disabled);
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();

        var results = await _context.Set<CronTickerEntity>()
            .AsNoTracking()
            .Where(e => e.IsEnabled)
            .ToListAsync();

        Assert.Single(results);
        Assert.Equal(enabled.Id, results[0].Id);
    }
}
