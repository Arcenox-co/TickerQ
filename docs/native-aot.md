# Native AOT

TickerQ's core scheduler, in-memory store, dashboard, Redis operational provider,
integrations, and OpenTelemetry instrumentation can run in a Native AOT application.
The release check publishes the installed packages and executes standalone native
applications instead of validating only project builds.

Use the slim ASP.NET host and disable reflection-based JSON defaults in the application:

```xml
<PropertyGroup>
  <PublishAot>true</PublishAot>
  <JsonSerializerIsReflectionEnabledByDefault>false</JsonSerializerIsReflectionEnabledByDefault>
</PropertyGroup>
```

```csharp
var builder = WebApplication.CreateSlimBuilder(args);

builder.Services.AddTickerQ(options =>
{
    options.UseApplicationNamespace("orders-production");
    options.UseReconciliationEpoch(1);
    options.WithJsonContext(JobJsonContext.Default);
    options.AddDashboard(dashboard =>
        dashboard.AllowAnonymousDashboard().SetReadOnly());
});
```

Every request and declared result type must be included in a source-generated JSON
context supplied through `WithJsonContext`. Both supported registration styles work in
Native AOT:

```csharp
builder.Services.MapTicker<CleanupJob>();
builder.Services.MapTicker<ProcessOrderJob, OrderRequest>();

public sealed class CleanupJob : ITickerFunction
{
    public Task ExecuteAsync(
        TickerFunctionContext context,
        CancellationToken cancellationToken = default) => Task.CompletedTask;
}

public sealed record OrderRequest(string OrderId);

[JsonSerializable(typeof(OrderRequest))]
internal partial class JobJsonContext : JsonSerializerContext;
```

When Redis is the operational provider, give the same source-generated context to Redis
so it can serialize custom ticker and result types without reflection:

```csharp
options.AddStackExchangeRedis(redis =>
{
    redis.Configuration = connectionString;
    redis.JsonSerializerContext = JobJsonContext.Default;
});
```

Methods decorated with `[TickerFunction]` are registered by the source generator and
are supported as well. `ITickerFunction` is a convenient default because constructor
dependencies and function boundaries remain explicit.

## Current support boundary

The verified Native AOT lanes cover:

- in-memory scheduling, typed payloads, time chains, cron occurrences, cooperative
  cancellation, and dashboard reads;
- Redis scheduling, typed payloads, time chains, point reads, and paginated dashboard
  queries against a real Redis server;
- encrypted integration settings, redaction, Slack payload preview, and the scheduler's
  integration event path without sending an outbound webhook; and
- OpenTelemetry enqueue, execute, and completion activities.

The AI assistant remains unavailable in Native AOT because its runtime tool discovery is
dynamic. `AddAssistant` fails clearly during configuration in a native application.

`TickerQ.EntityFrameworkCore` and `TickerQ.MongoDB` are supported on the managed .NET
runtime only. EF Core's default runtime model construction is not available after Native
AOT publishing, and the current MongoDB driver constructs serializers through runtime
reflection. TickerQ does not hide those constraints with broad preservation rules because
that would produce a larger binary without making the provider paths reliably native-safe.

The reference application is
[`samples/TickerQ.Sample.Dashboard.ReflectionFree`](../samples/TickerQ.Sample.Dashboard.ReflectionFree).
Release package verification is described in [release-validation.md](release-validation.md).
