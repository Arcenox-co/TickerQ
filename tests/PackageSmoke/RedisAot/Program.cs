using System.Text.Json.Serialization;
using TickerQ.Caching.StackExchangeRedis.DependencyInjection;
using TickerQ.Dashboard.DependencyInjection;
using TickerQ.DependencyInjection;
using TickerQ.Utilities;
using TickerQ.Utilities.Base;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Interfaces.Managers;
using TickerQ.Utilities.Licensing;

var connection = Environment.GetEnvironmentVariable("TICKERQ_AOT_REDIS_CONNECTION");
var licensePath = Environment.GetEnvironmentVariable("TICKERQ_AOT_LICENSE_PATH");
if (string.IsNullOrWhiteSpace(connection))
    throw new InvalidOperationException("TICKERQ_AOT_REDIS_CONNECTION must point to an isolated Redis server.");
if (string.IsNullOrWhiteSpace(licensePath) || !File.Exists(licensePath))
    throw new InvalidOperationException("TICKERQ_AOT_LICENSE_PATH must point to an external TickerQ certificate.");

var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
{
    Args = [], ContentRootPath = AppContext.BaseDirectory,
});
builder.WebHost.UseUrls("http://127.0.0.1:0");
builder.Services.AddSingleton<RedisProbe>();
builder.Services.AddTickerQ(options =>
{
    options.UseApplicationNamespace("installed-redis-aot-smoke");
    options.UseReconciliationEpoch(1);
    options.UseLicense(Path.GetFullPath(licensePath));
    options.WithJsonContext(RedisSmokeJsonContext.Default);
    options.ConfigureScheduler(scheduler =>
    {
        scheduler.MaxConcurrency = 2;
        scheduler.MinPollingInterval = TimeSpan.FromMilliseconds(10);
        scheduler.FallbackIntervalChecker = TimeSpan.FromMilliseconds(250);
        scheduler.StaleJobRecoveryEnabled = false;
    });
    options.AddStackExchangeRedis(redis =>
    {
        redis.Configuration = connection;
        redis.InstanceName = "tickerq:aot-smoke:";
        redis.JsonSerializerContext = RedisSmokeJsonContext.Default;
        redis.NodeHeartbeatInterval = TimeSpan.FromSeconds(1);
    });
    options.AddDashboard(dashboard => dashboard.AllowAnonymousDashboard().SetReadOnly());
});
builder.Services.MapTicker<RedisJob, RedisRequest>();
builder.Services.MapTicker<RedisChainRoot>();
builder.Services.MapTicker<RedisChainChild>();

await using var app = builder.Build();
app.UseTickerQ();
await app.StartAsync();
try
{
    var license = app.Services.GetRequiredService<TickerQLicenseStateProvider>();
    Check(license.ExecutionAllowed, $"External certificate did not allow Redis execution: {license.Current.Message}");

    var manager = app.Services.GetRequiredService<ITimeTickerManager<TimeTickerEntity>>();
    var store = app.Services.GetRequiredService<ITickerPersistenceProvider<TimeTickerEntity, CronTickerEntity>>();
    Check(store.GetType().Assembly.GetName().Name == "TickerQ.Caching.StackExchangeRedis",
        "The installed Redis package was not selected as the persistence provider.");

    var single = await manager.AddAsync<RedisJob, RedisRequest>(
        DateTime.UtcNow, new RedisRequest("native-redis", 23));
    Check(single.IsSucceeded, $"Scheduling the Redis job failed: {single.Exception}");
    await WaitForDoneAsync(store, single.Result.Id);
    Check(app.Services.GetRequiredService<RedisProbe>().Request is { Name: "native-redis", Count: 23 },
        "The native Redis execution did not bind the typed request.");

    var chain = await manager.AddChainAsync(definition => definition
        .WithDescription("Native Redis chain")
        .RunAt(DateTime.UtcNow)
        .StartWith<RedisChainRoot>()
        .Then<RedisChainChild>());
    Check(chain.IsSucceeded, $"Scheduling the Redis chain failed: {chain.Exception}");
    var childId = chain.Result.Children.Single().Id;
    await WaitForDoneAsync(store, chain.Result.Id);
    await WaitForDoneAsync(store, childId);
    Check(app.Services.GetRequiredService<RedisProbe>().Steps.ToArray() is ["root", "child"],
        "The native Redis chain did not execute in dependency order.");

    var address = app.Urls.Single(url => url.StartsWith("http://127.0.0.1:", StringComparison.Ordinal));
    using var client = new HttpClient { BaseAddress = new Uri(address) };
    using var detail = await client.GetAsync($"/tickerq/dashboard/api/dashboard/time-tickers/{single.Result.Id}");
    var detailBody = await detail.Content.ReadAsStringAsync();
    Check(detail.IsSuccessStatusCode && detailBody.Contains("\"status\":\"Done\"", StringComparison.Ordinal),
        $"Dashboard Redis detail failed ({(int)detail.StatusCode}): {detailBody}");
    using var pageRequest = new HttpRequestMessage(
        HttpMethod.Post, "/tickerq/dashboard/api/dashboard/time-tickers/query")
    {
        Content = new StringContent("{\"pageNumber\":1,\"pageSize\":20}",
            System.Text.Encoding.UTF8, "application/json"),
    };
    using var page = await client.SendAsync(pageRequest);
    var pageBody = await page.Content.ReadAsStringAsync();
    Check(page.IsSuccessStatusCode && pageBody.Contains(single.Result.Id.ToString(), StringComparison.OrdinalIgnoreCase),
        $"Dashboard Redis page failed ({(int)page.StatusCode}): {pageBody}");

    Console.WriteLine("PACKAGE_SMOKE_REDISAOT_OK");
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
            throw new InvalidOperationException($"Redis ticker {id} reached {row.Status}: {row.ExceptionMessage}");
        await Task.Delay(25, timeout.Token);
    }
}

static void Check(bool passed, string message)
{
    if (!passed) throw new InvalidOperationException(message);
}

public sealed class RedisProbe
{
    public RedisRequest? Request { get; set; }
    public Queue<string> Steps { get; } = new();
}

public sealed class RedisJob(RedisProbe probe) : ITickerFunction<RedisRequest>
{
    public Task ExecuteAsync(TickerFunctionContext<RedisRequest> context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        probe.Request = context.Request;
        return Task.CompletedTask;
    }
}

public sealed class RedisChainRoot(RedisProbe probe) : ITickerFunction
{
    public Task ExecuteAsync(TickerFunctionContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        probe.Steps.Enqueue("root");
        return Task.CompletedTask;
    }
}

public sealed class RedisChainChild(RedisProbe probe) : ITickerFunction
{
    public Task ExecuteAsync(TickerFunctionContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        probe.Steps.Enqueue("child");
        return Task.CompletedTask;
    }
}

public sealed record RedisRequest(string Name, int Count);

[JsonSerializable(typeof(RedisRequest))]
internal partial class RedisSmokeJsonContext : JsonSerializerContext;
