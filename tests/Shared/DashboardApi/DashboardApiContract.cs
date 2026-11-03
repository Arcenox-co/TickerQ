using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using TickerQ.DependencyInjection;
using TickerQ.Utilities;

namespace TickerQ.Tests.Shared.DashboardApi;

/// <summary>
/// The Dashboard's HTTP API against a running node on a real database. Every request goes through
/// routing, the license filter, the handlers, the managers, and the provider under test, and the
/// scheduler really executes what the API schedules. Each test gets its own empty store, and between
/// them the tests call every route the Dashboard maps without an assistant configured.
/// </summary>
public abstract class DashboardApiContract(IDashboardApiServer server)
{
    private const string Api = DashboardApiHost.Api;
    private const string D = Api + "/dashboard";
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(60);

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    [DashboardApiFact]
    public async Task Reads_DescribeTheNode_ItsFunctionsAndAnEmptyStore()
    {
        var function = Unique("meta");
        await using var node = await StartAsync(s => s.MapTicker(function, (_, _) => Task.CompletedTask));

        var auth = await node.GetAsync($"{Api}/auth/info");
        Assert.True(auth.GetProperty("authenticated").GetBoolean());
        Assert.Equal("none", auth.GetProperty("mode").GetString());
        Assert.True((await node.SendAsync(HttpMethod.Post, $"{Api}/auth/validate", null, HttpStatusCode.OK))
            .GetProperty("authenticated").GetBoolean());

        var options = await node.GetAsync($"{Api}/options");
        Assert.True(options.GetProperty("maxConcurrency").GetInt32() > 0);
        Assert.True(options.GetProperty("executionLogs").GetProperty("persistToDatabase").GetBoolean());
        var license = await node.GetAsync($"{Api}/license");
        Assert.Equal("Active", license.GetProperty("status").GetString());
        Assert.True(license.GetProperty("executionAllowed").GetBoolean());
        Assert.Equal(JsonValueKind.Object, (await node.GetAsync($"{D}/diagnostics")).ValueKind);

        Assert.True((await node.GetAsync($"{D}/host/status")).GetProperty("isRunning").GetBoolean());
        Assert.Equal(JsonValueKind.Null, (await node.GetAsync($"{D}/host/next-ticker")).GetProperty("id").ValueKind);

        Assert.Contains(function, Names(await node.GetAsync($"{D}/functions"), "functionName"));
        Assert.Contains(function, Names(await node.GetAsync($"{D}/nodes/any/functions"), "functionName"));
        // The first heartbeat is written as the node starts; the next poll reports its running scheduler.
        await WaitUntilAsync(async () => (await node.GetAsync($"{D}/nodes")).EnumerateArray()
            .Any(x => x.GetProperty("isRunning").GetBoolean()), "the node to report itself running");
        Assert.True((await node.GetAsync($"{D}/nodes"))[0].GetProperty("functionCount").GetInt32() >= 1);

        var statuses = await node.GetAsync($"{D}/stats/overall-statuses");
        Assert.Equal(8, statuses.GetArrayLength());
        Assert.All(statuses.EnumerateArray(), x => Assert.Equal(0, x.GetProperty("count").GetInt32()));
        Assert.Equal(0, (await node.GetAsync($"{D}/stats/node-jobs")).GetArrayLength());

        var timeGraph = await node.GetAsync($"{D}/graphs/time-tickers?pastDays=3&futureDays=1");
        Assert.Equal(5, timeGraph.GetArrayLength());
        Assert.All(timeGraph.EnumerateArray(), day => Assert.Equal(8, day.GetProperty("counts").GetArrayLength()));
        Assert.Equal(8, (await node.GetAsync($"{D}/graphs/cron-tickers")).GetArrayLength());
        Assert.Equal(0, (await node.GetAsync($"{D}/overview/upcoming")).GetArrayLength());
        Assert.Equal(0, (await node.GetAsync($"{D}/overview/recent-activity")).GetArrayLength());
    }

    [DashboardApiFact]
    public async Task JobAddedWithAPayload_RunsWithIt_AndEveryReadReportsTheOutcome()
    {
        var function = Unique("order");
        await using var node = await StartAsync(s => s.MapTicker<Order>(function, (context, _) =>
        {
            context.SetResult(new Receipt(context.Request.Name, context.Request.Quantity * 2));
            return Task.CompletedTask;
        }));
        var order = new Order("widget", 21);

        var id = await node.AddTimeTickerAsync(new
        {
            function, executionTime = DateTime.UtcNow, description = "api order", retries = 2,
            retryIntervalsSeconds = new[] { 1, 2 }, request = Request(order), timeoutSeconds = 30,
        });
        await node.WaitForStatusAsync(id, Succeeded);

        var ticker = await node.GetAsync($"{D}/time-tickers/{id}");
        Assert.Equal(function, ticker.GetProperty("functionName").GetString());
        Assert.Equal("api order", ticker.GetProperty("description").GetString());
        Assert.Equal(2, ticker.GetProperty("retries").GetInt32());
        Assert.Equal([1, 2], ticker.GetProperty("retryIntervalsSeconds").EnumerateArray().Select(x => x.GetInt32()));
        Assert.Equal(30, ticker.GetProperty("timeoutSeconds").GetInt32());
        Assert.NotEqual(JsonValueKind.Null, ticker.GetProperty("executedAt").ValueKind);
        Assert.Equal(order, Decode<Order>((await node.GetAsync($"{D}/time-tickers/{id}/request")).GetProperty("payload")));
        var result = await node.GetAsync($"{D}/time-tickers/{id}/result");
        Assert.Equal(new Receipt("widget", 42), Decode<Receipt>(result.GetProperty("payload")));
        Assert.Equal("application/json", result.GetProperty("mediaType").GetString());

        Assert.Equal(id, Single(await node.PostAsync($"{D}/time-tickers/query",
            new { functionName = function, statuses = Succeeded })));
        Assert.Equal(id, Single(await node.PostAsync($"{D}/time-tickers/query", new { search = "api order" })));
        Assert.Equal(0, (await node.PostAsync($"{D}/time-tickers/query",
            new { functionName = function, statuses = new[] { "Failed" } })).GetProperty("totalCount").GetInt32());
        Assert.Equal(id, Single(await node.PostAsync($"{D}/executions/query",
            new { functionName = function, types = new[] { "TimeTicker" } })));
        Assert.Contains(id, Ids(await node.GetAsync($"{D}/overview/recent-activity?count=5")));
        Assert.Equal(1, SucceededCount(await node.GetAsync($"{D}/stats/overall-statuses")));
        Assert.Equal(1, (await node.GetAsync($"{D}/graphs/time-tickers?pastDays=1&futureDays=1")).EnumerateArray()
            .Sum(day => SucceededCount(day.GetProperty("counts"))));

        // The node persists execution logs, so the tail is read back from this store.
        await WaitUntilAsync(async () => (await node.GetAsync($"{D}/log-tail/{id}")).GetProperty("lines").GetArrayLength() > 0,
            "the job's log lines to reach the store");
        Assert.Equal("database", (await node.GetAsync($"{D}/log-tail/{id}")).GetProperty("storage").GetString());
        var first = (await node.PostAsync($"{D}/log-tail/query", new { items = new[] { new { tickerId = id } } }))
            .GetProperty("items")[0];
        Assert.Equal(id, first.GetProperty("tickerId").GetGuid());
        Assert.NotEqual(0, first.GetProperty("tail").GetProperty("lines").GetArrayLength());
        var next = (await node.PostAsync($"{D}/log-tail/query",
            new { items = new[] { new { tickerId = id, cursor = first.GetProperty("cursor").GetString() } } })).GetProperty("items")[0];
        Assert.False(next.GetProperty("reset").GetBoolean());
    }

    [DashboardApiFact]
    public async Task PendingJob_CanBeEditedDuplicatedRunCancelledAndDeleted()
    {
        var function = Unique("pending");
        await using var node = await StartAsync(s => s.MapTicker<Order>(function, (_, _) => Task.CompletedTask));
        var later = DateTime.UtcNow.AddHours(1);

        var edited = await node.AddTimeTickerAsync(new
        {
            function, executionTime = later, request = Request(new Order("before", 1)),
        });
        Assert.Equal(edited, (await node.GetAsync($"{D}/host/next-ticker")).GetProperty("id").GetGuid());
        Assert.Contains(edited, Ids(await node.GetAsync($"{D}/overview/upcoming")));

        await node.SendAsync(HttpMethod.Patch, $"{D}/time-tickers/{edited}", new
        {
            description = "edited", retries = 3, executionTime = later.AddMinutes(5),
            request = Request(new Order("after", 2)),
        }, HttpStatusCode.NoContent);
        var ticker = await node.GetAsync($"{D}/time-tickers/{edited}");
        Assert.Equal("edited", ticker.GetProperty("description").GetString());
        Assert.Equal(3, ticker.GetProperty("retries").GetInt32());
        Assert.Equal(new Order("after", 2), Decode<Order>((await node.GetAsync($"{D}/time-tickers/{edited}/request")).GetProperty("payload")));

        // Streamed, as HttpClient's JsonContent sends it: no Content-Length, and the body still counts.
        var copy = Guid.Parse((await node.SendAsync(HttpMethod.Post, $"{D}/time-tickers/{edited}/duplicate",
            new { executionTime = later }, HttpStatusCode.OK, streamed: true)).GetProperty("id").GetString()!);
        Assert.Equal("Idle", (await node.GetAsync($"{D}/time-tickers/{copy}")).GetProperty("status").GetString());
        Assert.NotEqual(edited, copy);
        Assert.Equal("edited", (await node.GetAsync($"{D}/time-tickers/{copy}")).GetProperty("description").GetString());
        Assert.Equal(new Order("after", 2), Decode<Order>((await node.GetAsync($"{D}/time-tickers/{copy}/request")).GetProperty("payload")));

        await node.SendAsync(HttpMethod.Post, $"{D}/time-tickers/{edited}/run", null, HttpStatusCode.NoContent);
        await node.WaitForStatusAsync(edited, Succeeded);
        await node.SendAsync(HttpMethod.Patch, $"{D}/time-tickers/{edited}", new { description = "too late" }, HttpStatusCode.Conflict);
        await node.SendAsync(HttpMethod.Post, $"{D}/time-tickers/{edited}/cancel", null, HttpStatusCode.Conflict);

        var cancelled = await node.SendAsync(HttpMethod.Post, $"{D}/time-tickers/{copy}/cancel", null, HttpStatusCode.OK);
        Assert.Equal("Cancelled", cancelled.GetProperty("outcome").GetString());
        Assert.Equal(1, cancelled.GetProperty("affected").GetInt32());
        var cancelledTicker = await node.GetAsync($"{D}/time-tickers/{copy}");
        Assert.Equal("Cancelled", cancelledTicker.GetProperty("status").GetString());
        Assert.Contains("before any node picked it up", cancelledTicker.GetProperty("exceptionMessage").GetString());
        Assert.Equal(0, (await node.SendAsync(HttpMethod.Post, $"{D}/time-tickers/{copy}/cancel", null, HttpStatusCode.OK))
            .GetProperty("affected").GetInt32());

        var payload = Request(new Order("pending", 1));
        var deleted = await node.AddTimeTickerAsync(new { function, executionTime = later, request = payload });
        await node.SendAsync(HttpMethod.Delete, $"{D}/time-tickers/{deleted}", null, HttpStatusCode.NoContent);
        await node.SendAsync(HttpMethod.Get, $"{D}/time-tickers/{deleted}", null, HttpStatusCode.NotFound);
        await node.SendAsync(HttpMethod.Delete, $"{D}/time-tickers/{deleted}", null, HttpStatusCode.Conflict);

        var bulk = new List<Guid>();
        for (var i = 0; i < 3; i++)
            bulk.Add(await node.AddTimeTickerAsync(new { function, executionTime = later, description = "bulk", request = payload }));
        Assert.Equal(3, (await node.PostAsync($"{D}/time-tickers/bulk-delete", new { ids = bulk })).GetProperty("affected").GetInt32());
        Assert.Equal(0, (await node.PostAsync($"{D}/time-tickers/query", new { search = "bulk" })).GetProperty("totalCount").GetInt32());
    }

    [DashboardApiFact]
    public async Task NextUpcomingJob_StaysUnclaimedUntilItIsDue_SoItCanBeEditedAndDeleted()
    {
        var function = Unique("upcoming");
        await using var node = await StartAsync(s => s.MapTicker(function, (_, _) => Task.CompletedTask));

        var id = await node.AddTimeTickerAsync(new { function, executionTime = DateTime.UtcNow.AddDays(30) });
        // Give the scheduler time to read it as its next job; it must plan a wake, not claim it.
        await Task.Delay(TimeSpan.FromSeconds(2));
        var ticker = await node.GetAsync($"{D}/time-tickers/{id}");
        Assert.Equal("Idle", ticker.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, ticker.GetProperty("lockHolder").ValueKind);

        await node.SendAsync(HttpMethod.Patch, $"{D}/time-tickers/{id}", new { description = "still editable" },
            HttpStatusCode.NoContent);
        await node.SendAsync(HttpMethod.Delete, $"{D}/time-tickers/{id}", null, HttpStatusCode.NoContent);
        await node.SendAsync(HttpMethod.Get, $"{D}/time-tickers/{id}", null, HttpStatusCode.NotFound);
    }

    [DashboardApiFact]
    public async Task Chain_RunsEveryBranchByItsCondition_ReadsBackAsATree_AndCanBeReplacedWhilePending()
    {
        var root = Unique("root");
        var child = Unique("child");
        var grandchild = Unique("grandchild");
        var fallback = Unique("fallback");
        await using var node = await StartAsync(s =>
        {
            foreach (var name in new[] { root, child, grandchild, fallback })
                s.MapTicker(name, (_, _) => Task.CompletedTask);
        });

        var created = await node.PostAsync($"{D}/time-tickers/chain", new
        {
            executionTime = DateTime.UtcNow,
            root = new
            {
                function = root, description = "chain root",
                children = new object[]
                {
                    new { function = child, children = new[] { new { function = grandchild } } },
                    new { function = fallback, runCondition = "OnFailure" },
                },
            },
        });
        Assert.Equal(4, created.GetProperty("createdCount").GetInt32());
        var rootId = Guid.Parse(created.GetProperty("rootId").GetString()!);

        await WaitUntilAsync(async () => (await node.GetAsync($"{D}/time-tickers/{rootId}/chain")).EnumerateArray()
            .All(x => x.GetProperty("status").GetString() is "Done" or "DueDone" or "Skipped"), "every chain step to finish");
        var chain = await node.GetAsync($"{D}/time-tickers/{rootId}/chain");
        Assert.Equal(4, chain.GetArrayLength());
        Assert.Equal(rootId, chain[0].GetProperty("id").GetGuid());
        Assert.Equal(4, chain[0].GetProperty("chainSummary").GetProperty("totalCount").GetInt32());
        Assert.Equal("Skipped", StatusOf(chain, fallback));
        Assert.Contains(StatusOf(chain, grandchild), Succeeded);
        Assert.Equal(new[] { child, fallback }.Order(),
            Names(await node.GetAsync($"{D}/time-tickers/{rootId}/children"), "functionName").Order());
        // Only roots are listed; their steps are read through the chain.
        Assert.Equal(rootId, Single(await node.PostAsync($"{D}/time-tickers/query", new { search = "chain root" })));

        await node.SendAsync(HttpMethod.Patch, $"{D}/time-tickers/{rootId}/delete-behavior",
            new { deleteBehavior = "Restrict" }, HttpStatusCode.NoContent);
        Assert.Equal("Restrict", (await node.GetAsync($"{D}/time-tickers/{rootId}")).GetProperty("deleteBehavior").GetString());

        var pending = Guid.Parse((await node.PostAsync($"{D}/time-tickers/chain", new
        {
            executionTime = DateTime.UtcNow.AddHours(1),
            root = new { function = root, children = new[] { new { function = child } } },
        })).GetProperty("rootId").GetString()!);
        var replaced = await node.SendAsync(HttpMethod.Put, $"{D}/time-tickers/chain/{pending}", new
        {
            executionTime = DateTime.UtcNow.AddHours(2),
            root = new { function = root, children = new[] { new { function = child, children = new[] { new { function = grandchild } } } } },
        }, HttpStatusCode.OK);
        Assert.Equal(3, replaced.GetProperty("createdCount").GetInt32());
        var replacement = Guid.Parse(replaced.GetProperty("rootId").GetString()!);
        Assert.NotEqual(pending, replacement);
        await node.SendAsync(HttpMethod.Get, $"{D}/time-tickers/{pending}", null, HttpStatusCode.NotFound);
        Assert.Equal(3, (await node.GetAsync($"{D}/time-tickers/{replacement}/chain")).GetArrayLength());
    }

    [DashboardApiFact]
    public async Task Search_FindsAJobByItsId_AndAChainStepOrCronRunThroughTheJobItBelongsTo()
    {
        var function = Unique("search");
        var step = Unique("search-step");
        await using var node = await StartAsync(s =>
        {
            s.MapTicker(function, (_, _) => Task.CompletedTask);
            s.MapTicker(step, (_, _) => Task.CompletedTask);
        });
        var later = DateTime.UtcNow.AddHours(1);

        var standalone = await node.AddTimeTickerAsync(new { function, executionTime = later });
        var rootId = Guid.Parse((await node.PostAsync($"{D}/time-tickers/chain", new
        {
            executionTime = later,
            root = new { function, children = new[] { new { function = step, children = new[] { new { function = step } } } } },
        })).GetProperty("rootId").GetString()!);
        var grandchild = (await node.GetAsync($"{D}/time-tickers/{rootId}/chain")).EnumerateArray()
            .Single(x => x.GetProperty("parentId").ValueKind != JsonValueKind.Null && x.GetProperty("parentId").GetGuid() != rootId)
            .GetProperty("id").GetGuid();

        Assert.Equal(standalone, Single(await node.PostAsync($"{D}/time-tickers/query", new { search = standalone.ToString() })));
        Assert.Equal(standalone, Single(await node.PostAsync($"{D}/time-tickers/query",
            new { search = $" {standalone.ToString("N").ToUpperInvariant()} " })));
        // Only roots are listed, so a step is found through the chain that holds it.
        Assert.Equal(rootId, Single(await node.PostAsync($"{D}/time-tickers/query", new { search = grandchild.ToString() })));
        Assert.Equal(0, (await node.PostAsync($"{D}/time-tickers/query", new { search = Guid.NewGuid().ToString() }))
            .GetProperty("totalCount").GetInt32());
        Assert.Equal(2, (await node.PostAsync($"{D}/time-tickers/query", new { search = function })).GetProperty("totalCount").GetInt32());

        await node.SendAsync(HttpMethod.Post, $"{D}/time-tickers/{standalone}/run", null, HttpStatusCode.NoContent);
        await node.WaitForStatusAsync(standalone, Succeeded);
        Assert.Equal(standalone, Single(await node.PostAsync($"{D}/executions/query", new { search = standalone.ToString() })));

        var cronId = Guid.Parse((await node.PostAsync($"{D}/cron-tickers", new
        {
            function, expression = "0 0 0 1 1 *", isEnabled = false,
        })).GetProperty("id").GetString()!);
        await node.SendAsync(HttpMethod.Post, $"{D}/cron-tickers/{cronId}/run", null, HttpStatusCode.NoContent);
        await WaitUntilAsync(async () => await node.SucceededOccurrencesAsync(cronId) >= 1, "the on-demand cron run",
            () => node.DescribeOccurrencesAsync(cronId));
        var occurrence = (await node.PostAsync($"{D}/cron-occurrences/{cronId}/query", new { statuses = Succeeded }))
            .GetProperty("items")[0].GetProperty("id").GetGuid();

        Assert.Equal(cronId, Single(await node.PostAsync($"{D}/cron-tickers/query", new { search = cronId.ToString() })));
        Assert.Equal(cronId, Single(await node.PostAsync($"{D}/cron-tickers/query", new { search = occurrence.ToString() })));
        Assert.Equal(occurrence, Single(await node.PostAsync($"{D}/executions/query", new { search = occurrence.ToString() })));
        var runs = (await node.PostAsync($"{D}/executions/query", new { search = cronId.ToString() })).GetProperty("items");
        Assert.NotEqual(0, runs.GetArrayLength());
        Assert.All(runs.EnumerateArray(), x => Assert.Equal(cronId, x.GetProperty("cronTickerId").GetGuid()));
    }

    [DashboardApiFact]
    public async Task FailedJobs_RetriedFromHereOrInBulk_RunAgainInPlace()
    {
        var function = Unique("flaky");
        var calls = new System.Collections.Concurrent.ConcurrentDictionary<Guid, int>();
        await using var node = await StartAsync(s => s.MapTicker(function, (context, _) =>
            calls.AddOrUpdate(context.Id, 1, (_, n) => n + 1) == 1
                ? throw new InvalidOperationException("first attempt fails")
                : Task.CompletedTask));

        var failed = await node.AddTimeTickerAsync(new { function, executionTime = DateTime.UtcNow });
        await node.WaitForStatusAsync(failed, "Failed");
        var detail = await node.GetAsync($"{D}/time-tickers/{failed}");
        Assert.Contains("first attempt fails", detail.GetProperty("exceptionMessage").GetString());
        Assert.Equal(failed, Single(await node.PostAsync($"{D}/executions/query",
            new { functionName = function, statuses = new[] { "Failed" } })));

        // Retry-from-here re-runs the job under a fresh generation of the same row.
        Assert.Equal(failed, Guid.Parse((await node.PostAsync($"{D}/time-tickers/{failed}/retry-from-here", null))
            .GetProperty("id").GetString()!));
        await node.WaitForStatusAsync(failed, Succeeded);
        Assert.Equal(2, calls[failed]);

        var bulk = await node.AddTimeTickerAsync(new { function, executionTime = DateTime.UtcNow });
        await node.WaitForStatusAsync(bulk, "Failed");
        Assert.Equal(1, (await node.PostAsync($"{D}/executions/bulk-retry",
            new { items = new[] { new { id = bulk, type = "TimeTicker" } } })).GetProperty("affected").GetInt32());
        await node.WaitForStatusAsync(bulk, Succeeded);
        Assert.Equal(2, calls[bulk]);
        Assert.Equal(2, (await node.PostAsync($"{D}/time-tickers/query", new { functionName = function })).GetProperty("totalCount").GetInt32());
    }

    [DashboardApiFact]
    public async Task RunningJobs_StopWhenCancelled_AndRecordThatTheRequestCameFromTheDashboard()
    {
        var function = Unique("running");
        var running = new System.Collections.Concurrent.ConcurrentDictionary<Guid, bool>();
        await using var node = await StartAsync(s => s.MapTicker(function, async (context, cancellationToken) =>
        {
            running[context.Id] = true;
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }));

        var requested = await StartRunningAsync();
        Assert.Equal("Requested", (await node.SendAsync(HttpMethod.Post, $"{D}/time-tickers/{requested}/request-cancellation",
            null, HttpStatusCode.Accepted)).GetProperty("outcome").GetString());
        await node.WaitForStatusAsync(requested, "Cancelled");
        var exception = JsonDocument.Parse((await node.GetAsync($"{D}/time-tickers/{requested}"))
            .GetProperty("exceptionMessage").GetString()!).RootElement;
        Assert.Equal("Cancelled: requested from the dashboard or a manager.", exception.GetProperty("Reason").GetString());
        await node.SendAsync(HttpMethod.Post, $"{D}/time-tickers/{requested}/request-cancellation", null, HttpStatusCode.Conflict);

        var direct = await StartRunningAsync();
        await node.SendAsync(HttpMethod.Post, $"{D}/tickers/{direct}/cancel", null, HttpStatusCode.NoContent);
        await node.WaitForStatusAsync(direct, "Cancelled");
        await node.SendAsync(HttpMethod.Post, $"{D}/tickers/{Guid.NewGuid()}/cancel", null, HttpStatusCode.NotFound);

        var bulk = await StartRunningAsync();
        Assert.Equal(1, (await node.PostAsync($"{D}/tickers/bulk-cancel", new { ids = new[] { bulk, Guid.NewGuid() } }))
            .GetProperty("affected").GetInt32());
        await node.WaitForStatusAsync(bulk, "Cancelled");

        async Task<Guid> StartRunningAsync()
        {
            var id = await node.AddTimeTickerAsync(new { function, executionTime = DateTime.UtcNow });
            await WaitUntilAsync(() => Task.FromResult(running.ContainsKey(id)), $"job {id} to start");
            await node.WaitForStatusAsync(id, "InProgress");
            return id;
        }
    }

    [DashboardApiFact]
    public async Task Cron_RunsOccurrencesWithItsPayload_ThenCanBeEditedPausedRunRerunAndDeleted()
    {
        var function = Unique("cron");
        var received = new System.Collections.Concurrent.ConcurrentQueue<Order>();
        await using var node = await StartAsync(s => s.MapTicker<Order>(function, (context, _) =>
        {
            received.Enqueue(context.Request);
            return Task.CompletedTask;
        }));
        var order = new Order("recurring", 7);

        var id = Guid.Parse((await node.PostAsync($"{D}/cron-tickers", new
        {
            function, expression = "* * * * * *", description = "every second", retries = 1,
            retryIntervalsSeconds = new[] { 1 }, request = Request(order),
        })).GetProperty("id").GetString()!);
        var cron = await node.GetAsync($"{D}/cron-tickers/{id}");
        Assert.Equal("* * * * * *", cron.GetProperty("expression").GetString());
        Assert.Equal("every second", cron.GetProperty("description").GetString());
        Assert.Equal(1, cron.GetProperty("retries").GetInt32());
        Assert.True(cron.GetProperty("isEnabled").GetBoolean());
        Assert.Equal(order, Decode<Order>((await node.GetAsync($"{D}/cron-tickers/{id}/request")).GetProperty("payload")));
        Assert.Equal(id, Single(await node.PostAsync($"{D}/cron-tickers/query", new { functionName = function })));

        await WaitUntilAsync(async () => await node.SucceededOccurrencesAsync(id) >= 1, "a finished occurrence",
            () => node.DescribeOccurrencesAsync(id));
        Assert.Equal(order, received.First());
        var occurrence = (await node.PostAsync($"{D}/cron-occurrences/{id}/query", new { statuses = Succeeded }))
            .GetProperty("items")[0].GetProperty("id").GetGuid();
        var occurrenceDetail = await node.GetAsync($"{D}/cron-occurrences/{occurrence}");
        Assert.Equal(id, occurrenceDetail.GetProperty("cronTickerId").GetGuid());
        Assert.Contains(occurrenceDetail.GetProperty("status").GetString(), Succeeded);
        Assert.Equal(JsonValueKind.Object, (await node.GetAsync($"{D}/cron-occurrences/{occurrence}/request")).ValueKind);
        Assert.NotEqual(0, (await node.PostAsync($"{D}/executions/query",
            new { functionName = function, types = new[] { "CronOccurrence" } })).GetProperty("totalCount").GetInt32());
        Assert.NotEqual(0, (await node.GetAsync($"{D}/graphs/cron-tickers/{id}?pastDays=1&futureDays=1")).EnumerateArray()
            .Sum(day => SucceededCount(day.GetProperty("counts"))));
        Assert.NotEqual(0, (await node.GetAsync($"{D}/graphs/cron-occurrences/{id}")).GetArrayLength());

        await node.SendAsync(HttpMethod.Patch, $"{D}/cron-tickers/{id}",
            new { description = "edited", expression = "*/2 * * * * *" }, HttpStatusCode.NoContent);
        cron = await node.GetAsync($"{D}/cron-tickers/{id}");
        Assert.Equal("edited", cron.GetProperty("description").GetString());
        Assert.Equal("*/2 * * * * *", cron.GetProperty("expression").GetString());

        await node.SendAsync(HttpMethod.Post, $"{D}/cron-tickers/{id}/toggle", new { isEnabled = false }, HttpStatusCode.NoContent);
        Assert.False((await node.GetAsync($"{D}/cron-tickers/{id}")).GetProperty("isEnabled").GetBoolean());
        await WaitUntilAsync(async () => (await node.PostAsync($"{D}/cron-occurrences/{id}/query",
                new { statuses = new[] { "Idle", "Queued", "InProgress" } })).GetProperty("totalCount").GetInt32() == 0,
            "the paused cron to finish its last occurrence");
        var before = await node.SucceededOccurrencesAsync(id);
        await node.SendAsync(HttpMethod.Post, $"{D}/cron-tickers/{id}/run", null, HttpStatusCode.NoContent);
        await WaitUntilAsync(async () => await node.SucceededOccurrencesAsync(id) > before, "the on-demand run");

        // Editing the expression started a new definition revision; an occurrence of the older one
        // cannot be rerun, and says so. The occurrence the on-demand run just created can.
        var refused = await node.SendAsync(HttpMethod.Post, $"{D}/cron-occurrences/{occurrence}/run", null,
            HttpStatusCode.BadRequest);
        Assert.Contains("earlier version of its cron ticker", refused.GetProperty("error").GetString());
        occurrence = (await node.PostAsync($"{D}/cron-occurrences/{id}/query", new { statuses = Succeeded, sortBy = "createdat" }))
            .GetProperty("items")[0].GetProperty("id").GetGuid();
        var runs = received.Count;
        await node.SendAsync(HttpMethod.Post, $"{D}/cron-occurrences/{occurrence}/run", null, HttpStatusCode.NoContent);
        await WaitUntilAsync(() => Task.FromResult(received.Count > runs), "the rerun occurrence to execute");
        await WaitUntilAsync(async () =>
        {
            var status = (await node.GetAsync($"{D}/cron-occurrences/{occurrence}")).GetProperty("status").GetString();
            return Succeeded.Contains(status);
        }, "the rerun occurrence to finish");

        await node.SendAsync(HttpMethod.Delete, $"{D}/cron-occurrences/{occurrence}", null, HttpStatusCode.NoContent);
        await node.SendAsync(HttpMethod.Get, $"{D}/cron-occurrences/{occurrence}", null, HttpStatusCode.NotFound);
        await node.SendAsync(HttpMethod.Delete, $"{D}/cron-tickers/{id}", null, HttpStatusCode.NoContent);
        await node.SendAsync(HttpMethod.Get, $"{D}/cron-tickers/{id}", null, HttpStatusCode.NotFound);

        var paused = new List<Guid>();
        for (var i = 0; i < 2; i++)
            paused.Add(Guid.Parse((await node.PostAsync($"{D}/cron-tickers", new
            {
                function, expression = "0 0 * * * *", isEnabled = false, request = Request(order),
            })).GetProperty("id").GetString()!));
        Assert.Equal(2, (await node.PostAsync($"{D}/cron-tickers/bulk-delete", new { ids = paused })).GetProperty("affected").GetInt32());
        Assert.Equal(0, (await node.PostAsync($"{D}/cron-tickers/query", new { functionName = function })).GetProperty("totalCount").GetInt32());
    }

    [DashboardApiFact]
    public async Task CronOccurrence_StopsWhenCancellationIsRequested()
    {
        var function = Unique("cron-running");
        await using var node = await StartAsync(s => s.MapTicker(function, (_, cancellationToken) =>
            Task.Delay(Timeout.Infinite, cancellationToken)));

        var id = Guid.Parse((await node.PostAsync($"{D}/cron-tickers",
            new { function, expression = "* * * * * *", skipIfAlreadyRunning = true })).GetProperty("id").GetString()!);
        Guid occurrence = default;
        await WaitUntilAsync(async () =>
        {
            var page = await node.PostAsync($"{D}/cron-occurrences/{id}/query", new { statuses = new[] { "InProgress" } });
            if (page.GetProperty("totalCount").GetInt32() == 0) return false;
            occurrence = page.GetProperty("items")[0].GetProperty("id").GetGuid();
            return true;
        }, "an occurrence to start");
        await node.SendAsync(HttpMethod.Post, $"{D}/cron-tickers/{id}/toggle", new { isEnabled = false }, HttpStatusCode.NoContent);

        await node.SendAsync(HttpMethod.Post, $"{D}/cron-occurrences/{occurrence}/request-cancellation", null, HttpStatusCode.Accepted);
        await WaitUntilAsync(async () => (await node.GetAsync($"{D}/cron-occurrences/{occurrence}")).GetProperty("status")
            .GetString() == "Cancelled", "the occurrence to be cancelled");
        await node.SendAsync(HttpMethod.Post, $"{D}/cron-occurrences/{occurrence}/request-cancellation", null, HttpStatusCode.Conflict);
    }

    [DashboardApiFact]
    public async Task CronChain_RunsItsStepsAfterEachOccurrence()
    {
        var function = Unique("cron-root");
        var step = Unique("cron-step");
        var stepRan = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var node = await StartAsync(s =>
        {
            s.MapTicker(function, (_, _) => Task.CompletedTask);
            s.MapTicker(step, (_, _) => { stepRan.TrySetResult(); return Task.CompletedTask; });
        });

        var id = Guid.Parse((await node.PostAsync($"{D}/cron-tickers", new
        {
            function, expression = "* * * * * *",
            chain = new { children = new[] { new { function = step } } },
        })).GetProperty("id").GetString()!);
        Assert.Equal(step, (await node.GetAsync($"{D}/cron-tickers/{id}")).GetProperty("chain").GetProperty("children")[0]
            .GetProperty("function").GetString());
        await stepRan.Task.WaitAsync(Deadline);
        await node.SendAsync(HttpMethod.Post, $"{D}/cron-tickers/{id}/toggle", new { isEnabled = false }, HttpStatusCode.NoContent);
    }

    [DashboardApiFact]
    public async Task TestRuns_ExecuteFunctionsInline_WithoutStoringAJob()
    {
        var echo = Unique("echo");
        var broken = Unique("broken");
        var next = Unique("next");
        await using var node = await StartAsync(s =>
        {
            s.MapTicker<Order>(echo, (context, _) =>
            {
                context.SetResult(new Receipt(context.Request.Name, context.Request.Quantity));
                return Task.CompletedTask;
            });
            s.MapTicker(broken, (_, _) => throw new InvalidOperationException("broken on purpose"));
            s.MapTicker(next, (_, _) => Task.CompletedTask);
        });

        var run = await node.PostAsync($"{D}/functions/test-run",
            new { function = echo, request = Request(new Order("test", 3)) });
        Assert.True(run.GetProperty("success").GetBoolean(), run.ToString());
        Assert.Equal(new Receipt("test", 3), Decode<Receipt>(run.GetProperty("result")));
        var failed = await node.PostAsync($"{D}/functions/test-run", new { function = broken });
        Assert.False(failed.GetProperty("success").GetBoolean());
        Assert.Equal("Failed", failed.GetProperty("status").GetString());
        Assert.Contains("broken on purpose", failed.GetProperty("error").GetString());
        await node.SendAsync(HttpMethod.Post, $"{D}/functions/test-run", new { function = Unique("missing") }, HttpStatusCode.NotFound);

        var chain = await node.SendAsync(HttpMethod.Post, $"{D}/functions/test-run-chain", new
        {
            executionTime = DateTime.UtcNow,
            root = new { function = echo, request = Request(new Order("chain", 1)), children = new[] { new { function = next } } },
        }, HttpStatusCode.Accepted);
        var session = chain.GetProperty("rootExecutionId").GetGuid();
        await WaitUntilAsync(async () => (await node.GetAsync($"{D}/functions/test-run-chain/{session}"))
            .GetProperty("completed").GetBoolean(), "the test chain to finish");
        var finished = await node.GetAsync($"{D}/functions/test-run-chain/{session}");
        Assert.True(finished.GetProperty("success").GetBoolean(), finished.ToString());
        Assert.Equal(2, finished.GetProperty("steps").GetArrayLength());
        await node.SendAsync(HttpMethod.Get, $"{D}/functions/test-run-chain/{Guid.NewGuid()}", null, HttpStatusCode.NotFound);

        Assert.Equal(0, (await node.PostAsync($"{D}/time-tickers/query", null)).GetProperty("totalCount").GetInt32());
    }

    [DashboardApiFact]
    public async Task Host_CanBeStoppedAndStarted_AndRunsWhatWasAddedWhileStopped()
    {
        var function = Unique("host");
        await using var node = await StartAsync(s => s.MapTicker(function, (_, _) => Task.CompletedTask));

        await node.SendAsync(HttpMethod.Post, $"{D}/host/stop", null, HttpStatusCode.NoContent);
        await WaitUntilAsync(async () => !(await node.GetAsync($"{D}/host/status")).GetProperty("isRunning").GetBoolean(),
            "the scheduler to stop");
        var id = await node.AddTimeTickerAsync(new { function, executionTime = DateTime.UtcNow });
        await Task.Delay(500);
        Assert.Equal("Idle", (await node.GetAsync($"{D}/time-tickers/{id}")).GetProperty("status").GetString());

        await node.SendAsync(HttpMethod.Post, $"{D}/host/start", null, HttpStatusCode.NoContent);
        Assert.True((await node.GetAsync($"{D}/host/status")).GetProperty("isRunning").GetBoolean());
        await node.WaitForStatusAsync(id, Succeeded);
        await node.SendAsync(HttpMethod.Post, $"{D}/host/restart", null, HttpStatusCode.NoContent);
        Assert.True((await node.GetAsync($"{D}/host/status")).GetProperty("isRunning").GetBoolean());
    }

    [DashboardApiFact]
    public async Task Definitions_AreSavedVersionedCapturedFromJobsAndActivated()
    {
        var first = Unique("first");
        var second = Unique("second");
        await using var node = await StartAsync(s =>
        {
            s.MapTicker<Order>(first, (_, _) => Task.CompletedTask);
            s.MapTicker(second, (_, _) => Task.CompletedTask);
        });
        var request = Request(new Order("template", 5));

        var template = await node.SendAsync(HttpMethod.Post, $"{D}/definitions", new
        {
            kind = "Template", target = "TimeTicker", name = "Order flow", description = "two steps", includeRequestData = true,
            payload = new { root = new { function = first, request, children = new[] { new { function = second } } } },
        }, HttpStatusCode.Created);
        var templateId = template.GetProperty("id").GetGuid();
        var version = template.GetProperty("version").GetInt64();
        Assert.True(template.GetProperty("includesRequestData").GetBoolean());
        Assert.Contains(templateId, Ids(await node.GetAsync($"{D}/definitions?kind=Template")));
        Assert.DoesNotContain(templateId, Ids(await node.GetAsync($"{D}/definitions?target=CronTicker")));
        var saved = await node.GetAsync($"{D}/definitions/{templateId}");
        Assert.Equal("Order flow", saved.GetProperty("name").GetString());
        var savedRoot = saved.GetProperty("payload").GetProperty("root");
        Assert.Equal(second, savedRoot.GetProperty("children")[0].GetProperty("function").GetString());
        Assert.Equal(new Order("template", 5), Decode<Order>(savedRoot.GetProperty("request")));
        await node.SendAsync(HttpMethod.Post, $"{D}/definitions", new
        {
            kind = "Template", target = "TimeTicker", name = "order FLOW",
            payload = new { root = new { function = first, children = new[] { new { function = second } } } },
        }, HttpStatusCode.Conflict);

        var updated = await node.SendAsync(HttpMethod.Put, $"{D}/definitions/{templateId}", new
        {
            kind = "Template", target = "TimeTicker", name = "Order flow v2", includeRequestData = false, version,
            payload = new { root = new { function = first, children = new[] { new { function = second } } } },
        }, HttpStatusCode.OK);
        Assert.True(updated.GetProperty("version").GetInt64() > version);
        Assert.Equal("Order flow v2", updated.GetProperty("name").GetString());
        await node.SendAsync(HttpMethod.Put, $"{D}/definitions/{templateId}", new
        {
            kind = "Template", target = "TimeTicker", name = "stale", version,
            payload = new { root = new { function = first, children = new[] { new { function = second } } } },
        }, HttpStatusCode.Conflict);

        var draft = await node.SendAsync(HttpMethod.Post, $"{D}/definitions", new
        {
            kind = "Draft", target = "TimeTicker", name = "one-off", includeRequestData = true,
            payload = new { executionTime = DateTime.UtcNow, root = new { function = first, request } },
        }, HttpStatusCode.Created);
        var draftId = draft.GetProperty("id").GetGuid();
        var activated = await node.PostAsync($"{D}/definitions/{draftId}/activate", new { version = draft.GetProperty("version").GetInt64() });
        Assert.Equal(1, activated.GetProperty("createdCount").GetInt32());
        var activatedRoot = Guid.Parse(activated.GetProperty("rootId").GetString()!);
        await node.WaitForStatusAsync(activatedRoot, Succeeded);
        Assert.Equal(new Order("template", 5),
            Decode<Order>((await node.GetAsync($"{D}/time-tickers/{activatedRoot}/request")).GetProperty("payload")));
        var again = await node.PostAsync($"{D}/definitions/{draftId}/activate", new { version = draft.GetProperty("version").GetInt64() });
        Assert.Equal(0, again.GetProperty("createdCount").GetInt32());
        Assert.Equal(activatedRoot, Guid.Parse(again.GetProperty("rootId").GetString()!));

        var chainRoot = Guid.Parse((await node.PostAsync($"{D}/time-tickers/chain", new
        {
            executionTime = DateTime.UtcNow.AddHours(1),
            root = new { function = first, request, children = new[] { new { function = second } } },
        })).GetProperty("rootId").GetString()!);
        var captured = await node.SendAsync(HttpMethod.Post, $"{D}/definitions/from-time-ticker/{chainRoot}",
            new { name = "Captured chain" }, HttpStatusCode.Created);
        Assert.Equal("TimeTicker", captured.GetProperty("target").GetString());
        Assert.Equal("Template", captured.GetProperty("kind").GetString());
        await node.SendAsync(HttpMethod.Post, $"{D}/definitions/from-time-ticker/{activatedRoot}",
            new { name = "Single job" }, HttpStatusCode.BadRequest);

        var cron = Guid.Parse((await node.PostAsync($"{D}/cron-tickers", new
        {
            function = first, expression = "0 0 * * * *", isEnabled = false, request,
            chain = new { children = new[] { new { function = second } } },
        })).GetProperty("id").GetString()!);
        var fromCron = await node.SendAsync(HttpMethod.Post, $"{D}/definitions/from-cron-ticker/{cron}",
            new { name = "Captured cron" }, HttpStatusCode.Created);
        Assert.Equal("CronTicker", fromCron.GetProperty("target").GetString());
        Assert.Equal(second, fromCron.GetProperty("payload").GetProperty("root").GetProperty("children")[0]
            .GetProperty("function").GetString());

        var current = updated.GetProperty("version").GetInt64();
        await node.SendAsync(HttpMethod.Delete, $"{D}/definitions/{templateId}?version=0", null, HttpStatusCode.BadRequest);
        await node.SendAsync(HttpMethod.Delete, $"{D}/definitions/{templateId}?version={version}", null, HttpStatusCode.Conflict);
        await node.SendAsync(HttpMethod.Delete, $"{D}/definitions/{templateId}?version={current}", null, HttpStatusCode.NoContent);
        await node.SendAsync(HttpMethod.Get, $"{D}/definitions/{templateId}", null, HttpStatusCode.NotFound);
    }

    [DashboardApiFact]
    public async Task SchedulePolicies_AreSavedPreviewedUsedAndProtectedWhileInUse()
    {
        var function = Unique("policy");
        await using var node = await StartAsync(s => s.MapTicker(function, (_, _) => Task.CompletedTask));
        Assert.Equal(JsonValueKind.Array, (await node.GetAsync($"{D}/schedule-policies/date-providers")).GetProperty("names").ValueKind);

        var policy = await node.SendAsync(HttpMethod.Post, $"{D}/schedule-policies", new
        {
            name = "Office hours", description = "weekdays 9 to 5", timeZoneId = "UTC", allowedWeekdays = "MondayToFriday",
            allowedTimeWindows = new[] { new { start = "09:00:00", end = "17:00:00" } },
            excludedDates = new[] { "2030-12-25" }, excludedAnnualDates = new[] { new { month = 1, day = 1 } },
        }, HttpStatusCode.Created);
        var id = policy.GetProperty("id").GetGuid();
        var version = policy.GetProperty("version").GetInt64();
        var saved = await node.GetAsync($"{D}/schedule-policies/{id}");
        Assert.Equal("Office hours", saved.GetProperty("name").GetString());
        Assert.Equal(policy.GetProperty("allowedWeekdays").GetString(), saved.GetProperty("allowedWeekdays").GetString());
        Assert.Equal("09:00:00", saved.GetProperty("allowedTimeWindows")[0].GetProperty("start").GetString());
        Assert.Equal("2030-12-25", saved.GetProperty("excludedDates")[0].GetString());
        Assert.Equal(1, saved.GetProperty("excludedAnnualDates")[0].GetProperty("month").GetInt32());
        Assert.Contains(id, Ids(await node.GetAsync($"{D}/schedule-policies")));
        await node.SendAsync(HttpMethod.Post, $"{D}/schedule-policies", new { name = "Office hours" }, HttpStatusCode.Conflict);

        // Hourly from Saturday 2030-01-05: everything the policy allows is a weekday between 9 and 17.
        var preview = await node.PostAsync($"{D}/schedule-policies/{id}/preview",
            new { expression = "0 0 * * * *", fromUtc = new DateTime(2030, 1, 5, 0, 0, 0, DateTimeKind.Utc), count = 12 });
        Assert.Equal(12, preview.GetArrayLength());
        foreach (var slot in preview.EnumerateArray().Where(x => x.GetProperty("isAllowed").GetBoolean()))
        {
            var at = slot.GetProperty("effectiveAtUtc").GetDateTime();
            Assert.InRange(at.Hour, 9, 17);
            Assert.DoesNotContain(at.DayOfWeek, new[] { DayOfWeek.Saturday, DayOfWeek.Sunday });
        }

        // Monday 2030-01-07 at 10:00 is inside the policy, so the job keeps its time.
        var job = await node.AddTimeTickerAsync(new
        {
            function, executionTime = new DateTime(2030, 1, 7, 10, 0, 0, DateTimeKind.Utc), schedulePolicyName = "Office hours",
        });
        Assert.Equal("Office hours", (await node.GetAsync($"{D}/time-tickers/{job}")).GetProperty("schedulePolicyName").GetString());
        Assert.Equal(1, (await node.GetAsync($"{D}/schedule-policies/{id}/usage")).GetProperty("timeTickerCount").GetInt32());
        await node.SendAsync(HttpMethod.Delete, $"{D}/schedule-policies/{id}?version={version}", null, HttpStatusCode.Conflict);

        var updated = await node.SendAsync(HttpMethod.Put, $"{D}/schedule-policies/{id}", new
        {
            name = "Office hours", description = "edited", timeZoneId = "UTC", allowedWeekdays = "MondayToFriday",
            allowedTimeWindows = new[] { new { start = "08:00:00", end = "18:00:00" } }, version,
        }, HttpStatusCode.OK);
        Assert.Equal("edited", updated.GetProperty("description").GetString());
        var current = updated.GetProperty("version").GetInt64();
        Assert.True(current > version);
        await node.SendAsync(HttpMethod.Put, $"{D}/schedule-policies/{id}",
            new { name = "Office hours", description = "stale", version }, HttpStatusCode.Conflict);
        await node.SendAsync(HttpMethod.Put, $"{D}/schedule-policies/{id}",
            new { name = "Renamed", version = current }, HttpStatusCode.BadRequest);

        await node.SendAsync(HttpMethod.Delete, $"{D}/time-tickers/{job}", null, HttpStatusCode.NoContent);
        Assert.Equal(0, (await node.GetAsync($"{D}/schedule-policies/{id}/usage")).GetProperty("timeTickerCount").GetInt32());
        await node.SendAsync(HttpMethod.Delete, $"{D}/schedule-policies/{id}?version={current}", null, HttpStatusCode.NoContent);
        await node.SendAsync(HttpMethod.Get, $"{D}/schedule-policies/{id}", null, HttpStatusCode.NotFound);
    }

    [DashboardApiFact]
    public async Task Integrations_AreSavedInTheStore_AndRejectStaleOrUnknownChanges()
    {
        await using var node = await StartAsync(_ => { });

        var snapshot = await node.GetAsync($"{D}/integrations");
        Assert.True(snapshot.GetProperty("available").GetBoolean());
        Assert.True(snapshot.GetProperty("canManage").GetBoolean());
        Assert.True(snapshot.GetProperty("persistent").GetBoolean());
        var version = snapshot.GetProperty("configuration").GetProperty("version").GetGuid();

        var destination = Guid.NewGuid();
        var configuration = new
        {
            version,
            destinations = new[]
            {
                new
                {
                    id = destination, name = "Ops webhook", provider = "webhook", enabled = true,
                    url = "https://hooks.example.com/tickerq", headers = new Dictionary<string, string> { ["X-Token"] = "secret" },
                },
            },
            rules = new[]
            {
                new
                {
                    id = Guid.NewGuid(), name = "Failures", enabled = true, destinationIds = new[] { destination },
                    events = new[] { "failed" }, includeFunctions = Array.Empty<string>(), excludeFunctions = Array.Empty<string>(),
                    cooldownSeconds = 60,
                },
            },
            monitors = Array.Empty<object>(),
        };
        await node.SendAsync(HttpMethod.Put, $"{D}/integrations", configuration, HttpStatusCode.NoContent);

        var saved = (await node.GetAsync($"{D}/integrations")).GetProperty("configuration");
        Assert.NotEqual(version, saved.GetProperty("version").GetGuid());
        var savedDestination = saved.GetProperty("destinations")[0];
        Assert.Equal("Ops webhook", savedDestination.GetProperty("name").GetString());
        Assert.Equal("hooks.example.com", savedDestination.GetProperty("endpointHost").GetString());
        Assert.True(savedDestination.GetProperty("hasUrl").GetBoolean());
        Assert.Equal("failed", saved.GetProperty("rules")[0].GetProperty("events")[0].GetString());
        await node.SendAsync(HttpMethod.Put, $"{D}/integrations", configuration, HttpStatusCode.Conflict);

        var preview = await node.PostAsync($"{D}/integrations/preview", new { provider = "slack", messageKind = "job_alert" });
        Assert.False(string.IsNullOrWhiteSpace(preview.GetProperty("payload").GetString()));
        await node.SendAsync(HttpMethod.Post, $"{D}/integrations/destinations/{Guid.NewGuid()}/test", new { },
            HttpStatusCode.BadRequest);
    }

    [DashboardApiFact]
    public async Task InvalidRequests_AreRejected_WithoutTouchingTheStore()
    {
        var function = Unique("valid");
        var typed = Unique("typed");
        await using var node = await StartAsync(s =>
        {
            s.MapTicker(function, (_, _) => Task.CompletedTask);
            s.MapTicker<Order>(typed, (_, _) => Task.CompletedTask);
        });
        var missing = Guid.NewGuid();

        // Request property names are case-sensitive: camelCase would bind to an empty Order.
        var camelCase = await node.SendAsync(HttpMethod.Post, $"{D}/time-tickers",
            new { function = typed, request = "{\"name\":\"widget\",\"quantity\":1}" }, HttpStatusCode.BadRequest);
        Assert.Contains("case-sensitive", camelCase.GetProperty("error").GetString());

        await node.SendAsync(HttpMethod.Post, $"{D}/time-tickers", new { }, HttpStatusCode.BadRequest);
        await node.SendAsync(HttpMethod.Post, $"{D}/time-tickers",
            new { function = Unique("not-registered"), executionTime = DateTime.UtcNow }, HttpStatusCode.BadRequest);
        await node.SendAsync(HttpMethod.Post, $"{D}/time-tickers",
            new { function, missedExecutionAction = "Skip" }, HttpStatusCode.BadRequest);
        await node.SendAsync(HttpMethod.Post, $"{D}/time-tickers/chain",
            new { executionTime = DateTime.UtcNow, root = new { function = "$tickerq.parallel-group" } }, HttpStatusCode.BadRequest);
        await node.SendAsync(HttpMethod.Post, $"{D}/cron-tickers", new { function, expression = "" }, HttpStatusCode.BadRequest);
        await node.SendAsync(HttpMethod.Post, $"{D}/cron-tickers", new { function, expression = "not a cron" }, HttpStatusCode.BadRequest);
        await node.SendAsync(HttpMethod.Post, $"{D}/time-tickers/bulk-delete", new { ids = Array.Empty<Guid>() }, HttpStatusCode.BadRequest);
        await node.SendAsync(HttpMethod.Post, $"{D}/log-tail/query", new { items = Array.Empty<object>() }, HttpStatusCode.BadRequest);
        await node.SendAsync(HttpMethod.Post, $"{D}/schedule-policies", new { name = "" }, HttpStatusCode.BadRequest);
        await node.SendAsync(HttpMethod.Post, $"{D}/definitions",
            new { kind = "Template", target = "TimeTicker", name = "no steps", payload = new { root = new { function } } },
            HttpStatusCode.BadRequest);

        await node.SendAsync(HttpMethod.Get, $"{D}/time-tickers/{missing}", null, HttpStatusCode.NotFound);
        await node.SendAsync(HttpMethod.Patch, $"{D}/time-tickers/{missing}", new { description = "x" }, HttpStatusCode.NotFound);
        await node.SendAsync(HttpMethod.Post, $"{D}/time-tickers/{missing}/run", null, HttpStatusCode.NotFound);
        await node.SendAsync(HttpMethod.Delete, $"{D}/time-tickers/{missing}", null, HttpStatusCode.Conflict);
        await node.SendAsync(HttpMethod.Post, $"{D}/time-tickers/{missing}/cancel", null, HttpStatusCode.NotFound);
        await node.SendAsync(HttpMethod.Get, $"{D}/cron-tickers/{missing}", null, HttpStatusCode.NotFound);
        await node.SendAsync(HttpMethod.Get, $"{D}/cron-occurrences/{missing}", null, HttpStatusCode.NotFound);
        await node.SendAsync(HttpMethod.Post, $"{D}/cron-occurrences/{missing}/run", null, HttpStatusCode.BadRequest);
        await node.SendAsync(HttpMethod.Get, $"{D}/definitions/{missing}", null, HttpStatusCode.NotFound);
        await node.SendAsync(HttpMethod.Get, $"{D}/schedule-policies/{missing}", null, HttpStatusCode.NotFound);

        Assert.Equal(0, (await node.PostAsync($"{D}/time-tickers/query", null)).GetProperty("totalCount").GetInt32());
        Assert.Equal(0, (await node.PostAsync($"{D}/cron-tickers/query", null)).GetProperty("totalCount").GetInt32());
        Assert.Equal(0, (await node.GetAsync($"{D}/definitions")).GetArrayLength());
        Assert.Equal(0, (await node.GetAsync($"{D}/schedule-policies")).GetArrayLength());
    }

    [DashboardApiFact]
    public async Task WithoutALicense_ReadsStillAnswer_ButWritesAreForbidden()
    {
        var function = Unique("unlicensed");
        await using var node = await StartAsync(s => s.MapTicker(function, (_, _) => Task.CompletedTask), activeLicense: false);

        await node.GetAsync($"{Api}/options");
        Assert.False((await node.GetAsync($"{Api}/license")).GetProperty("executionAllowed").GetBoolean());
        var forbidden = await node.SendAsync(HttpMethod.Post, $"{D}/time-tickers",
            new { function, executionTime = DateTime.UtcNow }, HttpStatusCode.Forbidden);
        Assert.False(string.IsNullOrWhiteSpace(forbidden.GetProperty("error").GetString()));
        await node.SendAsync(HttpMethod.Post, $"{D}/cron-tickers", new { function, expression = "* * * * * *" }, HttpStatusCode.Forbidden);
        await node.SendAsync(HttpMethod.Post, $"{D}/schedule-policies", new { name = "blocked" }, HttpStatusCode.Forbidden);

        Assert.Equal(0, (await node.PostAsync($"{D}/time-tickers/query", null)).GetProperty("totalCount").GetInt32());
        Assert.Equal(0, (await node.PostAsync($"{D}/cron-tickers/query", null)).GetProperty("totalCount").GetInt32());
        Assert.Equal(0, (await node.GetAsync($"{D}/schedule-policies")).GetArrayLength());
    }

    private sealed record Order(string Name, int Quantity);

    private sealed record Receipt(string Name, int Total);

    // A job that starts after its due time finishes as DueDone, which is still a success.
    private static readonly string[] Succeeded = ["Done", "DueDone"];

    private static string Unique(string name) => $"api.{name}.{Guid.NewGuid():N}";

    // Functions read their request with TickerQ's request serializer, which defaults to .NET's
    // PascalCase names, so a request is sent in the shape the function will read it in.
    private static string Request<T>(T value) => JsonSerializer.Serialize(value);

    private async Task<ApiNode> StartAsync(Action<IServiceCollection> services, bool activeLicense = true)
    {
        var store = await server.CreateStoreAsync();
        try
        {
            var app = await DashboardApiHost.StartAsync(store, new DashboardApiHostOptions
            {
                ActiveLicense = activeLicense,
                Services = services,
                Scheduler = scheduler => scheduler.FallbackIntervalChecker = TimeSpan.FromSeconds(2),
            });
            return new ApiNode(app, store);
        }
        catch
        {
            await store.DisposeAsync();
            throw;
        }
    }

    private static Guid Single(JsonElement page)
    {
        Assert.Equal(1, page.GetProperty("totalCount").GetInt32());
        return page.GetProperty("items")[0].GetProperty("id").GetGuid();
    }

    private static IEnumerable<Guid> Ids(JsonElement items) => items.EnumerateArray().Select(x => x.GetProperty("id").GetGuid());

    private static List<string> Names(JsonElement items, string property)
        => items.EnumerateArray().Select(x => x.GetProperty(property).GetString()).ToList();

    private static string StatusOf(JsonElement chain, string function)
        => chain.EnumerateArray().Single(x => x.GetProperty("functionName").GetString() == function).GetProperty("status").GetString();

    private static int SucceededCount(JsonElement counts)
        => counts.EnumerateArray().Where(x => Succeeded.Contains(x.GetProperty("status").GetString()))
            .Sum(x => x.GetProperty("count").GetInt32());

    /// <summary>Byte payloads travel as Base64 of their UTF-8 JSON.</summary>
    private static T Decode<T>(JsonElement payload)
    {
        Assert.Equal(JsonValueKind.String, payload.ValueKind);
        return JsonSerializer.Deserialize<T>(Convert.FromBase64String(payload.GetString()!), Json);
    }

    private static async Task WaitUntilAsync(Func<Task<bool>> condition, string what, Func<Task<string>> state = null)
    {
        var deadline = DateTime.UtcNow + Deadline;
        while (!await condition())
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException($"Timed out after {Deadline.TotalSeconds:0}s waiting for {what}." +
                                           (state == null ? "" : $" {await state()}"));
            await Task.Delay(100);
        }
    }

    /// <summary>A running node, its Dashboard client, and the store it owns for the test.</summary>
    private sealed class ApiNode(WebApplication app, DashboardApiStore store) : IAsyncDisposable
    {
        private readonly HttpClient _client = app.GetTestClient();

        public async ValueTask DisposeAsync()
        {
            try
            {
                await app.StopAsync();
                await app.DisposeAsync();
            }
            finally
            {
                await store.DisposeAsync();
            }
        }

        public Task<JsonElement> GetAsync(string path) => SendAsync(HttpMethod.Get, path, null, HttpStatusCode.OK);

        public Task<JsonElement> PostAsync(string path, object body) => SendAsync(HttpMethod.Post, path, body, HttpStatusCode.OK);

        /// <summary>
        /// Sends a request, asserts its status, and returns its JSON body (undefined when it has none).
        /// The body is buffered like a browser's, with a Content-Length, unless <paramref name="streamed"/>.
        /// </summary>
        public async Task<JsonElement> SendAsync(HttpMethod method, string path, object body, HttpStatusCode expected,
            bool streamed = false)
        {
            using var request = new HttpRequestMessage(method, path);
            if (body != null)
                request.Content = streamed
                    ? JsonContent.Create(body, body.GetType(), options: Json)
                    : new StringContent(JsonSerializer.Serialize(body, body.GetType(), Json), Encoding.UTF8, "application/json");
            using var response = await _client.SendAsync(request);
            var text = await response.Content.ReadAsStringAsync();
            Assert.True(expected == response.StatusCode,
                $"{method} {path} returned {(int)response.StatusCode} instead of {(int)expected}: {text}");
            if (string.IsNullOrWhiteSpace(text) || response.Content.Headers.ContentType?.MediaType != "application/json")
                return default;
            return JsonDocument.Parse(text).RootElement.Clone();
        }

        public async Task<Guid> AddTimeTickerAsync(object body)
            => Guid.Parse((await PostAsync($"{D}/time-tickers", body)).GetProperty("id").GetString()!);

        public Task WaitForStatusAsync(Guid id, params string[] statuses)
        {
            string current = null;
            return WaitUntilAsync(async () =>
            {
                current = (await GetAsync($"{D}/time-tickers/{id}")).GetProperty("status").GetString();
                return statuses.Contains(current);
            }, $"time ticker {id} to reach {string.Join(" or ", statuses)}", () => Task.FromResult($"It is {current}."));
        }

        /// <summary>Every occurrence of the cron as status and error, for a timeout's message.</summary>
        public async Task<string> DescribeOccurrencesAsync(Guid cronId)
        {
            var items = (await PostAsync($"{D}/cron-occurrences/{cronId}/query", new { pageSize = 50 })).GetProperty("items");
            return items.GetArrayLength() == 0
                ? $"The cron has no occurrences. Cron: {await GetAsync($"{D}/cron-tickers/{cronId}")}. " +
                  $"Next: {await GetAsync($"{D}/host/next-ticker")}. Host: {await GetAsync($"{D}/host/status")}. " +
                  $"Diagnostics: {await GetAsync($"{D}/diagnostics")}. {SchedulerState()}"
                : "Occurrences: " + string.Join("; ", items.EnumerateArray().Select(x =>
                    $"{x.GetProperty("status").GetString()} at {x.GetProperty("scheduledFor")} {x.GetProperty("exceptionMessage")}"));
        }

        // What the API cannot show: whether runtime activation opened the scheduler, and what it planned.
        private string SchedulerState()
        {
            var gate = app.Services.GetService<TickerQ.Utilities.Interfaces.ITickerQActivationGate>();
            var context = app.Services.GetService<TickerExecutionContext>();
            return $"Activation gate: {(gate == null ? "none" : gate.IsActivated ? "open" : $"closed ({gate.ClosedReason?.Message ?? "no reason"})")}; " +
                   $"next planned: {context?.GetNextPlannedOccurrence()?.ToString("O") ?? "nothing"}.";
        }

        public async Task<int> SucceededOccurrencesAsync(Guid cronId)
            => (await PostAsync($"{D}/cron-occurrences/{cronId}/query", new { statuses = Succeeded })).GetProperty("totalCount").GetInt32();
    }
}
