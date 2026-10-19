using TickerQ.Dashboard.DependencyInjection;
using TickerQ.DependencyInjection;
using TickerQ.Utilities.Entities;
using Microsoft.EntityFrameworkCore;
using TickerQ.EntityFrameworkCore.DbContextFactory;
using TickerQ.EntityFrameworkCore.DependencyInjection;

namespace TickerQ.Sample.LicenseSmoke;

internal static class DashboardPreview
{
    public static async Task<int> RunAsync(string licensePath, string example, int port)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = [], EnvironmentName = Environments.Development
        });
        // This anonymous preview must remain local to this computer.
        builder.WebHost.ConfigureKestrel(server => server.Listen(System.Net.IPAddress.Loopback, port));
        builder.Services.AddSingleton<ExecutionProbe>();
        builder.Services.AddTickerQ(options =>
        {
            options.UseLicense(licensePath);
            options.UseApplicationNamespace("license-smoke-dashboard");
            options.UseReconciliationEpoch(1);
            if (example == "database")
            {
                var databasePath = Path.Combine(Path.GetTempPath(), "tickerq-database-logs-preview.db");
                options.AddOperationalStore(store => store.UseTickerQDbContext<TickerQDbContext>(db =>
                    db.UseSqlite(new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder { DataSource = databasePath }.ToString())));
                options.ConfigureExecutionLogs(logs => logs.PersistToDatabase = true);
            }
            // The in-memory sample has no durable lease recovery.
            options.ConfigureScheduler(scheduler =>
            {
                scheduler.StaleJobRecoveryEnabled = false;
                scheduler.NodeIdentifier = $"local-{example}";
                if (example == "basic") return;
                scheduler.MaxConcurrency = example == "chains" ? 1 : example == "readonly" ? 2 : 4;
                scheduler.DefaultExecutionTimeout = TimeSpan.FromSeconds(example == "readonly" ? 30 : 120);
                scheduler.SchedulerTimeZone = example == "readonly"
                    ? TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin") : TimeZoneInfo.Utc;
                scheduler.MinPollingInterval = TimeSpan.FromMilliseconds(example == "readonly" ? 1000 : 250);
                scheduler.FallbackIntervalChecker = TimeSpan.FromSeconds(15);
                scheduler.IdleWorkerTimeOut = TimeSpan.FromSeconds(30);
            });
            if (example != "basic")
            {
                options.ConfigureJobRetention(retention =>
                {
                    var readOnly = example == "readonly";
                    retention.DeleteSucceededAfter = TimeSpan.FromDays(readOnly ? 7 : 30);
                    retention.DeleteFailedAfter = TimeSpan.FromDays(readOnly ? 30 : 90);
                    retention.DeleteCancelledAfter = readOnly ? TimeSpan.FromDays(7) : null;
                    retention.DeleteSkippedAfter = TimeSpan.FromDays(readOnly ? 1 : 7);
                    retention.SweepInterval = TimeSpan.FromMinutes(readOnly ? 15 : 60);
                    retention.BatchSize = readOnly ? 100 : 500;
                    retention.MaxBatchesPerSweep = readOnly ? 4 : 10;
                    retention.MaxNodesPerChain = readOnly ? 100 : 1000;
                });
            }
            options.AddDashboard(dashboard =>
            {
                dashboard.SetTitle($"TickerQ — {example} example");
                dashboard.AllowAnonymousDashboard();
                dashboard.SetReadOnly(example == "readonly");
            });
            if (example == "chains")
                options.UseTickerSeeder(async (TickerQ.Utilities.Interfaces.Managers.ICronTickerManager<CronTickerEntity> manager) =>
                {
                    var result = await manager.AddAsync(new CronTickerEntity
                    {
                        Function = "PreviewDailySummary", Expression = "*/20 * * * * *",
                        Description = "Recurring chain demo: prepare → process sample records → finish",
                        SkipIfAlreadyRunning = true,
                        Chain = new TickerQ.Utilities.Models.CronChainTemplate
                        {
                            Children = [new TickerQ.Utilities.Models.CronChainNode
                            {
                                Function = "PreviewSlowJob",
                                Children = [new TickerQ.Utilities.Models.CronChainNode { Function = "PreviewOnDemand" }]
                            }]
                        }
                    });
                    if (!result.IsSucceeded) throw new InvalidOperationException("Could not seed the recurring chain demo.", result.Exception);
                });
            options.UseTickerSeeder(async manager =>
            {
                foreach (var delay in new[] { 2, 10, 30 })
                {
                    var result = await manager.AddAsync(new TimeTickerEntity
                    {
                        Function = "LicenseSmoke",
                        Description = "Harmless local license test",
                        ExecutionTime = DateTime.UtcNow.AddSeconds(delay)
                    });
                    if (!result.IsSucceeded)
                        throw new InvalidOperationException("Could not schedule the local preview job.");
                }
                if (example != "basic")
                {
                    foreach (var function in new[] { "PreviewOnDemand", "PreviewSlowJob" })
                    {
                        var result = await manager.AddAsync(new TimeTickerEntity
                        {
                            Function = function,
                            Description = function == "PreviewSlowJob"
                                ? "Log demo: 20 in-memory records, a warning, and a recovered simulated error"
                                : "Local configuration example — no external side effects",
                            ExecutionTime = DateTime.UtcNow.AddSeconds(5)
                        });
                        if (!result.IsSucceeded)
                            throw new InvalidOperationException("Could not schedule a configuration example job.");
                    }
                }
            });
        });
        await using var app = builder.Build();
        if (example == "database")
        {
            await using var db = await app.Services.GetRequiredService<IDbContextFactory<TickerQDbContext>>().CreateDbContextAsync();
            // Only this isolated local demo creates its schema directly. Production uses migrations.
            await db.Database.EnsureCreatedAsync();
        }
        app.UseTickerQ();
        app.MapGet("/", () => Results.Redirect("/tickerq/dashboard/about"));
        Console.WriteLine($"{example} preview: http://127.0.0.1:{port}/tickerq/dashboard/about");
        Console.WriteLine(example == "database"
            ? "Local SQLite preview. Jobs and log tails persist in the temporary directory. Press Ctrl+C to stop."
            : "Local preview only. Jobs are in memory and reset on restart. Press Ctrl+C to stop.");
        await app.RunAsync();
        return 0;
    }
}
