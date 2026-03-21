<h1 align="center">
  <img src="https://tickerq.net/tickerq-logo.png" alt="TickerQ Logo" width="140" />
  <br />
  TickerQ
</h1>

<p align="center">
  <strong>Robust. Adaptive. Precise.</strong><br />
  A fast, reflection-free background task scheduler for .NET — built with source generators, EF Core integration, cron + time-based execution, and a real-time dashboard.
</p>

<p align="center">
  <a href="https://www.nuget.org/packages/tickerq"><img src="https://img.shields.io/nuget/dt/tickerq.svg?style=flat-square" alt="NuGet Downloads" /></a>
  <a href="https://www.nuget.org/packages/tickerq"><img src="https://img.shields.io/nuget/vpre/tickerq.svg?style=flat-square" alt="NuGet Version" /></a>
  <a href="https://github.com/Arcenox-co/TickerQ/actions/workflows/build.yml"><img src="https://img.shields.io/github/actions/workflow/status/Arcenox-co/TickerQ/build.yml?branch=main&style=flat-square" alt="Build Status" /></a>
  <a href="https://tickerq.arcenox.com"><img src="https://img.shields.io/badge/docs-tickerq.net-blue?style=flat-square" alt="Documentation" /></a>
  <a href="https://discord.gg/ZJemWvp9MK"><img src="https://img.shields.io/discord/1234567890?style=flat-square&logo=discord&logoColor=white&label=discord&color=5865F2" alt="Discord" /></a>
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
| **Minimal setup** | `AddTickerQ()` → decorate a method → schedule. Minutes, not hours. |

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

builder.Services.AddTickerQ();

var app = builder.Build();
app.UseTickerQ();
app.Run();
```

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

> **Note:** All packages are versioned together. Always update all packages to the same version.

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

Dual licensed under **MIT** and **Apache 2.0** © [Arcenox](https://arcenox.com)
