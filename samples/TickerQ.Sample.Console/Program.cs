using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TickerQ.DependencyInjection;
using TickerQ.EntityFrameworkCore.DbContextFactory;
using TickerQ.EntityFrameworkCore.DependencyInjection;
using TickerQ.Utilities;
using TickerQ.Utilities.Base;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Interfaces.Managers;

var host = Host.CreateDefaultBuilder(args)
    .ConfigureServices((context, services) =>
    {
        // Configure TickerQ with SQLite operational store (file-based)
        services.AddTickerQ(options =>
        {
            options.EnablePeriodic();
            options.AddOperationalStore(efOptions =>
            {
                efOptions.EnablePeriodic();
                efOptions.UseTickerQDbContext<SampleConsoleDbContext>(dbOptions =>
                {
                    dbOptions.UseSqlite(
                        "Data Source=tickerq-console.db",
                        b => b.MigrationsAssembly("TickerQ.Sample.Console"));
                });
            });
        });

        services.AddHostedService<SampleScheduler>();
    })
    .Build();

// Ensure TickerQ operational store schema is applied.
// For this manual-verification sample we recreate the database on every run
// so the periodic-ticker tables are always present without managing migrations.
using (var scope = host.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<SampleConsoleDbContext>();
    db.Database.EnsureDeleted();
    db.Database.EnsureCreated();
}

// Build function metadata so TickerFunctionProvider.TickerFunctions is initialized
TickerFunctionProvider.Build();

// Activate TickerQ — this signals the initializer hosted service to seed
// cron/periodic tickers when the host starts.
host.UseTickerQ();

await host.RunAsync();

// Simple sample job
public class ConsoleSampleJobs
{
    private static long _heartbeatCount;

    [TickerFunction("ConsoleSample_HelloWorld")]
    public Task HelloWorldAsync(TickerFunctionContext context, CancellationToken cancellationToken)
    {
        Console.WriteLine($"[Console] Hello from TickerQ! Id={context.Id}, ScheduledFor={context.ScheduledFor:O}");
        return Task.CompletedTask;
    }

    /// <summary>
    /// Periodic ticker auto-seeded by TickerQ on first startup.
    /// Fires every 5 seconds; subsequent app runs will reuse the existing row.
    /// </summary>
    [TickerFunction("ConsoleSample_Heartbeat", PeriodicInterval = "00:00:05")]
    public Task HeartbeatAsync(TickerFunctionContext context, CancellationToken cancellationToken)
    {
        var n = Interlocked.Increment(ref _heartbeatCount);
        Console.WriteLine($"[Console] Heartbeat #{n} at {DateTime.UtcNow:HH:mm:ss.fff} (Id={context.Id})");
        return Task.CompletedTask;
    }
}

// Hosted service that schedules a single job on startup
public class SampleScheduler : IHostedService
{
    private readonly ITimeTickerManager<TimeTickerEntity> _timeTickerManager;

    public SampleScheduler(ITimeTickerManager<TimeTickerEntity> timeTickerManager)
    {
        _timeTickerManager = timeTickerManager;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var result = await _timeTickerManager.AddAsync(new TimeTickerEntity
        {
            Function = "ConsoleSample_HelloWorld",
            ExecutionTime = DateTime.UtcNow.AddSeconds(5)
        }, cancellationToken);

        if (!result.IsSucceeded)
        {
            Console.WriteLine($"Failed to schedule console sample job. Exception: {result.Exception}");
            return;
        }

        Console.WriteLine($"Scheduled console sample job with Id={result.Result.Id}, ScheduledFor={result.Result.ExecutionTime:O}");
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

/// <summary>
/// Sample DbContext using the three-parameter <see cref="TickerQDbContext{TTimeTicker, TCronTicker, TPeriodicTicker}"/>
/// base so that PeriodicTicker tables are mapped via OnModelCreating (visible to EnsureCreated/migrations).
/// </summary>
public class SampleConsoleDbContext : TickerQDbContext<TimeTickerEntity, CronTickerEntity, PeriodicTickerEntity>
{
    public SampleConsoleDbContext(DbContextOptions<SampleConsoleDbContext> options) : base(options) { }
}

