<h1 align="center">
  <img src="https://tickerq.net/tickerq-logo.svg" alt="TickerQ" width="140" />
  <br />
  TickerQ
</h1>

<p align="center">
  <strong>Background jobs, recurring schedules, and workflows for .NET.</strong><br />
  Source-generated execution. Flexible persistence. Real-time visibility.
</p>

<p align="center">
  <a href="https://www.nuget.org/packages/TickerQ"><img src="https://img.shields.io/nuget/vpre/TickerQ.svg?style=flat-square" alt="NuGet version" /></a>
  <a href="https://www.nuget.org/packages/TickerQ"><img src="https://img.shields.io/nuget/dt/TickerQ.svg?style=flat-square" alt="NuGet downloads" /></a>
  <a href="https://tickerq.net/docs"><img src="https://img.shields.io/badge/docs-tickerq.net-blue?style=flat-square" alt="Documentation" /></a>
  <a href="https://license.tickerq.net"><img src="https://img.shields.io/badge/license-source--available-334155?style=flat-square" alt="Source-available license" /></a>
  <a href="https://discord.gg/ZJemWvp9MK"><img src="https://img.shields.io/badge/discord-TickerQ-5865F2?style=flat-square&logo=discord&logoColor=white" alt="Discord community" /></a>
</p>

<p align="center">
  <a href="https://tickerq.net/docs">Documentation</a> ·
  <a href="https://tickerq.net/docs/dashboard">Dashboard</a> ·
  <a href="https://license.tickerq.net/pricing">Plans &amp; licensing</a> ·
  <a href="https://github.com/Arcenox-co/TickerQ/releases">Releases</a>
</p>

---

TickerQ brings background processing into your .NET application, from a single scheduled task to recurring workflows with dependent steps. It combines source-generated function registration, typed job requests, persistent execution state, and an embedded dashboard for managing jobs throughout their lifecycle.

Use it for invoice processing, scheduled reports, data synchronization, maintenance tasks, and other application workloads. Choose the persistence provider and optional packages that fit your application.

## Capabilities

| Area | What TickerQ provides |
| --- | --- |
| **Scheduling** | One-time jobs and recurring cron schedules, with time-zone support and manual execution. |
| **Workflows** | Parent-child job chains, conditional execution, and recurring chains that create a fresh workflow for each occurrence. |
| **Execution control** | Job priorities, per-function concurrency limits within each host, configurable retries, cron overlap policies, pending-job cancellation, and cooperative cancellation for running jobs. |
| **Persistence** | EF Core, MongoDB, and Redis providers, plus in-memory storage for development. |
| **Transactional scheduling** | Stage time jobs and cron schedules alongside business changes in the same EF Core application transaction. |
| **Distributed operation** | Application namespaces, execution ownership, lease renewal, and recovery across scheduler instances sharing an operational store. |
| **Typed jobs** | Source-generated registration, typed request contracts, payload validation, and Native AOT support in the core scheduler. |
| **Observability** | Execution history, optional persisted logs with retention controls, runtime diagnostics, and OpenTelemetry instrumentation. |

TickerQ uses **at-least-once execution**: retries and recovery can repeat work, so job handlers should tolerate repeated execution. Provider capabilities and deployment requirements are described in the [reliability guide](docs/reliability.md).

## Dashboard

The embedded dashboard gives developers and operators a shared view of scheduled work and its execution history.

- **Inspect executions:** follow live status changes, view logs and results, and explore individual jobs within a chain.
- **Manage schedules:** create and edit jobs, run work on demand, pause cron schedules, and retry or cancel executions with confirmation.
- **Understand the runtime:** inspect scheduler health, registered functions, execution ownership, and retention settings.
- **Use the optional AI assistant:** explore jobs, logs, and diagnostics in natural language, then review and confirm proposed changes using your configured model provider.

Explore the [dashboard documentation](https://tickerq.net/docs/dashboard).

## Integrations and monitoring

The optional **TickerQ.Integrations** package connects job operations to **Slack**, **Microsoft Teams**, and **custom webhooks**. Manage destinations, function filters, alert rules, and payload templates from the dashboard.

Monitor functions for daily completion deadlines, repeated failures, and long-running executions. Send incident alerts and optional recovery messages when the monitored condition clears. Customize job-alert, monitoring-alert, and recovery payloads for each destination.

## Packages

TickerQ is organized into focused packages. The links below open their source projects; installation and configuration are covered in the [documentation](https://tickerq.net/docs).

| Package | Purpose |
| --- | --- |
| [TickerQ](src/TickerQ) | Core scheduling and execution engine. |
| [TickerQ.Utilities](src/TickerQ.Utilities) | Shared entities, interfaces, and request contracts. |
| [TickerQ.SourceGenerator](src/TickerQ.SourceGenerator) | Compile-time function registration and request metadata. |
| [TickerQ.EntityFrameworkCore](src/TickerQ.EntityFrameworkCore) | Relational persistence and transactional scheduling with EF Core. |
| [TickerQ.MongoDB](src/TickerQ.MongoDB) | MongoDB persistence. |
| [TickerQ.Caching.StackExchangeRedis](src/TickerQ.Caching.StackExchangeRedis) | Redis persistence, caching, and distributed coordination. |
| [TickerQ.Dashboard](src/TickerQ.Dashboard) | Embedded dashboard, diagnostics, and optional AI assistance. |
| [TickerQ.Integrations](src/TickerQ.Integrations) | Slack, Teams, custom webhooks, and function monitoring. |
| [TickerQ.Instrumentation.OpenTelemetry](src/TickerQ.Instrumentation.OpenTelemetry) | OpenTelemetry instrumentation for job execution. |
| [TickerQ.SDK](hub/sdks/dotnet/TickerQ.SDK) | .NET client and integration support for TickerQ Hub. |
| [TickerQ.RemoteExecutor](hub/remoteExecutor/TickerQ.RemoteExecutor) | Hub registration and remote execution transport. |
| [@tickerq/sdk](hub/sdks/node) | JavaScript and TypeScript client for TickerQ Hub. |

Keep the .NET packages on matching release versions for your target framework. The JavaScript and TypeScript SDK follows its own version line.

## Documentation

Visit **[tickerq.net/docs](https://tickerq.net/docs)** for installation, configuration, guides, and the [API reference](https://tickerq.net/docs/api-reference). Consult the [release notes](https://github.com/Arcenox-co/TickerQ/releases) when upgrading.

For this source branch, the repository also maintains detailed [runtime and reliability notes](docs/reliability.md), [storage upgrade guidance](docs/upgrading-cron-time-tickers.md), and [dashboard security guidance](docs/security.md).

## TickerQ Hub

[TickerQ Hub](https://hub.tickerq.net) provides centralized scheduling across applications, with .NET and JavaScript/TypeScript clients and remote execution integration. The hosted service is governed by separate hosted-service terms.

## Licensing

Current commercial versions are **source-available** under one [TickerQ Software License Agreement v1.0](LICENSE.md). **Community**, **30-day Evaluation**, and **paid Commercial Subscription** are entitlement options under that agreement. Obtain the appropriate license certificate through the [licensing portal](https://license.tickerq.net/pricing). Community is subject to eligibility; Evaluation is limited to non-production use.

The .NET commercial transition begins with functional line **5.x**: `10.5.0` for .NET 10 and its parallel `9.5.0` / `8.5.0` builds. The `@tickerq/sdk` commercial line begins at `1.0.0`. See [LICENSING.md](LICENSING.md) for package coverage, version boundaries, and certificate requirements.

Artifacts previously released under MIT and/or Apache 2.0 retain their original terms. Required notices for retained third-party material are preserved in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

## Community and support

- **Bugs and feature requests:** open a [GitHub issue](https://github.com/Arcenox-co/TickerQ/issues) with the relevant details.
- **Questions and discussion:** join the [TickerQ Discord community](https://discord.gg/ZJemWvp9MK).
- **Contributions:** read the [contributing guide](CONTRIBUTING.md) and [Contributor License Agreement](CLA.md) before submitting a pull request.
- **Project support:** support development through [OpenCollective](https://opencollective.com/tickerq).

TickerQ is maintained by Arcenox with contributions from the [TickerQ community](https://github.com/Arcenox-co/TickerQ/graphs/contributors).
