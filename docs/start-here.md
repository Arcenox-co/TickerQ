# Start Here

Welcome to TickerQ. This page orients you in under 3 minutes.

## What Is TickerQ?
A background job scheduler for .NET that:
- Discovers job methods at compile time (no reflection) using a source generator.
- Runs one-off (time) and recurring (cron) jobs.
- Persists state (with EF Core) for reliability & restarts.
- Exposes a real-time dashboard for visibility & control.

## When Should I Use It?
Use TickerQ when you need any of:
| Need | Solution |
|------|----------|
| Run a task every minute | Cron ticker (`[TickerFunction("MyJob", "* * * * *")]`) |
| Execute a task once in the future | Time ticker (schedule programmatically) |
| Trigger job with payload | Use `TickerFunctionContext` + request object |
| View progress and history | Enable Dashboard package |
| Survive restarts & retry | Add EF Core operational store |
| Prioritize urgent tasks | Set `taskPriority` on `[TickerFunction]` |

## Core Concepts (Glossary Lite)
| Term | Meaning |
|------|---------|
| TickerFunction | A method annotated with `[TickerFunction]` acting as a job entry point |
| TimeTicker | A scheduled one-off execution (specific time / delay) |
| CronTicker | A recurring execution based on a cron expression |
| Occurrence | A single execution record (for history & retries) |
| Request Payload | Structured data passed to a job (typed) |
| Priority | Execution ordering hint (High / Normal / Low / LongRunning) |
| Seeder | Startup code that inserts initial tickers |
| Instance Identifier | Distinguishes a node in multi-instance deployments |

See full details in [Glossary](glossary.md) and [Architecture](architecture.md).

## Fast Decision: In-Memory vs EF Core
| Scenario | Recommended Mode |
|----------|------------------|
| Local prototype / demo | In-memory only |
| Durable jobs / retries | EF Core |
| Multiple app instances (scale-out) | EF Core |
| Auditing & dashboard history | EF Core |

## Minimal Examples
### Recurring Job Only (In-Memory)
```csharp
builder.Services.AddTickerQ();
app.UseTickerQ();

class Jobs {
    [TickerFunction("Heartbeat", cronExpression: "*/5 * * * *")] // every 5 minutes
    public void Heartbeat() => Console.WriteLine("Alive");
}
```

### Production-Ready (EF Core + Dashboard + Auth)
```csharp
builder.Services.AddTickerQ(o =>
{
    o.AddOperationalStore<AppDbContext>(ef =>
    {
        ef.UseModelCustomizerForMigrations();
        ef.CancelMissedTickersOnAppStart();
    });
    o.AddDashboard(d => d.EnableBasicAuth = true);
});
app.UseTickerQ();
```
Visit: `/tickerq-dashboard`.

## Typical Next Steps
1. Read: [Getting Started](getting-started.md)
2. Add your first job: [Usage](usage.md)
3. Pass request data & retries: [Recipes](recipes.md)
4. Observe & manage: [Dashboard](dashboard.md)
5. Harden & scale: [Configuration](configuration.md), [Entity Framework Core](entity-framework.md)

## Troubleshooting Early On
| Symptom | Quick Fix |
|---------|-----------|
| Dashboard 403 | Ensure Basic Auth config / remove conflicting auth filters |
| Job not showing | Confirm attribute + project build (source generator emits on build) |
| Migration fails | Adjust referential actions, see [Troubleshooting](troubleshooting.md) |
| Cron not firing | Validate expression syntax (standard 5-field) |

Still stuck? Check [FAQ](faq.md) or open an issue.
