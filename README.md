<h1 align="center">
  <img src="https://tickerq.net/tickerq-logo.svg" alt="TickerQ Logo" width="140" />
  <br />
  TickerQ
</h1>

<p align="center">
  <strong>The modern job scheduler for .NET</strong><br />
  Source-generated task scheduling with built-in persistence, cron & time-based execution, and real-time monitoring.
</p>

<p align="center">
  <a href="https://www.nuget.org/packages/tickerq"><img src="https://img.shields.io/nuget/dt/tickerq.svg?style=flat-square" alt="NuGet Downloads" /></a>
  <a href="https://www.nuget.org/packages/tickerq"><img src="https://img.shields.io/nuget/vpre/tickerq.svg?style=flat-square" alt="NuGet Version" /></a>
  <a href="https://github.com/Arcenox-co/TickerQ/actions/workflows/build.yml"><img src="https://img.shields.io/github/actions/workflow/status/Arcenox-co/TickerQ/build.yml?branch=main&style=flat-square" alt="Build Status" /></a>
  <a href="https://tickerq.net"><img src="https://img.shields.io/badge/docs-tickerq.net-blue?style=flat-square" alt="Documentation" /></a>
  <a href="https://discord.gg/ZJemWvp9MK"><img src="https://img.shields.io/badge/discord-TickerQ-5865F2?style=flat-square&logo=discord&logoColor=white" alt="Discord" /></a>
  <a href="https://opencollective.com/tickerq"><img src="https://opencollective.com/tickerq/tiers/badge.svg?style=flat-square" alt="OpenCollective" /></a>
</p>

---

## Why TickerQ?

| | |
|---|---|
| **Zero reflection, AOT ready** | Source generators at compile time. No runtime reflection, no magic strings, fully trimmable. |
| **Your database** | EF Core (PostgreSQL, SQL Server, SQLite, MySQL) or Redis. No separate storage. |
| **Real-time dashboard** | Built-in SignalR dashboard. Monitor, inspect, manage — no paid add-ons. |
| **Multi-node** | Redis heartbeats, dead-node cleanup, lock-based coordination. Just add instances. |
| **Explicit, safe setup** | Name the application, set its deployment epoch, decorate a method, then schedule. Minutes, not hours. |

## Features

- **Time & cron scheduling** — one-off and recurring jobs
- **Source-generated** — compile-time function registration for maximum performance
- **Dual persistence** — EF Core (PostgreSQL, SQL Server, SQLite, MySQL) or Redis
- **Live dashboard** — real-time UI with SignalR — [screenshots](https://tickerq.net/features/dashboard.html#dashboard-screenshots)
- **Retry & throttling** — configurable retry policies with backoff
- **Dependency injection** — first-class DI support
- **Multi-node** — distributed coordination via Redis heartbeats and dead-node cleanup
- **Hub** — centralized scheduling across applications via [TickerQ Hub](https://hub.tickerq.net)

## Quick Start

```bash
dotnet add package TickerQ
```

### 1. Register services

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddTickerQ(options =>
{
    // Physical runtime partition. Stable across replicas; use a distinct value per application.
    options.UseDefinedCronApplicationNamespace("orders-api");
    // Keep stable for identical replicas/restarts; increment for each reconciliation-changing deployment.
    options.UseReconciliationEpoch(1);
});

var app = builder.Build();
app.UseTickerQ();
app.Run();
```

The application namespace is not merely a code-defined Cron seed prefix. It physically owns runtime
definitions, occurrences, TimeTickers, results, outboxes, evidence, locks, recovery state, and
maintenance work. Two applications may safely use identical public GUIDs in one backend only when
both configure distinct stable namespaces. A scheduler-enabled host without a namespace is invalid.

Queue-only producers call `DisableBackgroundServices()`. They should still configure the namespace
of the scheduler they feed. They do not activate an epoch, but their writes remain physically
isolated. Omitting the namespace is supported only as an explicit compatibility choice targeting the
single `LegacyGlobal` partition; an owner is never inferred.

```csharp
builder.Services.AddTickerQ(options =>
{
    options.DisableBackgroundServices();
    options.UseDefinedCronApplicationNamespace("orders-api");
    options.AddOperationalStore(store => { /* configure the same backend as the scheduler */ });
});
```

### Adopting a pre-partition runtime store

After proving one application is the sole owner of all namespace-less runtime state, bind the target
namespace and a positive deployment epoch explicitly:

```csharp
options.UseLegacyRuntimePartitionAdoption("orders-api", reconciliationEpoch: 7);
```

Before activation, the provider acquires a store-global adoption lease, rejects a competing or
previously completed different owner, and moves legacy state into the target partition. Repeating the
same owner and epoch is idempotent; interrupted supported-provider adoption is atomic or resumable.
Two applications cannot both claim the legacy partition. Redis Cluster cannot safely move keys
between the legacy and target slots, and MongoDB adoption requires replica-set or mongos transactions.

Upgrading a store that already contains code-defined Cron rows requires an explicit legacy owner.
Shared stores should configure `MapLegacyDefinedCronOwnership(function, ownerNamespace)` consistently
in every application. `AdoptLegacyDefinedCronTickers()` is a catch-all only for a provably
single-application store; it can claim the wrong schedule in a shared store. Unclaimed legacy rows fail
closed. See [Upgrading CronTicker and TimeTicker storage](docs/upgrading-cron-time-tickers.md#explicit-legacy-ownership-migration).

### 2. Create a job

```csharp
using TickerQ.Utilities.Base;

public class MyJobs
{
    [TickerFunction("HelloWorld")]
    public async Task HelloWorld(
        TickerFunctionContext context,
        CancellationToken cancellationToken)
    {
        Console.WriteLine($"Hello from TickerQ! Job ID: {context.Id}");
    }
}
```

### 3. Schedule it

```csharp
public class MyService(ITimeTickerManager<TimeTickerEntity> manager)
{
    public async Task Schedule()
    {
        await manager.AddAsync(new TimeTickerEntity
        {
            Function = "HelloWorld",
            ExecutionTime = DateTime.UtcNow.AddSeconds(10)
        });
    }
}
```

## Typed request contracts

TickerQ can generate a Draft 2020-12 JSON Schema and AOT-safe `JsonTypeInfo<T>` for typed function requests. The same immutable descriptor drives the Dashboard editor, Hub/SDK metadata, scheduling validation, and execution-time drift checks.

```csharp
using System.Text.Json.Serialization;
using TickerQ.Utilities.Base;
using TickerQ.Utilities.Interfaces;

public record OrderCustomer(string Email, string? DisplayName = null);
public record ProcessOrderRequest(
    string OrderId,
    decimal Amount,
    OrderCustomer? Customer = null);

public class ProcessOrderJob : ITickerFunction<ProcessOrderRequest>
{
    public Task ExecuteAsync(
        TickerFunctionContext<ProcessOrderRequest> context,
        CancellationToken cancellationToken = default)
    {
        Console.WriteLine($"Processing {context.Request?.OrderId}");
        return Task.CompletedTask;
    }
}

[JsonSerializable(typeof(ProcessOrderRequest))]
[JsonSerializable(typeof(OrderCustomer))]
internal partial class TickerRequestJsonContext : JsonSerializerContext;
```

Register the interface-based function after building the app:

```csharp
app.MapTicker<ProcessOrderJob>();
```

Schedule through the typed manager overload when possible:

```csharp
await manager.AddAsync<ProcessOrderJob, ProcessOrderRequest>(
    DateTime.UtcNow.AddMinutes(1),
    new ProcessOrderRequest(
        "order-42",
        125.50m,
        new OrderCustomer("buyer@example.com")));
```

Important contract behavior:

- Payloads are validated against the registered schema before any single or batch write. Invalid batches are rejected without partial persistence.
- Contract version and deterministic `sha256:` schema fingerprint are copied into persisted time tickers, cron definitions, and cron occurrences.
- Execution fails before invoking the function when persisted identity differs from the current descriptor. Drift failures are terminal and do not consume retry attempts.
- Rows created by older TickerQ versions with both identity columns null remain executable. A partially populated identity is rejected.
- Request-less functions expose no request contract and the Dashboard omits payload input.
- Remote/AOT functions can validate from the transported schema even when the scheduler does not own a local CLR request type.

See the runnable nested-request/AOT probe in `samples/TickerQ.Sample.Dashboard.ReflectionFree`.

## Packages

| Package | Description |
|---------|------------|
| [`TickerQ`](https://www.nuget.org/packages/TickerQ) | Core scheduler engine |
| [`TickerQ.Utilities`](https://www.nuget.org/packages/TickerQ.Utilities) | Shared types, entities, and interfaces |
| [`TickerQ.EntityFrameworkCore`](https://www.nuget.org/packages/TickerQ.EntityFrameworkCore) | EF Core persistence provider |
| [`TickerQ.Caching.StackExchangeRedis`](https://www.nuget.org/packages/TickerQ.Caching.StackExchangeRedis) | Redis persistence and distributed coordination |
| [`TickerQ.Dashboard`](https://www.nuget.org/packages/TickerQ.Dashboard) | Real-time dashboard UI |
| [`TickerQ.Instrumentation.OpenTelemetry`](https://www.nuget.org/packages/TickerQ.Instrumentation.OpenTelemetry) | OpenTelemetry tracing |
| [`TickerQ.SourceGenerator`](https://www.nuget.org/packages/TickerQ.SourceGenerator) | Compile-time function registration |
| [`TickerQ.SDK`](https://www.nuget.org/packages/TickerQ.SDK) | Remote worker SDK and integration helpers |
| [`TickerQ.RemoteExecutor`](https://www.nuget.org/packages/TickerQ.RemoteExecutor) | Hub registration and remote execution transport |
| [`TickerQ.MongoDB`](https://www.nuget.org/packages/TickerQ.MongoDB) | MongoDB persistence provider |

> **Note:** All packages are versioned together. Always update all packages to the same version.

## Operating TickerQ safely

Running TickerQ across multiple nodes, or exposing the dashboard beyond localhost, has
contracts worth understanding before you deploy:

- **[Reliability & execution contracts](docs/reliability.md)** — at-least-once execution and
  lease fencing, `NodeIdentifier` vs. `ExecutionOwnerId`, provider reliability capabilities,
  cooperative timeout semantics, graceful drain, and run-now/bulk-retry.
- **[CronTicker and TimeTicker storage upgrades](docs/upgrading-cron-time-tickers.md)** —
  schema-first rolling rollout, application-owned EF migrations, legacy chain repair,
  namespaced code-defined cron ownership/retirement, `SeedOwnerNamespace` migration and
  legacy-adoption rollout, provider prerequisites, and rollback limits.
- **[Dashboard security & deployment hardening](docs/security.md)** — explicit CORS
  allow-lists, trusted forwarded proxies, the anonymous-dashboard opt-in, JWT signing keys,
  and sliding token-renewal semantics.
- **[Observability](docs/observability.md)** — OpenTelemetry setup and safe tags, plus the
  bounded failure-webhook drop metric and reason sanitizer.

## TickerQ Hub

Centralized scheduling across applications — [hub.tickerq.net](https://hub.tickerq.net)

## Documentation

Full documentation at **[tickerq.net](https://tickerq.net)** — docs are open-source at [TickerQ-UI](https://github.com/Arcenox-co/TickerQ-UI).

## Sponsors & Backers

Support TickerQ through [OpenCollective](https://opencollective.com/tickerq).

<a href="https://opencollective.com/tickerq"><img src="https://opencollective.com/tickerq/backers.svg?width=890" /></a>

## Contributing

PRs, ideas, and issues are welcome! Please read our [Contributing Guide](CONTRIBUTING.md) and sign the [CLA](CLA.md) before submitting a pull request.

## Contributors

Thanks to all our wonderful contributors! See [CONTRIBUTORS.md](CONTRIBUTORS.md) for details.

<a href="https://github.com/Arcenox-co/TickerQ/graphs/contributors">
  <img src="https://contrib.rocks/image?repo=Arcenox-co/TickerQ" />
</a>

## License

TickerQ's commercial transition begins with functional line **5.x**: version `10.5.0` for .NET 10 and its parallel `9.5.0` / `8.5.0` builds. All current TickerQ package families, including `TickerQ.MongoDB` and `@tickerq/sdk`, are source-available under the [TickerQ Software License Agreement v1.0](LICENSE.md); Community, Evaluation, and paid Commercial licenses are available at [license.tickerq.net](https://license.tickerq.net/pricing).

Every immutable artifact originally released under MIT and/or Apache 2.0 retains the terms included with that artifact. Current distributions preserve required notices for retained contributor portions in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md); those notices do not license TickerQ as a whole. See [LICENSING.md](LICENSING.md) for the complete version boundary.
