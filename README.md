<h1 align="center">
  <img src="https://tickerq.net/tickerq-logo.svg" alt="TickerQ" width="140" />
  <br />
  TickerQ
</h1>

<p align="center">
  <strong>Background job scheduling for .NET</strong><br />
  Scheduled tasks, recurring workflows, and real-time visibility in one platform.
</p>

<p align="center">
  <a href="https://www.nuget.org/packages/TickerQ"><img src="https://img.shields.io/nuget/vpre/TickerQ.svg?style=flat-square" alt="NuGet version" /></a>
  <a href="https://www.nuget.org/packages/TickerQ"><img src="https://img.shields.io/nuget/dt/TickerQ.svg?style=flat-square" alt="NuGet downloads" /></a>
  <a href="https://license.tickerq.net"><img src="https://img.shields.io/badge/license-source--available-334155?style=flat-square" alt="Source-available license" /></a>
  <a href="https://discord.gg/ZJemWvp9MK"><img src="https://img.shields.io/badge/discord-TickerQ-5865F2?style=flat-square&logo=discord&logoColor=white" alt="Discord community" /></a>
</p>

<p align="center">
  <a href="https://license.tickerq.net/register"><strong>Start for free</strong></a> ·
  <a href="https://tickerq.net/docs">Documentation</a> ·
  <a href="https://license.tickerq.net/pricing">View plans</a> ·
  <a href="https://github.com/Arcenox-co/TickerQ/releases">Releases</a>
</p>

---

TickerQ helps .NET teams schedule background work, build job workflows, and monitor execution from a single dashboard. It supports everything from scheduled emails and reports to invoice processing and recurring data pipelines, with a choice of storage providers and optional integrations.

## Start for free

**Free for companies with annual consolidated gross revenue below US$5 million.** The Community license includes production use and can be renewed annually at no charge while eligibility is maintained.

Community is also available to individuals working solely on their own behalf, registered nonprofits, and eligible educational institutions, students, educators, and academic researchers for teaching, learning, or noncommercial academic research.

The revenue threshold applies to the entire company group, including parents, subsidiaries, and entities under common control, for the most recently completed fiscal year. For client work, each client must qualify independently or hold a paid license.

Organizations outside Community eligibility can choose a commercial subscription or a one-time **30-day Evaluation** for internal, non-production use. Full eligibility and usage terms are defined in the [license agreement](LICENSE.md).

**[Start for free →](https://license.tickerq.net/register)** · [Compare plans](https://license.tickerq.net/pricing)

## Scheduling and workflows

- **Flexible scheduling** — Run jobs immediately, at a specific time, or on a recurring cron schedule.
- **Job chains** — Connect dependent steps, define execution conditions, and repeat complete workflows on a schedule.
- **Execution controls** — Configure retries, priorities, per-host function concurrency, and cron overlap prevention.
- **Persistent storage** — Choose EF Core, MongoDB, or Redis, with in-memory storage available for development.
- **Transactional scheduling** — Save jobs and application data together in the same EF Core transaction.
- **Distributed execution** — Coordinate multiple application instances through a shared operational store and recover interrupted work.

Functions are registered at compile time through a source generator, with typed request support and Native AOT compatibility in the core scheduler.

## Dashboard

The [TickerQ dashboard](https://tickerq.net/docs/dashboard) provides live execution status, logs, history, and scheduler diagnostics. Inspect individual steps in a chain, retry failed jobs, pause schedules, and request cancellation from the same interface.

An optional **AI assistant** supports natural-language exploration of jobs and logs, along with proposed jobs, schedules, and monitoring rules. Changes require confirmation, and the assistant uses your configured model provider.

## Integrations and monitoring

**TickerQ.Integrations** connects job events to Slack, Microsoft Teams, and custom webhooks. Destinations, function filters, alert rules, and message payloads are managed from the dashboard.

Monitor repeated failures, long-running jobs, and daily completion deadlines, with optional recovery messages when an issue clears. OpenTelemetry instrumentation adds execution tracing to your existing observability tools.

## Packages

TickerQ is modular. Add the storage provider, dashboard, and integrations your application needs.

| Package | Purpose |
| --- | --- |
| [TickerQ](src/TickerQ) | Scheduling and job execution |
| [TickerQ.Dashboard](src/TickerQ.Dashboard) | Management dashboard and optional AI assistant |
| [TickerQ.EntityFrameworkCore](src/TickerQ.EntityFrameworkCore) | Relational storage through EF Core |
| [TickerQ.MongoDB](src/TickerQ.MongoDB) | MongoDB persistence |
| [TickerQ.Caching.StackExchangeRedis](src/TickerQ.Caching.StackExchangeRedis) | Redis persistence, caching, and coordination |
| [TickerQ.Integrations](src/TickerQ.Integrations) | Slack, Teams, webhooks, and job monitoring |
| [TickerQ.Instrumentation.OpenTelemetry](src/TickerQ.Instrumentation.OpenTelemetry) | Execution tracing |

[Utilities](src/TickerQ.Utilities) and the [source generator](src/TickerQ.SourceGenerator) provide shared contracts and compile-time registration. Use matching release versions across the .NET packages.

For centralized scheduling across applications, see [TickerQ Hub](https://hub.tickerq.net), its [.NET SDK](hub/sdks/dotnet/TickerQ.SDK), [JavaScript/TypeScript SDK](hub/sdks/node), and [remote executor](hub/remoteExecutor/TickerQ.RemoteExecutor). Hub is a separate hosted service with its own terms.

## Documentation and support

Installation, configuration, and usage guides are available on the [documentation website](https://tickerq.net/docs).

- **Reference:** [API documentation](https://tickerq.net/docs/api-reference) and [release notes](https://github.com/Arcenox-co/TickerQ/releases).
- **Deployment:** [reliability](docs/reliability.md), [storage upgrades](docs/upgrading-cron-time-tickers.md), and [dashboard security](docs/security.md).
- **Support:** [Discord community](https://discord.gg/ZJemWvp9MK) and [GitHub issues](https://github.com/Arcenox-co/TickerQ/issues).
- **Contributions:** [contributing guide](CONTRIBUTING.md) and [CLA](CLA.md).

Jobs should be safe to execute more than once, since retries and recovery can repeat execution. The reliability guide covers these guarantees and deployment considerations.

## License

Current commercial versions of TickerQ are **source-available** under the [TickerQ Software License Agreement v1.0](LICENSE.md). Community, Evaluation, and paid subscriptions are covered by the same agreement. See the [licensing overview](LICENSING.md) for package coverage, version boundaries, and certificate requirements.

Older releases published under MIT or Apache 2.0 keep their original terms. [Third-party notices](THIRD-PARTY-NOTICES.md) are preserved.

---

Maintained by Arcenox with contributions from the [TickerQ community](https://github.com/Arcenox-co/TickerQ/graphs/contributors). Support ongoing development through [OpenCollective](https://opencollective.com/tickerq).
