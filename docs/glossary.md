# Glossary

Concise definitions to build shared vocabulary.

## Core
- **TickerFunction**: A method decorated with `[TickerFunction]`; becomes a schedulable job.
- **TimeTicker**: One-off scheduled job instance.
- **CronTicker**: Recurring job defined by cron expression.
- **Occurrence**: Single execution record (success/failure metadata).
- **TickerFunctionContext**: Context passed to execution containing metadata & controls.
- **Priority**: Execution ordering (High, Normal, Low, LongRunning) influencing scheduler ordering.
- **Instance Identifier**: Logical name of the running node.

## Configuration
- **Operational Store**: EF Core persistence layer for tickers & occurrences.
- **Seeder**: Startup initializer inserting baseline tickers.
- **Missed Job Check Delay**: Interval scanning for missed executions after downtime.
- **Exception Handler**: Implementation of `ITickerExceptionHandler` for centralized failure handling.

## Dashboard
- **Dashboard Repository**: Data access abstraction for UI.
- **Notification Hub**: SignalR hub pushing live updates (thread count, statuses).
- **Basic Auth**: Built-in lightweight authentication mode.

## Internals
- **Source Generator**: Roslyn code gen building registration for discovered ticker functions.
- **TickerTaskScheduler**: Custom prioritized task scheduler.
- **Cancellation Token Manager**: Tracks and allows external cancellation of running/due tickers.
- **Request Payload**: Serialized JSON stored & supplied to job method at runtime.

See [Architecture](architecture.md) for deeper technical flow.
