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
public sealed record OrderResult(string OrderId, bool Processed);

[JsonSerializable(typeof(OrderRequest))]
[JsonSerializable(typeof(OrderResult))]
internal partial class JobJsonContext : JsonSerializerContext;
```

`WithJsonContext` is optional in an ordinary managed .NET application. When reflection
serialization is enabled, TickerQ resolves request metadata through
`DefaultJsonTypeInfoResolver`. It becomes required when Native AOT or application settings
disable reflection-based JSON metadata. TickerQ resolves every registered typed function
during startup and fails with a `No JsonTypeInfo was available` error when the configured
context is missing a request or declared result type; the failure is not deferred until the
first job executes.

If the application already owns several generated contexts, combine them instead of creating
a duplicate context:

```csharp
using System.Text.Json.Serialization.Metadata;

options.WithJsonContext(JsonTypeInfoResolver.Combine(
    OrdersJsonContext.Default,
    BillingJsonContext.Default));
```

Requestless functions require no application JSON entry. Adding a generated context to a
managed application is supported, but it is an optimization/compatibility choice rather than
a normal-runtime requirement.

Use the `JsonTypeInfo<T>` result APIs in trimmed or native applications:

```csharp
context.SetResult(result, JobJsonContext.Default.OrderResult);
var parentResult = context.GetParentResult(JobJsonContext.Default.OrderResult);
```

The convenience `SetResult(value)` and `GetParentResult<T>()` overloads use reflection
metadata and are not Native-AOT-safe.

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

- in-memory scheduling, typed payloads, time chains, cron occurrences, time-zone-aware
  schedule policies, cooperative cancellation, and dashboard reads/writes;
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
