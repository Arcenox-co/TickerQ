# Native AOT

TickerQ's core scheduler, in-memory store, and dashboard can run in a Native AOT
application. The release check publishes installed `TickerQ`, `TickerQ.Utilities`, and
`TickerQ.Dashboard` packages, starts the native dashboard host, invokes interface-based
jobs through their generated registrations, calls a dashboard API endpoint, serves the
embedded SPA, enforces a protected write, and negotiates a SignalR connection.

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

Methods decorated with `[TickerFunction]` are registered by the source generator and
are supported as well. `ITickerFunction` is a convenient default because constructor
dependencies and function boundaries remain explicit.

## Current support boundary

The verified Native AOT lane covers the core scheduler, the in-memory provider, typed
payload metadata, both function registration styles, and the dashboard with the AI
assistant disabled. `AddAssistant` uses runtime tool discovery and fails clearly when
called from a Native AOT application.

EF Core, MongoDB, Redis, integrations, and OpenTelemetry packages keep their separate
managed-runtime validation. Do not assume a provider is Native AOT-supported merely
because the core application publishes. Add a native runtime test for the exact provider
and database configuration before deploying that combination.

The reference application is
[`samples/TickerQ.Sample.Dashboard.ReflectionFree`](../samples/TickerQ.Sample.Dashboard.ReflectionFree).
Release package verification is described in [release-validation.md](release-validation.md).
