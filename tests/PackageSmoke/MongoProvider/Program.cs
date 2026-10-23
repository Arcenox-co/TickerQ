using System.Text.Json.Serialization;
using MongoDB.Driver;
using TickerQ.Dashboard.DependencyInjection;
using TickerQ.DependencyInjection;
using TickerQ.MongoDB.DependencyInjection;
using TickerQ.Utilities;
using TickerQ.Utilities.Base;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Interfaces.Managers;
using TickerQ.Utilities.Licensing;

var connection = Environment.GetEnvironmentVariable("TICKERQ_MONGO_SMOKE_CONNECTION");
var databaseName = Environment.GetEnvironmentVariable("TICKERQ_MONGO_SMOKE_DATABASE");
var licensePath = Environment.GetEnvironmentVariable("TICKERQ_AOT_LICENSE_PATH");
if (string.IsNullOrWhiteSpace(connection))
    throw new InvalidOperationException("TICKERQ_MONGO_SMOKE_CONNECTION must point to an isolated MongoDB replica set.");
if (string.IsNullOrWhiteSpace(databaseName))
    throw new InvalidOperationException("TICKERQ_MONGO_SMOKE_DATABASE must name an isolated MongoDB database.");
if (string.IsNullOrWhiteSpace(licensePath) || !File.Exists(licensePath))
    throw new InvalidOperationException("TICKERQ_AOT_LICENSE_PATH must point to an external TickerQ certificate.");

var mongoClient = new MongoClient(connection);
await mongoClient.DropDatabaseAsync(databaseName);

var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
{
    Args = [], ContentRootPath = AppContext.BaseDirectory,
});
builder.WebHost.UseUrls("http://127.0.0.1:0");
builder.Services.AddSingleton<MongoProbe>();
builder.Services.AddTickerQ(options =>
{
    options.UseApplicationNamespace("installed-mongo-provider-smoke");
    options.UseReconciliationEpoch(1);
    options.UseLicense(Path.GetFullPath(licensePath));
    options.WithJsonContext(MongoSmokeJsonContext.Default);
    options.ConfigureScheduler(scheduler =>
    {
        scheduler.MaxConcurrency = 2;
        scheduler.MinPollingInterval = TimeSpan.FromMilliseconds(10);
        scheduler.FallbackIntervalChecker = TimeSpan.FromMilliseconds(250);
        scheduler.StaleJobRecoveryEnabled = false;
    });
    options.AddOperationalStore(mongo =>
        mongo.UseTickerQMongoClient(connection, databaseName));
    options.AddDashboard(dashboard => dashboard.AllowAnonymousDashboard().SetReadOnly());
});
builder.Services.MapTicker<MongoJob, MongoRequest>();
builder.Services.MapTicker<MongoChainRoot>();
builder.Services.MapTicker<MongoChainChild>();

await using var app = builder.Build();
app.UseTickerQ();
await app.StartAsync();
try
{
    var license = app.Services.GetRequiredService<TickerQLicenseStateProvider>();
    Check(license.ExecutionAllowed, $"External certificate did not allow MongoDB execution: {license.Current.Message}");

    var manager = app.Services.GetRequiredService<ITimeTickerManager<TimeTickerEntity>>();
    var store = app.Services.GetRequiredService<ITickerPersistenceProvider<TimeTickerEntity, CronTickerEntity>>();
    Check(store.GetType().Assembly.GetName().Name == "TickerQ.MongoDB",
        "The installed MongoDB package was not selected as the persistence provider.");

    var single = await manager.AddAsync<MongoJob, MongoRequest>(
        DateTime.UtcNow, new MongoRequest("native-mongo", 29));
    Check(single.IsSucceeded, $"Scheduling the MongoDB job failed: {single.Exception}");
    await WaitForDoneAsync(store, single.Result.Id);
    Check(app.Services.GetRequiredService<MongoProbe>().Request is { Name: "native-mongo", Count: 29 },
        "The MongoDB execution did not bind the typed request.");

    var chain = await manager.AddChainAsync(definition => definition
        .WithDescription("Native MongoDB chain")
        .RunAt(DateTime.UtcNow)
        .StartWith<MongoChainRoot>()
        .Then<MongoChainChild>());
    Check(chain.IsSucceeded, $"Scheduling the MongoDB chain failed: {chain.Exception}");
    var childId = chain.Result.Children.Single().Id;
    await WaitForDoneAsync(store, chain.Result.Id);
    await WaitForDoneAsync(store, childId);
    Check(app.Services.GetRequiredService<MongoProbe>().Steps.ToArray() is ["root", "child"],
        "The MongoDB chain did not execute in dependency order.");

    var address = app.Urls.Single(url => url.StartsWith("http://127.0.0.1:", StringComparison.Ordinal));
    using var client = new HttpClient { BaseAddress = new Uri(address) };
    using var detail = await client.GetAsync($"/tickerq/dashboard/api/dashboard/time-tickers/{single.Result.Id}");
    var detailBody = await detail.Content.ReadAsStringAsync();
    Check(detail.IsSuccessStatusCode && detailBody.Contains("\"status\":\"Done\"", StringComparison.Ordinal),
        $"Dashboard MongoDB detail failed ({(int)detail.StatusCode}): {detailBody}");
    using var pageRequest = new HttpRequestMessage(
        HttpMethod.Post, "/tickerq/dashboard/api/dashboard/time-tickers/query")
    {
        Content = new StringContent("{\"pageNumber\":1,\"pageSize\":20}",
            System.Text.Encoding.UTF8, "application/json"),
    };
    using var page = await client.SendAsync(pageRequest);
    var pageBody = await page.Content.ReadAsStringAsync();
    Check(page.IsSuccessStatusCode && pageBody.Contains(single.Result.Id.ToString(), StringComparison.OrdinalIgnoreCase),
        $"Dashboard MongoDB page failed ({(int)page.StatusCode}): {pageBody}");

    Console.WriteLine("PACKAGE_SMOKE_MONGOPROVIDER_OK");
}
finally
{
    await app.StopAsync();
}

static async Task WaitForDoneAsync(
    ITickerPersistenceProvider<TimeTickerEntity, CronTickerEntity> store, Guid id)
{
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
    while (true)
    {
        timeout.Token.ThrowIfCancellationRequested();
        var row = await store.GetTimeTickerById(id, timeout.Token);
        if (row?.Status == TickerStatus.Done) return;
        if (row?.Status is TickerStatus.Failed or TickerStatus.Cancelled or TickerStatus.Skipped)
            throw new InvalidOperationException($"MongoDB ticker {id} reached {row.Status}: {row.ExceptionMessage}");
        await Task.Delay(25, timeout.Token);
    }
}

static void Check(bool passed, string message)
{
    if (!passed) throw new InvalidOperationException(message);
}

public sealed class MongoProbe
{
    public MongoRequest? Request { get; set; }
    public Queue<string> Steps { get; } = new();
}

public sealed class MongoJob(MongoProbe probe) : ITickerFunction<MongoRequest>
{
    public Task ExecuteAsync(TickerFunctionContext<MongoRequest> context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        probe.Request = context.Request;
        return Task.CompletedTask;
    }
}

public sealed class MongoChainRoot(MongoProbe probe) : ITickerFunction
{
    public Task ExecuteAsync(TickerFunctionContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        probe.Steps.Enqueue("root");
        return Task.CompletedTask;
    }
}

public sealed class MongoChainChild(MongoProbe probe) : ITickerFunction
{
    public Task ExecuteAsync(TickerFunctionContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        probe.Steps.Enqueue("child");
        return Task.CompletedTask;
    }
}

public sealed record MongoRequest(string Name, int Count);

[JsonSerializable(typeof(MongoRequest))]
internal partial class MongoSmokeJsonContext : JsonSerializerContext;
