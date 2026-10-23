using System.Collections.Concurrent;
using System.Text.Json.Serialization;
using TickerQ.Dashboard.DependencyInjection;
using TickerQ.DependencyInjection;
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

var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
{
    Args = [],
    ContentRootPath = AppContext.BaseDirectory,
});
builder.WebHost.UseUrls("http://127.0.0.1:0");
builder.Services.AddSingleton<SchedulerProbe>();
builder.Services.AddTickerQ(options =>
{
    options.UseApplicationNamespace("installed-scheduler-aot-smoke");
    options.UseReconciliationEpoch(1);
    options.UseLicense(Path.GetFullPath(licensePath));
    options.WithJsonContext(SchedulerSmokeJsonContext.Default);
    options.ConfigureScheduler(scheduler =>
    {
        scheduler.MaxConcurrency = 2;
        scheduler.MinPollingInterval = TimeSpan.FromMilliseconds(10);
        scheduler.FallbackIntervalChecker = TimeSpan.FromMilliseconds(250);
        scheduler.StaleJobRecoveryEnabled = false;
    });
    options.AddDashboard(dashboard => dashboard.AllowAnonymousDashboard().SetReadOnly());
});
builder.Services.MapTicker<CompleteJob, SmokeRequest>();
builder.Services.MapTicker<ChainRootJob>();
builder.Services.MapTicker<ChainChildJob>();
builder.Services.MapTicker<CancellableJob>();
builder.Services.MapTicker<CronJob>();

await using var app = builder.Build();
app.UseTickerQ();
await app.StartAsync();
try
{
    var license = app.Services.GetRequiredService<TickerQLicenseStateProvider>();
    Check(license.ExecutionAllowed, $"External certificate did not allow execution: {license.Current.Message}");
    Check(app.Services.GetRequiredService<ITickerQHostScheduler>().IsRunning,
        "Licensed scheduler did not start.");

    var manager = app.Services.GetRequiredService<ITimeTickerManager<TimeTickerEntity>>();
    var cronManager = app.Services.GetRequiredService<ICronTickerManager<CronTickerEntity>>();
    var store = app.Services.GetRequiredService<ITickerPersistenceProvider<TimeTickerEntity, CronTickerEntity>>();
    var probe = app.Services.GetRequiredService<SchedulerProbe>();

    var single = await manager.AddAsync<CompleteJob, SmokeRequest>(
        DateTime.UtcNow,
        new SmokeRequest("native-e2e", 17));
    Check(single.IsSucceeded, $"Scheduling the typed job failed: {single.Exception}");
    var completed = await WaitForStatusAsync(store, single.Result.Id, TickerStatus.Done);
    Check(probe.TypedRequests.TryGetValue(single.Result.Id, out var request) &&
          request is { Name: "native-e2e", Count: 17 },
        "The licensed native execution did not bind the typed request.");
    Check(completed.ExecutedAt.HasValue && completed.ExceptionMessage is null,
        "The completed native execution did not persist a clean terminal state.");

    var chain = await manager.AddChainAsync(definition => definition
        .WithDescription("Native AOT chain")
        .RunAt(DateTime.UtcNow)
        .StartWith<ChainRootJob>()
        .Then<ChainChildJob>());
    Check(chain.IsSucceeded, $"Scheduling the chain failed: {chain.Exception}");
    var childId = chain.Result.Children.Single().Id;
    await WaitForStatusAsync(store, chain.Result.Id, TickerStatus.Done);
    await WaitForStatusAsync(store, childId, TickerStatus.Done);
    Check(probe.ChainSteps.ToArray() is ["root", "child"],
        "The licensed native chain did not execute in parent/child order.");

    var cancellable = await manager.AddAsync<CancellableJob>(DateTime.UtcNow);
    Check(cancellable.IsSucceeded, $"Scheduling the cancellable job failed: {cancellable.Exception}");
    await probe.CancellableStarted.Task.WaitAsync(TimeSpan.FromSeconds(15));
    var cancellation = await manager.RequestCancellationAsync(cancellable.Result.Id);
    Check(cancellation.IsSucceeded && cancellation.AffectedRows == 1,
        $"Cooperative cancellation was not accepted: {cancellation.Exception}");
    var cancelled = await WaitForStatusAsync(store, cancellable.Result.Id, TickerStatus.Cancelled);
    Check(probe.CancellationObserved && cancelled.ExecutedAt.HasValue,
        "The licensed native execution did not persist cooperative cancellation.");

    var cron = await cronManager.AddAsync<CronJob>("0 0 0 1 1 *");
    Check(cron.IsSucceeded, $"Scheduling the cron definition failed: {cron.Exception}");
    var occurrence = await cronManager.RunNowAsync(cron.Result.Id);
    Check(occurrence.IsSucceeded && occurrence.AffectedRows == 1,
        $"Running the cron definition on demand failed: {occurrence.Exception}");
    var completedOccurrence = await WaitForCronStatusAsync(
        store, occurrence.Result.Id, TickerStatus.Done);
    Check(probe.CronExecutions == 1 && completedOccurrence.ExecutedAt.HasValue,
        "The licensed native cron occurrence did not execute and persist completion.");
    var disabled = await cronManager.SetEnabledAsync(cron.Result.Id, false);
    Check(disabled.IsSucceeded && disabled.AffectedRows == 1,
        $"Disabling the cron definition failed: {disabled.Exception}");

    var address = app.Urls.Single(url => url.StartsWith("http://127.0.0.1:", StringComparison.Ordinal));
    using var client = new HttpClient { BaseAddress = new Uri(address) };
    using var response = await client.GetAsync($"/tickerq/dashboard/api/dashboard/time-tickers/{single.Result.Id}");
    var body = await response.Content.ReadAsStringAsync();
    Check(response.IsSuccessStatusCode && body.Contains("\"status\":\"Done\"", StringComparison.Ordinal),
        $"Dashboard did not expose the completed native execution ({(int)response.StatusCode}): {body}");
    using var cronResponse = await client.GetAsync($"/tickerq/dashboard/api/dashboard/cron-tickers/{cron.Result.Id}");
    var cronBody = await cronResponse.Content.ReadAsStringAsync();
    Check(cronResponse.IsSuccessStatusCode && cronBody.Contains("\"id\":", StringComparison.Ordinal),
        $"Dashboard did not expose the native cron definition ({(int)cronResponse.StatusCode}): {cronBody}");
    using var occurrenceResponse = await client.GetAsync(
        $"/tickerq/dashboard/api/dashboard/cron-occurrences/{occurrence.Result.Id}");
    var occurrenceBody = await occurrenceResponse.Content.ReadAsStringAsync();
    Check(occurrenceResponse.IsSuccessStatusCode &&
          occurrenceBody.Contains("\"status\":\"Done\"", StringComparison.Ordinal),
        $"Dashboard did not expose the native cron occurrence ({(int)occurrenceResponse.StatusCode}): {occurrenceBody}");

    Console.WriteLine("PACKAGE_SMOKE_SCHEDULERAOT_OK");
}
finally
{
    await app.StopAsync();
}

static async Task<TimeTickerEntity> WaitForStatusAsync(
    ITickerPersistenceProvider<TimeTickerEntity, CronTickerEntity> store,
    Guid id,
    TickerStatus expected)
{
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
    while (true)
    {
        timeout.Token.ThrowIfCancellationRequested();
        // GetTimeTickers deliberately returns roots with nested children. Use the
        // point lookup so this probe can observe both roots and individual chain steps.
        var row = await store.GetTimeTickerById(id, timeout.Token);
        if (row?.Status == expected)
            return row;
        if (row?.Status is TickerStatus.Failed or TickerStatus.Skipped ||
            row?.Status == TickerStatus.Cancelled && expected != TickerStatus.Cancelled)
            throw new InvalidOperationException(
                $"Ticker {id} reached {row.Status} while waiting for {expected}: {row.ExceptionMessage ?? row.SkippedReason}");
        await Task.Delay(20, timeout.Token);
    }
}

static async Task<CronTickerOccurrenceEntity<CronTickerEntity>> WaitForCronStatusAsync(
    ITickerPersistenceProvider<TimeTickerEntity, CronTickerEntity> store,
    Guid id,
    TickerStatus expected)
{
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
    while (true)
    {
        timeout.Token.ThrowIfCancellationRequested();
        var row = (await store.GetAllCronTickerOccurrences(
            occurrence => occurrence.Id == id, timeout.Token)).SingleOrDefault();
        if (row?.Status == expected)
            return row;
        if (row?.Status is TickerStatus.Failed or TickerStatus.Skipped or TickerStatus.Cancelled)
            throw new InvalidOperationException(
                $"Cron occurrence {id} reached {row.Status} while waiting for {expected}: " +
                $"{row.ExceptionMessage ?? row.SkippedReason}");
        await Task.Delay(20, timeout.Token);
    }
}

static void Check(bool passed, string message)
{
    if (!passed) throw new InvalidOperationException(message);
}

public sealed class SchedulerProbe
{
    public ConcurrentDictionary<Guid, SmokeRequest> TypedRequests { get; } = new();
    public ConcurrentQueue<string> ChainSteps { get; } = new();
    public TaskCompletionSource CancellableStarted { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    public bool CancellationObserved { get; set; }
    public int CronExecutions;
}

public sealed class CompleteJob(SchedulerProbe probe) : ITickerFunction<SmokeRequest>
{
    public Task ExecuteAsync(TickerFunctionContext<SmokeRequest> context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        probe.TypedRequests[context.Id] = context.Request;
        return Task.CompletedTask;
    }
}

public sealed class ChainRootJob(SchedulerProbe probe) : ITickerFunction
{
    public Task ExecuteAsync(TickerFunctionContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        probe.ChainSteps.Enqueue("root");
        return Task.CompletedTask;
    }
}

public sealed class ChainChildJob(SchedulerProbe probe) : ITickerFunction
{
    public Task ExecuteAsync(TickerFunctionContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        probe.ChainSteps.Enqueue("child");
        return Task.CompletedTask;
    }
}

public sealed class CancellableJob(SchedulerProbe probe) : ITickerFunction
{
    public async Task ExecuteAsync(TickerFunctionContext context, CancellationToken cancellationToken)
    {
        probe.CancellableStarted.TrySetResult();
        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            probe.CancellationObserved = true;
            throw;
        }
    }
}

public sealed class CronJob(SchedulerProbe probe) : ITickerFunction
{
    public Task ExecuteAsync(TickerFunctionContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Interlocked.Increment(ref probe.CronExecutions);
        return Task.CompletedTask;
    }
}

public sealed record SmokeRequest(string Name, int Count);

[JsonSerializable(typeof(SmokeRequest))]
internal partial class SchedulerSmokeJsonContext : JsonSerializerContext;
