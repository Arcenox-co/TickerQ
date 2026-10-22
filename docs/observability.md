# Observability

TickerQ emits OpenTelemetry traces for job execution and a bounded, non-blocking failure
webhook. This page covers safe tracing setup and the webhook overload/redaction contract.

## OpenTelemetry tracing

Install the instrumentation package and wire it in two places — the OpenTelemetry
`TracerProvider` (to listen to TickerQ's `ActivitySource`) and the TickerQ builder (to emit
the activities):

```bash
dotnet add package TickerQ.Instrumentation.OpenTelemetry
```

```csharp
using TickerQ.Instrumentation.OpenTelemetry;
using OpenTelemetry.Trace;

builder.Services.AddOpenTelemetry()
    .WithTracing(tracing =>
    {
        tracing.AddTickerQInstrumentation() // listens to the "TickerQ" ActivitySource
               .AddOtlpExporter();
    });

builder.Services.AddTickerQ(options =>
{
    options.AddOperationalStore(ef => { /* ... */ });
    options.AddOpenTelemetryInstrumentation(); // emit TickerQ activities
});
```

- `AddTickerQInstrumentation()` is a `TracerProviderBuilder` extension that calls
  `AddSource("TickerQ")`. Calling `tracing.AddSource("TickerQ")` directly is equivalent.
- `AddOpenTelemetryInstrumentation()` is a TickerQ builder extension that registers the
  instrumentation implementation. Both are required for spans to appear.
- ActivitySource name: **`TickerQ`**.

### Activity status semantics

| Outcome | `ActivityStatusCode` |
|---------|----------------------|
| Completed successfully | `Ok` |
| Completed unsuccessfully | `Error` |
| Failed (exception) | `Error` |
| Cancelled (includes timeout-cancellation) | `Error` |

### Tags are deliberately safe

Tracing tags are chosen to be **safe to export**: no exception messages, no stack traces, no
cancellation/skip reason strings, and no job payloads are ever set as tags. Only a stable
exception **type name** is exported on failure — exception messages and stack traces are
withheld because they can contain credentials or payload data.

Tag keys actually emitted:

| Tag | Meaning |
|-----|---------|
| `tickerq.job.id` | Job id (GUID). |
| `tickerq.job.type` | `TimeTicker` / `CronTicker`. |
| `tickerq.job.function` | Function name. |
| `tickerq.job.priority` | Priority (`Normal`, `High`, `LongRunning`). |
| `tickerq.job.machine` | Executing node label. |
| `tickerq.job.retries` | Configured max retry attempts. |
| `tickerq.job.parent_id` | Parent job id (child jobs only). |
| `tickerq.job.run_condition` | Run condition (time-ticker children only). |
| `tickerq.job.enqueued_from` | Caller source location, e.g. `UserController.CreateUser (Program.cs:42)`. |
| `tickerq.job.execution_time_ms` | Execution duration in ms (completion). |
| `tickerq.job.success` | `true` / `false` (completion). |
| `tickerq.job.retry_count` | Retry count at failure. |
| `tickerq.job.error_type` | Exception **type name** on failure (no message, no stack trace). |
| `tickerq.seeding.type` / `tickerq.seeding.environment` | Data-seeding spans. |

> **Note:** `tickerq.job.enqueued_from` carries a caller source location (type, method,
> file, line). It's build/source metadata rather than user data, but treat your trace
> backend as you would source-path information.

## Dashboard-managed integrations

The optional [TickerQ.Integrations package](../src/TickerQ.Integrations/README.md) adds
Slack, Teams Workflows, and custom JSON webhooks, with destinations and per-function rules
edited in the dashboard. Settings are encrypted in the operational store; EF users must
apply the new settings-table migration. Delivery uses a bounded, best-effort process-local
queue. See the package guide for permissions, shared encryption keys, templates, retries,
and delivery limitations. Applications that need a different transport can register their own
non-blocking `ITickerQFailureNotifier` implementation.


## Dashboard overall status totals

Built-in persistence providers implement the optional `ITickerDashboardStatisticsProvider`
contract. EF Core and MongoDB aggregate status counts in the database. Redis scans IDs
in bounded batches and returns status counts from Lua without transferring job payloads;
in-memory storage counts stored status values directly. Totals retain each provider's
existing dashboard query semantics: flat time rows for EF Core, MongoDB, and in-memory,
and aggregate time roots for Redis, plus cron occurrences. Custom providers that do not
implement the contract retain the existing metadata query fallback.
