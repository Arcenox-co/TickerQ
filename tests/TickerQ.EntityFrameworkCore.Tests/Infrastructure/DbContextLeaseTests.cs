using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

using TickerQ.EntityFrameworkCore.Configurations;
using TickerQ.EntityFrameworkCore.DbContextFactory;
using TickerQ.Utilities.Entities;

namespace TickerQ.EntityFrameworkCore.Tests.Infrastructure;

/// <summary>
/// Minimal DbContext for lease resolution tests.
/// </summary>
public class LeaseTestDbContext : DbContext
{
    public LeaseTestDbContext(DbContextOptions<LeaseTestDbContext> options) : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new TimeTickerConfigurations<TimeTickerEntity>("ticker"));
        modelBuilder.ApplyConfiguration(new CronTickerConfigurations<CronTickerEntity>("ticker"));
        modelBuilder.ApplyConfiguration(new CronTickerOccurrenceConfigurations<CronTickerEntity>("ticker"));
        base.OnModelCreating(modelBuilder);
    }
}

public class DbContextLeaseTests : IAsyncLifetime
{
    private SqliteConnection _connection = null!;
    private DbContextOptions<LeaseTestDbContext> _options = null!;

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        await _connection.OpenAsync();

        _options = new DbContextOptionsBuilder<LeaseTestDbContext>()
            .UseSqlite(_connection)
            .Options;

        await using var ctx = new LeaseTestDbContext(_options);
        await ctx.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        await _connection.DisposeAsync();
    }

    // =========================================================================
    // Factory path — IDbContextFactory<T> registered
    // =========================================================================

    [Fact]
    public async Task CreateAsync_WithDbContextFactory_UsesFactory()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IDbContextFactory<LeaseTestDbContext>>(
            new PooledDbContextFactory<LeaseTestDbContext>(_options));
        var sp = services.BuildServiceProvider();

        using var lease = await DbContextLease<LeaseTestDbContext>.CreateAsync(sp, CancellationToken.None);

        Assert.NotNull(lease.Context);
        Assert.IsType<LeaseTestDbContext>(lease.Context);

        // Verify context is functional
        var count = await lease.Context.Set<TimeTickerEntity>().CountAsync();
        Assert.Equal(0, count);
    }

    [Fact]
    public void Create_WithDbContextFactory_UsesFactory()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IDbContextFactory<LeaseTestDbContext>>(
            new PooledDbContextFactory<LeaseTestDbContext>(_options));
        var sp = services.BuildServiceProvider();

        using var lease = DbContextLease<LeaseTestDbContext>.Create(sp);

        Assert.NotNull(lease.Context);
        Assert.IsType<LeaseTestDbContext>(lease.Context);
    }

    // =========================================================================
    // Scoped path — AddDbContext<T> (no factory)
    // =========================================================================

    [Fact]
    public async Task CreateAsync_WithScopedDbContext_CreatesScope()
    {
        var services = new ServiceCollection();
        services.AddDbContext<LeaseTestDbContext>(opt => opt.UseSqlite(_connection));
        var sp = services.BuildServiceProvider();

        using var lease = await DbContextLease<LeaseTestDbContext>.CreateAsync(sp, CancellationToken.None);

        Assert.NotNull(lease.Context);
        Assert.IsType<LeaseTestDbContext>(lease.Context);

        // Verify context is functional
        var count = await lease.Context.Set<TimeTickerEntity>().CountAsync();
        Assert.Equal(0, count);
    }

    [Fact]
    public void Create_WithScopedDbContext_CreatesScope()
    {
        var services = new ServiceCollection();
        services.AddDbContext<LeaseTestDbContext>(opt => opt.UseSqlite(_connection));
        var sp = services.BuildServiceProvider();

        using var lease = DbContextLease<LeaseTestDbContext>.Create(sp);

        Assert.NotNull(lease.Context);
        Assert.IsType<LeaseTestDbContext>(lease.Context);
    }

    // =========================================================================
    // Pooled factory path — AddPooledDbContextFactory<T>
    // =========================================================================

    [Fact]
    public async Task CreateAsync_WithPooledDbContextFactory_UsesPool()
    {
        var services = new ServiceCollection();
        services.AddPooledDbContextFactory<LeaseTestDbContext>(opt => opt.UseSqlite(_connection));
        var sp = services.BuildServiceProvider();

        using var lease = await DbContextLease<LeaseTestDbContext>.CreateAsync(sp, CancellationToken.None);

        Assert.NotNull(lease.Context);
        Assert.IsType<LeaseTestDbContext>(lease.Context);

        var count = await lease.Context.Set<TimeTickerEntity>().CountAsync();
        Assert.Equal(0, count);
    }

    // =========================================================================
    // Factory takes precedence over scoped registration
    // =========================================================================

    [Fact]
    public async Task CreateAsync_BothFactoryAndScoped_PrefersFactory()
    {
        var services = new ServiceCollection();
        // Register both — factory should win
        services.AddDbContext<LeaseTestDbContext>(opt => opt.UseSqlite(_connection));
        services.AddSingleton<IDbContextFactory<LeaseTestDbContext>>(
            new PooledDbContextFactory<LeaseTestDbContext>(_options));
        var sp = services.BuildServiceProvider();

        using var lease = await DbContextLease<LeaseTestDbContext>.CreateAsync(sp, CancellationToken.None);

        Assert.NotNull(lease.Context);

        // Verify functional
        var count = await lease.Context.Set<TimeTickerEntity>().CountAsync();
        Assert.Equal(0, count);
    }

    // =========================================================================
    // Isolation — each lease gets its own DbContext instance
    // =========================================================================

    [Fact]
    public async Task CreateAsync_MultipleCalls_ReturnsDifferentInstances()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IDbContextFactory<LeaseTestDbContext>>(
            new PooledDbContextFactory<LeaseTestDbContext>(_options));
        var sp = services.BuildServiceProvider();

        using var lease1 = await DbContextLease<LeaseTestDbContext>.CreateAsync(sp, CancellationToken.None);
        using var lease2 = await DbContextLease<LeaseTestDbContext>.CreateAsync(sp, CancellationToken.None);

        Assert.NotSame(lease1.Context, lease2.Context);
    }

    [Fact]
    public async Task CreateAsync_ScopedPath_MultipleCalls_ReturnsDifferentInstances()
    {
        var services = new ServiceCollection();
        services.AddDbContext<LeaseTestDbContext>(opt => opt.UseSqlite(_connection));
        var sp = services.BuildServiceProvider();

        using var lease1 = await DbContextLease<LeaseTestDbContext>.CreateAsync(sp, CancellationToken.None);
        using var lease2 = await DbContextLease<LeaseTestDbContext>.CreateAsync(sp, CancellationToken.None);

        Assert.NotSame(lease1.Context, lease2.Context);
    }

    // =========================================================================
    // Dispose — context is disposed after lease
    // =========================================================================

    [Fact]
    public async Task Dispose_FactoryPath_DisposesContext()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IDbContextFactory<LeaseTestDbContext>>(
            new PooledDbContextFactory<LeaseTestDbContext>(_options));
        var sp = services.BuildServiceProvider();

        LeaseTestDbContext capturedContext;
        using (var lease = await DbContextLease<LeaseTestDbContext>.CreateAsync(sp, CancellationToken.None))
        {
            capturedContext = lease.Context;
            // Context is alive inside the lease
            Assert.NotNull(capturedContext.Model);
        }

        // After dispose, context should be disposed (querying throws)
        await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
            await capturedContext.Set<TimeTickerEntity>().CountAsync());
    }

    [Fact]
    public async Task Dispose_ScopedPath_DisposesScope()
    {
        var services = new ServiceCollection();
        services.AddDbContext<LeaseTestDbContext>(opt => opt.UseSqlite(_connection));
        var sp = services.BuildServiceProvider();

        LeaseTestDbContext capturedContext;
        using (var lease = await DbContextLease<LeaseTestDbContext>.CreateAsync(sp, CancellationToken.None))
        {
            capturedContext = lease.Context;
            Assert.NotNull(capturedContext.Model);
        }

        // After dispose, the scope is disposed, so resolving from it or using the context should fail
        await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
            await capturedContext.Set<TimeTickerEntity>().CountAsync());
    }

    // =========================================================================
    // Change tracking isolation — leases don't share tracked entities
    // =========================================================================

    [Fact]
    public async Task ChangeTracking_LeasesAreIsolated()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IDbContextFactory<LeaseTestDbContext>>(
            new PooledDbContextFactory<LeaseTestDbContext>(_options));
        var sp = services.BuildServiceProvider();

        // Add an entity via lease1
        using (var lease1 = await DbContextLease<LeaseTestDbContext>.CreateAsync(sp, CancellationToken.None))
        {
            lease1.Context.Set<CronTickerEntity>().Add(new CronTickerEntity
            {
                Id = Guid.NewGuid(),
                Function = "TestFunc",
                Expression = "* * * * *",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                Request = Array.Empty<byte>()
            });
            await lease1.Context.SaveChangesAsync();
        }

        // lease2 should see the data but have empty change tracker
        using var lease2 = await DbContextLease<LeaseTestDbContext>.CreateAsync(sp, CancellationToken.None);
        var count = await lease2.Context.Set<CronTickerEntity>().CountAsync();
        Assert.Equal(1, count);
        Assert.Empty(lease2.Context.ChangeTracker.Entries());
    }

    // =========================================================================
    // No factory and no scoped registration — throws
    // =========================================================================

    [Fact]
    public async Task CreateAsync_NoRegistration_Throws()
    {
        var services = new ServiceCollection();
        var sp = services.BuildServiceProvider();

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await DbContextLease<LeaseTestDbContext>.CreateAsync(sp, CancellationToken.None));
    }

    [Fact]
    public void Create_NoRegistration_Throws()
    {
        var services = new ServiceCollection();
        var sp = services.BuildServiceProvider();

        Assert.Throws<InvalidOperationException>(() =>
            DbContextLease<LeaseTestDbContext>.Create(sp));
    }
}
