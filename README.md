<h1 align="center">
  <img src="https://tickerq.net/tickerq-logo.svg" alt="TickerQ" width="140" />
  <br />
  TickerQ
</h1>

<p align="center">
  <strong>Background jobs and workflows for .NET</strong><br />
  From a scheduled task to a recurring workflow. See every step along the way.
</p>

<p align="center">
  <a href="https://www.nuget.org/packages/TickerQ"><img src="https://img.shields.io/nuget/vpre/TickerQ.svg?style=flat-square" alt="NuGet version" /></a>
  <a href="https://www.nuget.org/packages/TickerQ"><img src="https://img.shields.io/nuget/dt/TickerQ.svg?style=flat-square" alt="NuGet downloads" /></a>
  <a href="https://license.tickerq.net"><img src="https://img.shields.io/badge/license-source--available-334155?style=flat-square" alt="Source-available license" /></a>
  <a href="https://discord.gg/ZJemWvp9MK"><img src="https://img.shields.io/badge/discord-TickerQ-5865F2?style=flat-square&logo=discord&logoColor=white" alt="Discord community" /></a>
</p>

<p align="center">
  <a href="https://license.tickerq.net/register"><strong>Start for free</strong></a> ·
  <a href="https://tickerq.net/docs/5.x">Documentation</a> ·
  <a href="https://tickerq.net/docs/5.x/dashboard/tour">Explore the dashboard</a> ·
  <a href="https://license.tickerq.net/pricing">View plans</a> ·
  <a href="https://github.com/Arcenox-co/TickerQ/releases">Releases</a>
</p>

---

**TickerQ brings scheduling, job chains, and execution visibility into your .NET application.** Run work on demand or on a schedule, coordinate dependent steps, and follow each execution through the dashboard. Use it for invoice processing, scheduled reports, data imports, notifications, and application maintenance.

> **Start for free.** Companies with annual consolidated gross revenue below **US$5 million** qualify for Community, including production use. Other eligible users include individuals, nonprofits, and education. [Check eligibility](#start-for-free) · [Get your license →](https://license.tickerq.net/register)

<p align="center">
  <a href="https://tickerq.net/docs/5.x/dashboard/tour">
    <img src="https://raw.githubusercontent.com/Arcenox-co/TickerQ/main/docs/images/dashboard/overview-2026-10-23.jpg" alt="TickerQ dashboard showing scheduler health, active nodes, execution statistics, recent jobs, and upcoming work" width="1200" />
  </a>
  <br />
  <sub>The current 5.x dashboard running on a two-node documentation demo. Workspace and certificate identifiers are anonymized.</sub>
</p>

## Why TickerQ

- **Write jobs in C#.** Use typed requests and compile-time function registration. The core scheduler and dashboard support [Native AOT](docs/native-aot.md); the AI assistant remains a managed-runtime feature.
- **Build complete workflows.** Connect jobs with conditional steps, pass results between them, and schedule a fresh chain for every recurring run.
- **Keep execution under control.** Set retries, priorities, per-host function concurrency, and cron overlap policies. Cancel pending work or request cooperative cancellation of a running job.
- **Commit jobs with business data.** Stage time jobs and cron schedules in the same EF Core transaction as your application changes.
- **Choose your storage.** Use EF Core, MongoDB, or Redis for persistence, with in-memory storage for development.
- **Run across application instances.** Coordinate workers through a shared operational store, isolate workloads with application namespaces, and recover interrupted work.

For example, a daily invoice workflow can **prepare invoices → generate PDFs → send them to customers**, with retries and execution history for each step. [Explore recurring chains →](https://tickerq.net/docs/5.x/guides/recurring-chains)

## A dashboard for daily operations

Understand what is scheduled, what is running, and what needs attention from the same interface.

- **Inspect each run:** live status, execution history, logs, results, and a visual view of job chains.
- **Manage scheduled work:** create and edit jobs, trigger a run, pause cron schedules, and retry or cancel executions.
- **Diagnose runtime issues:** review scheduler health, execution ownership, registered functions, and retention settings.

The optional **AI assistant** helps you explore jobs and logs in natural language and prepare jobs, schedules, or monitoring rules. It uses your configured model provider; you review and confirm changes before they are applied.

### Explore the interface

Select a view to expand its current dashboard screenshot.

<details>
<summary><strong>Workflow execution</strong> — follow parallel branches, conditions, outcomes, progress, and status</summary>

<p align="center">
  <a href="https://tickerq.net/docs/5.x/guides/job-chaining">
    <img src="https://raw.githubusercontent.com/Arcenox-co/TickerQ/main/docs/images/dashboard/workflow.jpg" alt="TickerQ workflow flowchart with a parallel group and conditional outcome jobs" width="1200" />
  </a>
</p>

</details>

<details>
<summary><strong>Visual workflow builder</strong> — create grouped branches and outcome steps without losing the job graph</summary>

<p align="center">
  <a href="https://tickerq.net/docs/5.x/dashboard/saved-work">
    <img src="https://raw.githubusercontent.com/Arcenox-co/TickerQ/main/docs/images/dashboard/workflow-builder.jpg" alt="TickerQ visual workflow builder with grouped parallel jobs and final outcome steps" width="1200" />
  </a>
</p>

</details>

<details>
<summary><strong>Execution details and logs</strong> — inspect lifecycle messages, retries, progress, errors, and application source frames</summary>

<p align="center">
  <a href="https://tickerq.net/docs/5.x/guides/execution-logs">
    <img src="https://raw.githubusercontent.com/Arcenox-co/TickerQ/main/docs/images/dashboard/execution-logs.jpg" alt="TickerQ execution details with database-backed lifecycle logs and application failure evidence" width="1200" />
  </a>
</p>

</details>

<details>
<summary><strong>Drafts and templates</strong> — keep unfinished work non-executable and reuse complete chain designs</summary>

<p align="center">
  <a href="https://tickerq.net/docs/5.x/dashboard/saved-work">
    <img src="https://raw.githubusercontent.com/Arcenox-co/TickerQ/main/docs/images/dashboard/saved-work.jpg" alt="TickerQ Saved Work page with a one-time draft and reusable chain templates" width="1200" />
  </a>
</p>

</details>

<details>
<summary><strong>Cluster nodes</strong> — compare replica health, capacity, queues, versions, and registered functions</summary>

<p align="center">
  <a href="https://tickerq.net/docs/5.x/dashboard/clustering">
    <img src="https://raw.githubusercontent.com/Arcenox-co/TickerQ/main/docs/images/dashboard/nodes.jpg" alt="TickerQ Nodes page showing active replicas, execution capacity, and an offline node" width="1200" />
  </a>
</p>

</details>

<details>
<summary><strong>AI Operations</strong> — review diagnoses, repeated failure patterns, recoveries, and proposed actions</summary>

<p align="center">
  <a href="https://tickerq.net/docs/5.x/dashboard/ai-assistant">
    <img src="https://raw.githubusercontent.com/Arcenox-co/TickerQ/main/docs/images/dashboard/ai-operations.jpg" alt="TickerQ AI Operations overview with investigations, recovery signals, and recent failures" width="1200" />
  </a>
</p>

</details>

<details>
<summary><strong>Integrations and monitoring</strong> — configure Slack, Teams, webhooks, alert rules, and monitors</summary>

<p align="center">
  <a href="https://tickerq.net/docs/5.x/integrations">
    <img src="https://raw.githubusercontent.com/Arcenox-co/TickerQ/main/docs/images/dashboard/integrations.jpg" alt="TickerQ Integrations page with Slack and custom webhook connections" width="1200" />
  </a>
</p>

</details>

## Alerts, deadlines, and recovery

Connect **Slack**, **Microsoft Teams**, or a **custom webhook** with `TickerQ.Integrations`. Choose which functions to monitor and configure destinations, alert rules, and message payloads from the dashboard.

Set a daily completion deadline, detect repeated failures, or flag jobs that run too long. Enable recovery messages when a monitored issue clears. OpenTelemetry instrumentation connects execution traces to your existing observability tools.

[Explore integrations and monitoring →](https://tickerq.net/docs/5.x/integrations)

## Packages

Add the packages your application needs. Installation and configuration are covered in the [documentation](https://tickerq.net/docs/5.x).

| Package | Purpose |
| --- | --- |
| [TickerQ](src/TickerQ) | Scheduling and job execution |
| [TickerQ.Dashboard](src/TickerQ.Dashboard) | Management dashboard and optional AI assistant |
| [TickerQ.EntityFrameworkCore](src/TickerQ.EntityFrameworkCore) | Relational storage through EF Core |
| [TickerQ.MongoDB](src/TickerQ.MongoDB) | MongoDB persistence |
| [TickerQ.Caching.StackExchangeRedis](src/TickerQ.Caching.StackExchangeRedis) | Redis persistence, caching, and coordination |
| [TickerQ.Integrations](src/TickerQ.Integrations) | Slack, Teams, webhooks, and job monitoring |
| [TickerQ.Instrumentation.OpenTelemetry](src/TickerQ.Instrumentation.OpenTelemetry) | Execution tracing |

[Utilities](src/TickerQ.Utilities) and the [source generator](src/TickerQ.SourceGenerator) provide shared contracts and compile-time registration. Keep .NET packages on matching release versions.

For centralized scheduling across applications, [TickerQ Hub](https://hub.tickerq.net) provides [.NET](hub/sdks/dotnet/TickerQ.SDK) and [JavaScript/TypeScript](hub/sdks/node) SDKs with [remote execution](hub/remoteExecutor/TickerQ.RemoteExecutor). Hub is a separate hosted service with its own terms.

## Start for free

| License | Who it is for | Use |
| --- | --- | --- |
| **Community** | Companies below US$5M annual consolidated gross revenue and other eligible users listed below | Free, including production use; annual renewal while eligible |
| **Evaluation** | Organizations evaluating TickerQ | One-time 30-day evaluation for internal, non-production use |
| **Commercial subscription** | Organizations outside Community eligibility, including companies at or above US$5M | Paid production use within the agreed scope |

<details>
<summary><strong>Community eligibility</strong></summary>

- **Companies:** annual worldwide gross revenue below US$5 million across the entire control group, including parents, subsidiaries, and entities under common control, for the most recently completed fiscal year.
- **Individuals:** using TickerQ solely on their own behalf, not for an employer or client.
- **Nonprofits:** validly registered nonprofit organizations.
- **Education:** eligible institutions, students, educators, and academic researchers using TickerQ primarily for teaching, learning, or noncommercial academic research.

For client work, the client must qualify independently or hold a paid license. Community certificates last 12 months and require renewed eligibility confirmation before expiry. The [license agreement](LICENSE.md) defines the full eligibility and usage terms.

</details>

**[Get your free Community license →](https://license.tickerq.net/register)** · [Compare commercial plans](https://license.tickerq.net/pricing)

## Documentation and support

Installation, configuration, and usage guides are available on the [documentation website](https://tickerq.net/docs/5.x).

- **Reference:** [Manager API](https://tickerq.net/docs/5.x/guides/managers) and [release notes](https://tickerq.net/docs/5.x/changelog).
- **Deployment:** [reliability](https://tickerq.net/docs/5.x/guides/reliability), [storage upgrades](https://tickerq.net/docs/5.x/guides/storage-upgrade), and [dashboard security](https://tickerq.net/docs/5.x/dashboard/authentication).
- **Support:** [Discord community](https://discord.gg/ZJemWvp9MK) and [GitHub issues](https://github.com/Arcenox-co/TickerQ/issues).
- **Contributions:** [contributing guide](CONTRIBUTING.md) and [CLA](CLA.md).

Jobs should be safe to execute more than once, since retries and recovery can repeat execution. The reliability guide covers these guarantees and deployment considerations.

## License

Current commercial versions of TickerQ are **source-available** under the [TickerQ Software License Agreement v1.0](LICENSE.md). Community, Evaluation, and paid subscriptions are covered by the same agreement. See the [licensing overview](LICENSING.md) for package coverage, version boundaries, and certificate requirements.

Older releases published under MIT or Apache 2.0 keep their original terms. [Third-party notices](THIRD-PARTY-NOTICES.md) are preserved.

---

Maintained by Arcenox with contributions from the [TickerQ community](https://github.com/Arcenox-co/TickerQ/graphs/contributors). Support ongoing development through [OpenCollective](https://opencollective.com/tickerq).
