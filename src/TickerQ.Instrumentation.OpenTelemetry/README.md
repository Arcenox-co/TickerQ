# TickerQ.Instrumentation.OpenTelemetry

OpenTelemetry instrumentation package for TickerQ job scheduler with distributed tracing support.

## Features

- **Distributed Tracing**: Full OpenTelemetry activity/span creation for job execution lifecycle
- **Structured Logging**: Rich logging with job context through ILogger integration
- **Parent-Child Relationships**: Maintains trace relationships between parent and child jobs
- **Retry Tracking**: Tracks retry attempts with detailed context
- **Performance Metrics**: Comprehensive execution time and outcome tracking
- **Error Tracking**: Detailed exception and cancellation tracking
- **Caller Information**: Automatic detection of where jobs are enqueued from

## Installation

```bash
dotnet add package TickerQ.Instrumentation.OpenTelemetry
```

## Usage

### Basic Setup

```csharp
using TickerQ.Instrumentation.OpenTelemetry;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);

// Configure OpenTelemetry with TickerQ ActivitySource
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing =>
    {
        tracing.AddSource("TickerQ") // Add TickerQ ActivitySource
               .AddConsoleExporter()
               .AddJaegerExporter();
    });

// Add TickerQ with OpenTelemetry instrumentation
builder.Services.AddTickerQ<MyTimeTicker, MyCronTicker>(options => { })
    .AddOperationalStore(ef => { })
    .AddOpenTelemetryInstrumentation(); // 👈 Enable tracing

var app = builder.Build();
app.Run();
```

### With Jaeger

```csharp
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing =>
    {
        tracing.AddTickerQInstrumentation()
               .AddJaegerExporter(options =>
               {
                   options.Endpoint = new Uri("http://localhost:14268/api/traces");
               });
    });
```

### With Application Insights

```csharp
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing =>
    {
        tracing.AddTickerQInstrumentation()
               .AddAzureMonitorTraceExporter();
    });
```

## Trace Structure

### Job Execution Activities
```
tickerq.job.execute.timeticker (main job execution span)
├── tickerq.job.enqueued (when job starts execution)
├── tickerq.job.completed (on successful completion)
├── tickerq.job.failed (on failure)
├── tickerq.job.cancelled (on cancellation)
├── tickerq.job.skipped (when skipped)
├── tickerq.seeding.started (for data seeding)
└── tickerq.seeding.completed (seeding completion)
```

### Tags Added to Activities

Tags are deliberately chosen to be **safe to export**. No exception messages, stack traces,
cancellation/skip reason strings, or job payloads are ever set as tags — only a stable
exception **type name** is emitted on failure, because messages and stack traces can contain
credentials or payload data. (Reasons are still available through the structured `ILogger`
output below.)

| Tag | Description | Example |
|-----|-------------|---------|
| `tickerq.job.id` | Unique job identifier | `123e4567-e89b-12d3-a456-426614174000` |
| `tickerq.job.type` | Type of ticker | `TimeTicker`, `CronTicker` |
| `tickerq.job.function` | Function name being executed | `ProcessEmails` |
| `tickerq.job.priority` | Job priority | `Normal`, `High`, `LongRunning` |
| `tickerq.job.machine` | Machine executing the job | `web-server-01` |
| `tickerq.job.retries` | Maximum retry attempts | `3` |
| `tickerq.job.parent_id` | Parent job ID (child jobs only) | `parent-job-guid` |
| `tickerq.job.run_condition` | Run condition (time-ticker children only) | `OnSuccess` |
| `tickerq.job.enqueued_from` | Where the job was enqueued from | `UserController.CreateUser (Program.cs:42)` |
| `tickerq.job.execution_time_ms` | Execution time in milliseconds (on completion) | `1250` |
| `tickerq.job.success` | Whether execution was successful (on completion) | `true`, `false` |
| `tickerq.job.retry_count` | Retry count reached at failure | `2` |
| `tickerq.job.error_type` | Exception **type name** on failure (no message/stack trace) | `SqlException`, `TimeoutException` |
| `tickerq.seeding.type` | Ticker type being seeded | `TimeTicker` |
| `tickerq.seeding.environment` | Environment/node for the seeding span | `production-node-01` |

## Logging Output

The instrumentation provides structured logging for all job events:

```
[INF] TickerQ Job enqueued: TimeTicker - ProcessEmails (123e4567-e89b-12d3-a456-426614174000) from ExecutionTaskHandler
[INF] TickerQ Job completed: ProcessEmails (123e4567-e89b-12d3-a456-426614174000) in 1250ms - Success: True
[ERR] TickerQ Job failed: ProcessEmails (123e4567-e89b-12d3-a456-426614174000) - Retry 1 - Connection timeout
[INF] TickerQ Job completed: ProcessEmails (123e4567-e89b-12d3-a456-426614174000) in 2500ms - Success: False
[WRN] TickerQ Job cancelled: ProcessEmails (123e4567-e89b-12d3-a456-426614174000) - Task was cancelled
[INF] TickerQ Job skipped: ProcessEmails (123e4567-e89b-12d3-a456-426614174000) - Another CronOccurrence is already running!
[INF] TickerQ start seeding data: TimeTicker (production-node-01)
[INF] TickerQ completed seeding data: TimeTicker (production-node-01)
```

## Integration with Logging Frameworks

This package works seamlessly with any logging framework that integrates with `ILogger`:

### Serilog
```csharp
builder.Host.UseSerilog((context, config) =>
{
    config.WriteTo.Console()
          .WriteTo.File("logs/tickerq-.txt", rollingInterval: RollingInterval.Day)
          .Enrich.FromLogContext();
});
```

### NLog
```csharp
builder.Logging.ClearProviders();
builder.Logging.AddNLog();
```

## Performance Impact

- **Minimal Overhead**: Activities are only created when OpenTelemetry listeners are active
- **Efficient Logging**: Uses structured logging with minimal string allocations
- **Conditional Tracing**: No performance impact when tracing is disabled

## Requirements

- .NET 8.0 or later
- OpenTelemetry 1.15.3 or later
- TickerQ.Utilities (automatically included)
