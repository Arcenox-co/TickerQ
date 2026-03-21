using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using TickerQ.EntityFrameworkCore.Configurations;
using TickerQ.EntityFrameworkCore.DbContextFactory;
using TickerQ.Utilities.Entities;

namespace TickerQ.Benchmarks;

/// <summary>
/// Benchmarks for DbContextLease — comparing factory vs scoped resolution paths.
/// Demonstrates the performance of TickerQ's lightweight context leasing vs raw DI resolution.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.HostProcess)]
public class DbContextLeaseBenchmarks
{
    private SqliteConnection _connection = null!;
    private ServiceProvider _factorySp = null!;
    private ServiceProvider _scopedSp = null!;
    private ServiceProvider _pooledSp = null!;

    [GlobalSetup]
    public void Setup()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<BenchmarkDbContext>()
            .UseSqlite(_connection)
            .Options;

        // Factory path
        var factoryServices = new ServiceCollection();
        factoryServices.AddSingleton<IDbContextFactory<BenchmarkDbContext>>(
            new PooledDbContextFactory<BenchmarkDbContext>(options));
        _factorySp = factoryServices.BuildServiceProvider();

        // Scoped path
        var scopedServices = new ServiceCollection();
        scopedServices.AddDbContext<BenchmarkDbContext>(opt => opt.UseSqlite(_connection));
        _scopedSp = scopedServices.BuildServiceProvider();

        // Pooled factory path
        var pooledServices = new ServiceCollection();
        pooledServices.AddPooledDbContextFactory<BenchmarkDbContext>(opt => opt.UseSqlite(_connection));
        _pooledSp = pooledServices.BuildServiceProvider();

        using var ctx = new BenchmarkDbContext(options);
        ctx.Database.EnsureCreated();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _factorySp.Dispose();
        _scopedSp.Dispose();
        _pooledSp.Dispose();
        _connection.Dispose();
    }

    [Benchmark(Description = "DbContextLease: Factory path (async)")]
    public async Task<int> Lease_Factory_Async()
    {
        using var lease = await DbContextLease<BenchmarkDbContext>.CreateAsync(_factorySp, CancellationToken.None);
        return lease.Context.GetHashCode();
    }

    [Benchmark(Description = "DbContextLease: Factory path (sync)")]
    public int Lease_Factory_Sync()
    {
        using var lease = DbContextLease<BenchmarkDbContext>.Create(_factorySp);
        return lease.Context.GetHashCode();
    }

    [Benchmark(Description = "DbContextLease: Scoped path (async)")]
    public async Task<int> Lease_Scoped_Async()
    {
        using var lease = await DbContextLease<BenchmarkDbContext>.CreateAsync(_scopedSp, CancellationToken.None);
        return lease.Context.GetHashCode();
    }

    [Benchmark(Description = "DbContextLease: Scoped path (sync)")]
    public int Lease_Scoped_Sync()
    {
        using var lease = DbContextLease<BenchmarkDbContext>.Create(_scopedSp);
        return lease.Context.GetHashCode();
    }

    [Benchmark(Description = "DbContextLease: Pooled factory (async)")]
    public async Task<int> Lease_Pooled_Async()
    {
        using var lease = await DbContextLease<BenchmarkDbContext>.CreateAsync(_pooledSp, CancellationToken.None);
        return lease.Context.GetHashCode();
    }

    [Benchmark(Baseline = true, Description = "Raw: IDbContextFactory.CreateDbContext")]
    public int Raw_Factory_Create()
    {
        var factory = _factorySp.GetRequiredService<IDbContextFactory<BenchmarkDbContext>>();
        using var ctx = factory.CreateDbContext();
        return ctx.GetHashCode();
    }

    [Benchmark(Description = "Raw: ServiceScope + resolve DbContext")]
    public int Raw_Scoped_Create()
    {
        using var scope = _scopedSp.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<BenchmarkDbContext>();
        return ctx.GetHashCode();
    }
}

public class BenchmarkDbContext : DbContext
{
    public BenchmarkDbContext(DbContextOptions<BenchmarkDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new TimeTickerConfigurations<TimeTickerEntity>("ticker"));
        modelBuilder.ApplyConfiguration(new CronTickerConfigurations<CronTickerEntity>("ticker"));
        base.OnModelCreating(modelBuilder);
    }
}
