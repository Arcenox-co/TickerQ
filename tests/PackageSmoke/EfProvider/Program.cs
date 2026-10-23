using Microsoft.EntityFrameworkCore;
using TickerQ.Dashboard.DependencyInjection;
using TickerQ.DependencyInjection;
using TickerQ.EntityFrameworkCore.DbContextFactory;
using TickerQ.EntityFrameworkCore.DependencyInjection;
using TickerQ.Utilities;
using TickerQ.Utilities.Base;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Interfaces.Managers;
using TickerQ.Utilities.Licensing;

var licensePath = Environment.GetEnvironmentVariable("TICKERQ_AOT_LICENSE_PATH");
if (string.IsNullOrWhiteSpace(licensePath) || !File.Exists(licensePath))
    throw new InvalidOperationException("TICKERQ_AOT_LICENSE_PATH must point to an external TickerQ certificate.");
var databasePath = Environment.GetEnvironmentVariable("TICKERQ_EF_SMOKE_DATABASE");
if (string.IsNullOrWhiteSpace(databasePath))
    throw new InvalidOperationException("TICKERQ_EF_SMOKE_DATABASE must point to an isolated SQLite database.");
var connectionString = $"Data Source={Path.GetFullPath(databasePath)};Pooling=False";

var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
{
    Args = [], ContentRootPath = AppContext.BaseDirectory,
});
builder.WebHost.UseUrls("http://127.0.0.1:0");
builder.Services.AddSingleton<EfProbe>();
builder.Services.AddTickerQ(options =>
{
    options.UseApplicationNamespace("installed-ef-provider-smoke");
    options.UseReconciliationEpoch(1);
    options.UseLicense(Path.GetFullPath(licensePath));
    options.ConfigureScheduler(scheduler =>
    {
        scheduler.MaxConcurrency = 1;
        scheduler.MinPollingInterval = TimeSpan.FromMilliseconds(10);
        scheduler.FallbackIntervalChecker = TimeSpan.FromMilliseconds(250);
        scheduler.StaleJobRecoveryEnabled = false;
    });
    options.AddOperationalStore(ef =>
        ef.UseTickerQDbContext<TickerQDbContext>(db => db.UseSqlite(connectionString)));
    options.AddDashboard(dashboard => dashboard.AllowAnonymousDashboard().SetReadOnly());
});
builder.Services.MapTicker<EfJob>();

await using var app = builder.Build();
var factory = app.Services.GetRequiredService<IDbContextFactory<TickerQDbContext>>();
await using (var schema = await factory.CreateDbContextAsync())
{
    await schema.Database.EnsureDeletedAsync();
    await schema.Database.EnsureCreatedAsync();
}

app.UseTickerQ();
await app.StartAsync();
try
{
    var license = app.Services.GetRequiredService<TickerQLicenseStateProvider>();
    Check(license.ExecutionAllowed, $"External certificate did not allow EF execution: {license.Current.Message}");
    var store = app.Services.GetRequiredService<ITickerPersistenceProvider<TimeTickerEntity, CronTickerEntity>>();
    Check(store.GetType().Assembly.GetName().Name == "TickerQ.EntityFrameworkCore",
        "The installed EF package was not selected as the persistence provider.");

    var manager = app.Services.GetRequiredService<ITimeTickerManager<TimeTickerEntity>>();
    var scheduled = await manager.AddAsync<EfJob>(DateTime.UtcNow);
    Check(scheduled.IsSucceeded, $"Scheduling the EF job failed: {scheduled.Exception}");
    await WaitForDoneAsync(store, scheduled.Result.Id);
    Check(app.Services.GetRequiredService<EfProbe>().Calls == 1,
        "The EF execution did not invoke the job exactly once.");

    var address = app.Urls.Single(url => url.StartsWith("http://127.0.0.1:", StringComparison.Ordinal));
    using var client = new HttpClient { BaseAddress = new Uri(address) };
    using var detail = await client.GetAsync($"/tickerq/dashboard/api/dashboard/time-tickers/{scheduled.Result.Id}");
    var body = await detail.Content.ReadAsStringAsync();
    Check(detail.IsSuccessStatusCode && body.Contains("\"status\":\"Done\"", StringComparison.Ordinal),
        $"Dashboard EF detail failed ({(int)detail.StatusCode}): {body}");

    Console.WriteLine("PACKAGE_SMOKE_EFPROVIDER_OK");
}
finally
{
    await app.StopAsync();
}

static async Task WaitForDoneAsync(
    ITickerPersistenceProvider<TimeTickerEntity, CronTickerEntity> store, Guid id)
{
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
    while (true)
    {
        timeout.Token.ThrowIfCancellationRequested();
        var row = await store.GetTimeTickerById(id, timeout.Token);
        if (row?.Status == TickerStatus.Done) return;
        if (row?.Status is TickerStatus.Failed or TickerStatus.Cancelled or TickerStatus.Skipped)
            throw new InvalidOperationException($"EF ticker {id} reached {row.Status}: {row.ExceptionMessage}");
        await Task.Delay(25, timeout.Token);
    }
}

static void Check(bool passed, string message)
{
    if (!passed) throw new InvalidOperationException(message);
}

public sealed class EfProbe { public int Calls; }

public sealed class EfJob(EfProbe probe) : ITickerFunction
{
    public Task ExecuteAsync(TickerFunctionContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Interlocked.Increment(ref probe.Calls);
        return Task.CompletedTask;
    }
}

