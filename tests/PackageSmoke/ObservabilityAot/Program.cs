using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TickerQ.DependencyInjection;
using TickerQ.Instrumentation.OpenTelemetry;
using TickerQ.Integrations.DependencyInjection;
using TickerQ.Utilities;
using TickerQ.Utilities.Base;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Integrations;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Interfaces.Managers;

var licensePath = Environment.GetEnvironmentVariable("TICKERQ_AOT_LICENSE_PATH");
var licensed = !string.IsNullOrWhiteSpace(licensePath) && File.Exists(licensePath);
var activities = new ConcurrentQueue<string>();
using var listener = new ActivityListener
{
    ShouldListenTo = source => source.Name == "TickerQ",
    Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
    SampleUsingParentId = (ref ActivityCreationOptions<string> _) => ActivitySamplingResult.AllData,
    ActivityStopped = activity => activities.Enqueue(activity.OperationName),
};
ActivitySource.AddActivityListener(listener);

var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    Args = [], ContentRootPath = AppContext.BaseDirectory,
});
builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
builder.Services.AddTickerQ(options =>
{
    options.UseApplicationNamespace("installed-observability-aot-smoke");
    options.UseReconciliationEpoch(1);
    if (licensed)
    {
        options.UseLicense(Path.GetFullPath(licensePath!));
        options.ConfigureScheduler(scheduler =>
        {
            scheduler.MaxConcurrency = 1;
            scheduler.MinPollingInterval = TimeSpan.FromMilliseconds(10);
            scheduler.FallbackIntervalChecker = TimeSpan.FromMilliseconds(250);
            scheduler.StaleJobRecoveryEnabled = false;
        });
    }
    else
    {
        options.DisableBackgroundServices();
        options.UseLicense(Path.Combine(AppContext.BaseDirectory, "intentionally-missing.tqlicense"));
    }
    options.AddIntegrations(integrations =>
    {
        integrations.ApplicationName = "Package smoke";
        integrations.EnvironmentName = "NativeAOT";
        integrations.AllowedHosts = ["example.com"];
    });
    options.AddOpenTelemetryInstrumentation();
});
builder.Services.MapTicker<ObservedJob>();

using var host = builder.Build();
host.UseTickerQ();
await host.StartAsync();
try
{
    var admin = host.Services.GetRequiredService<ITickerIntegrationAdminService>();
    var initial = await admin.GetAsync();
    Check(initial.Available && initial.Providers.SequenceEqual(["slack", "teams", "webhook"]),
        "The native integrations service did not expose its built-in providers.");

    var preview = admin.Preview(new IntegrationPreviewRequest
    {
        Provider = "slack", MessageKind = "job_alert",
    });
    Check(preview.Payload.Contains("invoice generation failed", StringComparison.OrdinalIgnoreCase) &&
          preview.DefaultTemplate?.Contains("{{function}}", StringComparison.Ordinal) == true,
        "The native integrations service did not render its built-in preview.");

    var destinationId = Guid.NewGuid();
    await admin.SaveAsync(new IntegrationConfiguration
    {
        Version = initial.Configuration.Version,
        Destinations =
        [
            new IntegrationDestination
            {
                Id = destinationId, Name = "AOT webhook", Provider = "webhook",
                Url = "https://example.com/tickerq", Headers = new() { ["X-Smoke"] = "native" },
            },
        ],
        Rules =
        [
            new IntegrationRule
            {
                Id = Guid.NewGuid(), Name = "AOT failures", DestinationIds = [destinationId],
                Events = ["failed"], IncludeFunctions = ["PackageSmoke.*"],
            },
        ],
    });
    var saved = await admin.GetAsync();
    var redacted = saved.Configuration.Destinations.Single();
    Check(redacted.HasUrl && redacted.HasHeaders && redacted.Url is null && redacted.Headers is null &&
          redacted.EndpointHost == "example.com",
        "The native integrations configuration did not persist and redact its protected settings.");

    if (licensed)
    {
        var manager = host.Services.GetRequiredService<ITimeTickerManager<TimeTickerEntity>>();
        var store = host.Services.GetRequiredService<ITickerPersistenceProvider<TimeTickerEntity, CronTickerEntity>>();
        var scheduled = await manager.AddAsync<ObservedJob>(DateTime.UtcNow);
        Check(scheduled.IsSucceeded, $"Scheduling the observed native job failed: {scheduled.Exception}");
        await WaitForDoneAsync(store, scheduled.Result.Id);
        Check(activities.Contains("tickerq.job.enqueued") &&
              activities.Contains("tickerq.job.completed") &&
              activities.Any(name => name.StartsWith("tickerq.job.execute.", StringComparison.Ordinal)),
            "The installed OpenTelemetry package did not emit scheduler activities in Native AOT.");
    }

    Console.WriteLine("PACKAGE_SMOKE_OBSERVABILITYAOT_OK");
}
finally
{
    await host.StopAsync();
}

static async Task WaitForDoneAsync(
    ITickerPersistenceProvider<TimeTickerEntity, CronTickerEntity> store, Guid id)
{
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
    while (true)
    {
        timeout.Token.ThrowIfCancellationRequested();
        var row = await store.GetTimeTickerById(id, timeout.Token);
        if (row?.Status == TickerStatus.Done) return;
        if (row?.Status is TickerStatus.Failed or TickerStatus.Cancelled or TickerStatus.Skipped)
            throw new InvalidOperationException($"Observed job reached {row.Status}: {row.ExceptionMessage}");
        await Task.Delay(20, timeout.Token);
    }
}

static void Check(bool passed, string message)
{
    if (!passed) throw new InvalidOperationException(message);
}

public sealed class ObservedJob : ITickerFunction
{
    public Task ExecuteAsync(TickerFunctionContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }
}
