using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using TickerQ.Utilities.Entities;
using TickerQ.EntityFrameworkCore.Entities;

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
    public void EveryRuntimeEntity_HasBoundedRequiredPartitionOwnershipAndPartitionLeadingIdentity()
    {
        var runtimeTypes = new[]
        {
            typeof(TimeTickerEntity), typeof(CronTickerEntity),
            typeof(CronTickerOccurrenceEntity<CronTickerEntity>),
            typeof(TimeTickerResultEntity<TimeTickerEntity>),
            typeof(CronTickerOccurrenceResultEntity<CronTickerEntity>),
            typeof(NodeFinalizationOutboxEntity), typeof(TickerQStoreMetadata)
        };

        foreach (var clrType in runtimeTypes)
        {
            var entity = _context.Model.FindEntityType(clrType)!;
            var partition = entity.FindProperty("ApplicationNamespaceKey");
            Assert.NotNull(partition);
            Assert.False(partition!.IsNullable);
            Assert.Equal(80, partition.GetMaxLength());
            Assert.Equal("ApplicationNamespaceKey", entity.FindPrimaryKey()!.Properties[0].Name);
            Assert.All(entity.GetIndexes().Where(index => index.Properties.Any(property =>
                    property.Name is "Status" or "ExecutionTime" or "TickerId" or "CronTickerId")),
                index => Assert.Equal("ApplicationNamespaceKey", index.Properties[0].Name));
        }
    }

    [Fact]
    public void RequestContractIdentity_IsNullableAndFingerprintIsBounded()
    {
        foreach (var entityType in new[]
                 {
                     _context.Model.FindEntityType(typeof(CronTickerEntity))!,
                     _context.Model.FindEntityType(typeof(TimeTickerEntity))!
                 })
        {
            var version = entityType.FindProperty(nameof(CronTickerEntity.RequestContractVersion))!;
            var fingerprint = entityType.FindProperty(nameof(CronTickerEntity.RequestContractFingerprint))!;

            Assert.True(version.IsNullable);
            Assert.True(fingerprint.IsNullable);
            Assert.Equal(128, fingerprint.GetMaxLength());
        }
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
    public void DefinitionRevision_IsAConcurrencyToken()
    {
        var entityType = _context.Model.FindEntityType(typeof(CronTickerEntity))!;
        var property = entityType.FindProperty(nameof(CronTickerEntity.DefinitionRevision))!;

        Assert.True(property.IsConcurrencyToken);
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

    // The SeedKey unique index must be provider-portable: it must NOT carry a hardcoded
    // provider-specific partial-index predicate (the old `"SeedKey" IS NOT NULL`, whose
    // double-quote identifier quoting is wrong on SQL Server). Instead it relies on each EF
    // provider's own nullable-unique-index convention (SqlServer auto-adds the null filter;
    // SQLite/PostgreSQL treat NULLs as distinct natively). Asserting the relational filter is
    // null proves we no longer pin a single provider's SQL into the model.
    [Fact]
    public void SeedKey_UniqueIndex_HasNoHardcodedProviderSpecificFilter()
    {
        var entityType = _context.Model.FindEntityType(typeof(CronTickerEntity))!;
        var index = entityType.GetIndexes()
            .Single(i => i.Properties.Count == 2
                         && i.Properties[0].Name == nameof(CronTickerEntity.ApplicationNamespaceKey)
                         && i.Properties[1].Name == nameof(CronTickerEntity.SeedKey));

        Assert.True(index.IsUnique);
        Assert.Null(index.GetFilter());
    }

    // Provider-portable nullable uniqueness on SQLite: any number of rows may carry a null
    // SeedKey (legacy/user/dashboard rows and retired duplicates), while a non-null SeedKey is
    // unique. This is the invariant the two-phase legacy adoption depends on.
    [Fact]
    public async Task SeedKey_MultipleNullRows_Coexist_AndNonNullIsUnique_OnSqlite()
    {
        CronTickerEntity Row(string seedKey) => new()
        {
            Id = Guid.NewGuid(),
            Function = "Fn",
            Expression = "* * * * *",
            SeedKey = seedKey,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            Request = Array.Empty<byte>()
        };

        // Three null-SeedKey rows plus one non-null all coexist under the unique index.
        _context.Set<CronTickerEntity>().AddRange(Row(null), Row(null), Row(null), Row("owner-a"));
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();

        Assert.Equal(3, await _context.Set<CronTickerEntity>().CountAsync(e => e.SeedKey == null));
        Assert.Equal(1, await _context.Set<CronTickerEntity>().CountAsync(e => e.SeedKey == "owner-a"));

        // A second row with the SAME non-null SeedKey violates the unique index.
        _context.Set<CronTickerEntity>().Add(Row("owner-a"));
        await Assert.ThrowsAsync<DbUpdateException>(() => _context.SaveChangesAsync());
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
