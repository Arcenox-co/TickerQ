# TickerQ.Instrumentation.OpenTelemetry

OpenTelemetry instrumentation package for TickerQ job scheduler with distributed tracing support.

## Features

- **Distributed Tracing**: Full OpenTelemetry activity/span creation for job execution
- **Structured Logging**: Rich logging with job context through ILogger
- **Parent-Child Relationships**: Maintains trace relationships between parent and child jobs
- **Retry Tracking**: Links retry attempts to original job traces
- **Performance Metrics**: Tracks job execution times and outcomes

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

// Configure OpenTelemetry
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing =>
    {
        tracing.AddTickerQInstrumentation() // Add TickerQ tracing
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

### Job Execution Trace
```
tickerq.job.executing
├── tickerq.job.enqueued (when job is added)
├── tickerq.job.retrying (for each retry attempt)
└── tickerq.child_job.executing (for child jobs)
```

### Tags Added to Activities

| Tag | Description | Example |
|-----|-------------|---------|
| `tickerq.job.id` | Unique job identifier | `123e4567-e89b-12d3-a456-426614174000` |
| `tickerq.job.type` | Type of ticker | `TimeTicker`, `CronTickerOccurrence` |
| `tickerq.job.function` | Function name being executed | `ProcessEmails` |
| `tickerq.job.priority` | Job priority | `Normal`, `High`, `LongRunning` |
| `tickerq.job.machine` | Machine executing the job | `web-server-01` |
| `tickerq.job.parent_id` | Parent job ID (for child jobs) | `parent-job-guid` |
| `tickerq.job.enqueued_from` | Where the job was enqueued from | `UserController.CreateUser (Program.cs:42)` |

## Logging Output

The instrumentation provides structured logging for all job events:

```
[INF] TickerQ Job enqueued: CronTicker - ProcessEmails (123e4567-e89b-12d3-a456-426614174000) from UserController.CreateUser
[INF] TickerQ Job started: CronTicker - ProcessEmails (123e4567-e89b-12d3-a456-426614174000)
[INF] TickerQ Job completed: ProcessEmails (123e4567-e89b-12d3-a456-426614174000) in 1250ms - Success: True
[ERR] TickerQ Job failed: ProcessEmails (123e4567-e89b-12d3-a456-426614174000) - Retry 1 - Connection timeout
[WRN] TickerQ Job cancelled: ProcessEmails (123e4567-e89b-12d3-a456-426614174000) - Operation was cancelled
[INF] TickerQ Job skipped: ProcessEmails (123e4567-e89b-12d3-a456-426614174000) - Another instance is already running
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
- OpenTelemetry 1.7.0 or later
- TickerQ.Utilities (automatically included)
