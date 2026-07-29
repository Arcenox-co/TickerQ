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

## Failure webhook (bounded, non-blocking)

TickerQ ships a built-in failure notifier that POSTs a JSON payload to a webhook on terminal
failure and on timeout-cancellation. It is designed to **never block or slow job execution**.

```csharp
builder.Services.AddTickerQ(options =>
{
    options.NotifyFailuresViaWebhook(
        url: "https://hooks.example.com/tickerq",
        headers: new Dictionary<string, string> { ["Authorization"] = "Bearer …" });
});
```

### Overload behavior & drop metric

Deliveries are queued through a **bounded channel** (capacity **1000**) in
`BoundedChannelFullMode.DropOldest` mode. When the queue is full, the oldest pending
notification is dropped so a slow or unreachable webhook endpoint can never apply
backpressure to the scheduler.

Drops are counted (`DroppedCount`, read atomically) and logged — on the first drop and then
at power-of-two thresholds — as a warning:

> `TickerQ failure webhook queue dropped {DroppedCount} events due to capacity {Capacity}`

The delivery pump retries transient send failures (delays ≈ 1s then 5s) with a 10s HTTP
timeout, all off the execution path.

### Reason sanitization

The failure reason string is sanitized before it leaves the process. By default the sanitizer
collapses `\r`/`\n` to spaces and truncates to **2048** characters. Override it to redact or
reshape the reason:

```csharp
options.NotifyFailuresViaWebhook(
    url: "https://hooks.example.com/tickerq",
    headers: null,
    reasonSanitizer: reason => Redact(reason)); // FailureWebhookOptions.ReasonSanitizer
```

If you pass no sanitizer, `FailureWebhookOptions.DefaultReasonSanitizer` is used.
