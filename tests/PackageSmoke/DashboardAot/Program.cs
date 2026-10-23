using System.Text.Json.Serialization;
using TickerQ.Dashboard.DependencyInjection;
using TickerQ.DependencyInjection;
using TickerQ.Utilities;
using TickerQ.Utilities.Base;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Interfaces.Managers;
using TickerQ.Utilities.Models;

var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
{
    Args = [],
    ContentRootPath = AppContext.BaseDirectory,
});
builder.WebHost.UseUrls("http://127.0.0.1:0");
builder.Services.AddSingleton<InvocationProbe>();
builder.Services.AddTickerQ(options =>
{
    options.UseApplicationNamespace("installed-dashboard-aot-smoke");
    options.UseReconciliationEpoch(1);
    options.DisableBackgroundServices();
    options.UseLicense(Path.Combine(AppContext.BaseDirectory, "intentionally-missing.tqlicense"));
    options.WithJsonContext(SmokeJsonContext.Default);
    options.AddScheduleDateProvider<SmokeHolidayProvider>();
    options.AddDashboard(dashboard => dashboard
        .AllowAnonymousDashboard()
        .SetReadOnly()
        .ConfigureSignalR(signalR => signalR.Services.AddSingleton<SignalRConfigurationProbe>()));
});
builder.Services.MapTicker<InterfaceJob>();
builder.Services.MapTicker<TypedInterfaceJob, SmokeRequest>();

await using var app = builder.Build();
app.UseTickerQ();
await app.StartAsync();
try
{
    _ = app.Services.GetRequiredService<ITimeTickerManager<TimeTickerEntity>>();
    _ = app.Services.GetRequiredService<SignalRConfigurationProbe>();
    Check(TickerFunctionProvider.IsBuilt, "Dashboard host did not publish the function registry.");
    Check(TickerFunctionProvider.TickerFunctions.ContainsKey(nameof(InterfaceJob)),
        "Request-less ITickerFunction registration was trimmed.");
    Check(TickerFunctionProvider.TickerFunctions.ContainsKey(nameof(TypedInterfaceJob)),
        "Typed ITickerFunction registration was trimmed.");

    var policy = await app.Services.GetRequiredService<ISchedulePolicyManager>().UpsertAsync(
        SchedulePolicy.Create("Native business hours")
            .InTimeZone("UTC")
            .AllowWeekdays(DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday,
                DayOfWeek.Thursday, DayOfWeek.Friday)
            .AllowBetween(new TimeOnly(9, 0), new TimeOnly(17, 0))
            .Build());
    Check(policy.Version == 1, "Native schedule-policy persistence did not create version one.");

    await TickerFunctionProvider.TickerFunctions[nameof(InterfaceJob)].Delegate(
        CancellationToken.None, app.Services, new TickerFunctionContext());
    Check(app.Services.GetRequiredService<InvocationProbe>().Calls == 1,
        "Native registration did not preserve the job's public constructor for DI.");

    var requestInfo = TickerFunctionProvider.GetRequestTypeInfo<SmokeRequest>(nameof(TypedInterfaceJob));
    var payload = TickerHelper.CreateTickerRequest(new SmokeRequest("native-dashboard", 7), requestInfo);
    Check(TickerHelper.ReadTickerRequest(payload, requestInfo) is { Name: "native-dashboard", Count: 7 },
        "Typed interface request metadata failed under reflection-free JSON.");

    var address = app.Urls.Single(url => url.StartsWith("http://127.0.0.1:", StringComparison.Ordinal));
    using var client = new HttpClient { BaseAddress = new Uri(address) };
    using var response = await client.GetAsync("/tickerq/dashboard/api/options");
    var responseBody = await response.Content.ReadAsStringAsync();
    Check(response.IsSuccessStatusCode, $"Dashboard options endpoint returned {(int)response.StatusCode}: {responseBody}");
    Check(responseBody.Contains("\"maxConcurrency\"", StringComparison.Ordinal),
        "Dashboard options response did not use the generated response contract.");

    using var policiesResponse = await client.GetAsync("/tickerq/dashboard/api/dashboard/schedule-policies");
    var policiesBody = await policiesResponse.Content.ReadAsStringAsync();
    Check(policiesResponse.IsSuccessStatusCode &&
          policiesBody.Contains("Native business hours", StringComparison.Ordinal),
        $"Native schedule-policy endpoint returned {(int)policiesResponse.StatusCode}: {policiesBody}");

    using var providersResponse = await client.GetAsync(
        "/tickerq/dashboard/api/dashboard/schedule-policies/date-providers");
    var providersBody = await providersResponse.Content.ReadAsStringAsync();
    Check(providersResponse.IsSuccessStatusCode &&
          providersBody.Contains("Smoke holidays", StringComparison.Ordinal),
        $"Native schedule-date-provider endpoint returned {(int)providersResponse.StatusCode}: {providersBody}");

    using var blockedWrite = await client.PostAsync("/tickerq/dashboard/api/dashboard/host/start", content: null);
    Check(blockedWrite.StatusCode == System.Net.HttpStatusCode.Forbidden,
        $"Missing-license dashboard mutation returned {(int)blockedWrite.StatusCode} instead of 403.");

    using var shellResponse = await client.GetAsync("/tickerq/dashboard/");
    var shell = await shellResponse.Content.ReadAsStringAsync();
    Check(shellResponse.IsSuccessStatusCode && shell.Contains("<div id=\"root\"></div>", StringComparison.Ordinal),
        "Native dashboard did not serve its embedded SPA shell.");

    using var negotiateResponse = await client.PostAsync(
        "/tickerq/dashboard/tickerq-notification-hub/negotiate?negotiateVersion=1",
        content: null);
    var negotiateBody = await negotiateResponse.Content.ReadAsStringAsync();
    Check(negotiateResponse.IsSuccessStatusCode &&
          negotiateBody.Contains("\"connectionId\"", StringComparison.Ordinal),
        $"Native dashboard SignalR negotiation returned {(int)negotiateResponse.StatusCode}: {negotiateBody}");

    Console.WriteLine("PACKAGE_SMOKE_DASHBOARDAOT_OK");
}
finally
{
    await app.StopAsync();
}

static void Check(bool passed, string message)
{
    if (!passed) throw new InvalidOperationException(message);
}

public sealed class InvocationProbe
{
    public int Calls { get; set; }
}

public sealed class SignalRConfigurationProbe { }

public sealed class SmokeHolidayProvider : IScheduleDateProvider
{
    public string Name => "Smoke holidays";
    public bool IsExcluded(DateOnly localDate) => localDate is { Month: 1, Day: 1 };
}

public sealed class InterfaceJob(InvocationProbe probe) : ITickerFunction
{
    public Task ExecuteAsync(TickerFunctionContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        probe.Calls++;
        return Task.CompletedTask;
    }
}

public sealed class TypedInterfaceJob : ITickerFunction<SmokeRequest>
{
    public Task ExecuteAsync(TickerFunctionContext<SmokeRequest> context, CancellationToken cancellationToken)
        => Task.CompletedTask;
}

public sealed record SmokeRequest(string Name, int Count);

[JsonSerializable(typeof(SmokeRequest))]
internal partial class SmokeJsonContext : JsonSerializerContext;
