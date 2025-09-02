## Architecture Overview

### Core Concepts
- **TickerFunction**: Attribute-marked method discovered at compile time (source generator). Provides: name, optional cron expression, priority.
- **Tickers**: Runtime scheduling units.
  - TimeTicker (one-shot or delayed execution)
  - CronTicker (recurring based on cron expression)
- **Execution Host**: Orchestrates retrieval and execution of due tickers, using a custom `TickerTaskScheduler` for prioritized execution.

### Scheduling Flow
1. Source generator emits registration code for all `[TickerFunction]` methods.
2. At startup, `AddTickerQ` wires services and registers functions in `TickerFunctionProvider`.
3. When a ticker becomes due, host enqueues a task into custom scheduler.
4. Priority influences ordering; long-running tasks can leverage default scheduler.
5. Execution context (`TickerFunctionContext`) surfaces metadata and cancellation helpers.

### Persistence (EF Core Mode)
- Operational store tracks: Time tickers, Cron tickers, Occurrences.
- Seeder API can bootstrap initial tickers.
- Missed ticker handling ensures continuity after restarts.

### Dashboard Pipeline
- `AddDashboard` registers repository + SignalR hub.
- Real-time thread count + ticker events broadcast via `ITickerQNotificationHubSender`.
- Middleware hooks: `PreDashboardMiddleware`, `PostDashboardMiddleware`, `CustomMiddleware`.

### Extensibility Points
- `ITickerExceptionHandler` – centralize error handling.
- `UseTickerSeeder` – seed dynamic schedules.
- `SetInstanceIdentifier` – multi-node awareness.
- `TickerRequestProvider.GetRequestAsync<T>` – fetch typed request payload.
- `TickerCancellationTokenManager` – external cancellation.

### Threading Model
- Custom scheduler manages a blocking task queue and priority map.
- Active thread count debounced and pushed to dashboard.

### Security
- Built-in Basic Auth (optional) or host-provided authentication/authorization.

### When to Use Each Mode
| Mode | Use Case | Limitations |
|------|----------|-------------|
| In-memory | Local dev, ephemeral tools | No persistence, no retry state across restarts |
| EF Core | Production workloads | Requires migrations & DB |

### Error Handling Strategy
Implement `ITickerExceptionHandler` to:
- Log and alert on failures
- Perform cleanup
- Distinguish canceled vs faulted jobs

### Future Enhancements (Suggested)
- Pluggable storage providers
- Fine-grained rate limiting per function
- Multi-tenant dashboard filtering

See other docs for configuration, usage, and API details.