# Schedule policies and local-time calendars

Schedule policies are named, reusable availability rules for one-time jobs, chains, cron jobs,
and recurring chains. They express when work may start in the policy's local time zone without
putting holiday or business-hour logic in job handlers.

Policies are stored by the configured persistence provider and isolated by
`UseApplicationNamespace(...)`. The built-in in-memory, EF Core, MongoDB, and Redis providers
support them. The dashboard provides a **Schedule policies** editor and a cron preview that shows
which candidate occurrences are allowed or skipped.

## Define a policy

Seed code-owned policies before ticker seeders run. `UpsertAsync` makes startup reconciliation
idempotent across restarts:

```csharp
builder.Services.AddTickerQ(options => options
    .UseApplicationNamespace("orders-production")
    .UseSchedulePolicySeeder(async (policies, cancellationToken) =>
    {
        await policies.UpsertAsync(
            SchedulePolicy.Create("EU business hours")
                .WithDescription("Weekdays from 09:00 to 17:00, excluding closure dates")
                .InTimeZone("Europe/Tirane")
                .AllowWeekdays(
                    DayOfWeek.Monday,
                    DayOfWeek.Tuesday,
                    DayOfWeek.Wednesday,
                    DayOfWeek.Thursday,
                    DayOfWeek.Friday)
                .AllowBetween(new TimeOnly(9, 0), new TimeOnly(17, 0))
                .ExcludeAnnualDate(1, 1)
                .ExcludeAnnualDate(12, 25)
                .ExcludeDate(new DateOnly(2027, 1, 2))
                .Build(),
            cancellationToken);
    }));
```

`AllowBetween` uses an inclusive start and exclusive end. A window whose end is earlier than its
start crosses midnight. Equal start and end allows the whole local day. With no time windows, the
allowed weekdays are available for the whole day.

Policy names are stable identifiers. Matching ignores case and repeated whitespace, while the
stored display name is preserved. Rename a policy by creating a new one and moving schedules to it.
Deletion is rejected while a ticker or another policy references the policy.

## One-time jobs and chains

A one-time schedule chooses what happens when its requested instant is blocked:

```csharp
var result = await timeTickerManager.AddAsync<PrepareInvoicesJob>(
    TimeSchedule.At(DateTimeOffset.Parse("2026-12-25T10:00:00+01:00"))
        .UsingPolicy("EU business hours")
        .WhenBlocked(BlockedOccurrenceBehavior.MoveForward),
    cancellationToken);
```

The same policy applies to a chain root:

```csharp
var result = await timeTickerManager.AddChainAsync(chain => chain
    .RunAt(DateTime.UtcNow.AddHours(1))
    .UsingPolicy("EU business hours")
    .WhenBlocked(BlockedOccurrenceBehavior.MoveForward)
    .StartWith<PrepareInvoicesJob>()
    .Then<SendInvoicesJob>(), cancellationToken);
```

| Behavior | One-time result |
| --- | --- |
| `Reject` | Creation or update fails and nothing is persisted. This is the default. |
| `Skip` | The root and all descendants are persisted as `Skipped` evidence. No function runs. |
| `MoveForward` | The requested instant is retained and execution moves to the next allowed instant. |

Policies belong on the chain root. Once a root is admitted, descendants follow their normal
conditions, retries, and delays even if the policy window closes. This keeps a workflow from being
paused halfway through its own transaction or compensation path.

`RequestedExecutionTime`, the effective `ExecutionTime`, policy version, blocked behavior, and the
adjustment reason are retained for operations and audit views. A later policy edit affects future
schedule decisions; it does not rewrite a one-time job that was already moved or skipped.

## Cron schedules and recurring chains

Cron expressions are evaluated in the cron schedule's time zone. Blocked occurrences are omitted,
so cron policies always use `Skip` behavior:

```csharp
var result = await cronTickerManager.AddAsync<GenerateDailyReportJob>(
    CronSchedule.From("0 9 * * 1-5")
        .InTimeZone("Europe/Tirane")
        .UsingPolicy("EU business hours"),
    cancellationToken);
```

For a recurring chain:

```csharp
var result = await cronTickerManager.AddChainAsync(chain => chain
    .WithExpression("0 9 * * 1-5")
    .InTimeZone("Europe/Tirane")
    .UsingPolicy("EU business hours")
    .StartWith<PrepareInvoicesJob>()
    .Then<SendInvoicesJob>(), cancellationToken);
```

The cron time zone controls when the cron expression produces candidates. The policy time zone
controls how each candidate is evaluated. They may differ deliberately. For example, a report can
be triggered at 09:00 in New York and still be blocked by a European closure calendar.

Each search uses the current stored policy definition. Editing a policy therefore changes future
cron occurrences without rewriting every cron row. The cron row keeps the policy version last
validated by a create or update operation for diagnostics.

## Missed execution windows

Availability policies answer **whether a scheduled instant is allowed**. Missed-execution settings
answer **whether already-allowed work is still useful after it starts late**. They can be used with
or without a named schedule policy.

For a one-time job, `RunOnce` preserves the durable default: the persisted job executes once when a
worker becomes available, however late that is. Use `SkipAfter` when late work would be harmful or
obsolete:

```csharp
await timeTickerManager.AddAsync<RefreshExchangeRatesJob>(
    TimeSchedule.At(DateTimeOffset.UtcNow.AddMinutes(5))
        .WhenMissed(MissedExecutionPolicy.SkipAfter(TimeSpan.FromMinutes(2))),
    cancellationToken);
```

The same rule belongs on a time-chain root:

```csharp
await timeTickerManager.AddChainAsync(chain => chain
    .RunAt(DateTime.UtcNow.AddMinutes(5))
    .WhenMissed(MissedExecutionPolicy.SkipAfter(TimeSpan.FromMinutes(2)))
    .StartWith<PrepareRatesJob>()
    .Then<PublishRatesJob>(), cancellationToken);
```

If the root is more than two minutes late when execution is about to enter user code, the root and
all of its descendants become `Skipped`. No handler runs. The exact grace boundary is inclusive: a
job delayed by exactly two minutes may still start.

Cron schedules have two separate decisions:

```csharp
await cronTickerManager.AddAsync<RefreshExchangeRatesJob>(
    CronSchedule.From("*/5 * * * *")
        .WhenMissed(MissedExecutionPolicy.SkipAfter(TimeSpan.FromMinutes(2)))
        .CatchUp(CronCatchUpMode.RunLatest),
    cancellationToken);
```

| Setting | Controls | Default |
| --- | --- | --- |
| `WhenMissed(...)` | A cron occurrence that was already persisted but starts late because of capacity, a store outage, or a scheduler restart. | `RunOnce()` |
| `CatchUp(...)` | Cron slots that passed while no scheduler was available to persist them. | `Skip` |

`RunLatest` materializes at most the latest missed slot still inside the `SkipAfter` window. It does
not replay every slot from an outage. `RunLatest` therefore requires a bounded `SkipAfter` policy;
TickerQ rejects an unbounded combination during create or update. Built-in providers support this
lookup. A custom provider must explicitly implement and advertise cron catch-up support.

For a recurring chain, an expired occurrence is recorded as `Skipped` and its chain is not
materialized. A caught-up occurrence that remains inside the window publishes one fresh chain in
the usual way. Failure retries remain separate: missed-execution handling does not consume the
function's retry count because skipped user code never started.

## Compose policies

Composition keeps shared rules small:

```csharp
await policies.UpsertAsync(
    SchedulePolicy.Create("Production window")
        .InTimeZone("Europe/Tirane")
        .AllowWeekdays(DayOfWeek.Monday, DayOfWeek.Tuesday,
            DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday)
        .AllowBetween(new TimeOnly(7, 0), new TimeOnly(22, 0))
        .Require("Company calendar")
        .ExceptWhen("Maintenance freeze")
        .Build(), cancellationToken);
```

- `Require(name)` allows an instant only when both policies allow it.
- `ExceptWhen(name)` blocks an instant whenever the referenced policy allows it.
- Cycles and missing references are rejected during create or update.

## Application-owned date providers

Use a date provider when exclusions come from application code or a bounded local data source:

```csharp
public sealed class PublicHolidayProvider : IScheduleDateProvider
{
    public string Name => "Public holidays";

    public bool IsExcluded(DateOnly localDate)
        => localDate is { Month: 1, Day: 1 } or { Month: 12, Day: 25 };

    public string GetReason(DateOnly localDate)
        => $"{localDate:yyyy-MM-dd} is a public holiday.";
}

builder.Services.AddTickerQ(options => options
    .AddScheduleDateProvider<PublicHolidayProvider>()
    .UseSchedulePolicySeeder((policies, cancellationToken) =>
        policies.UpsertAsync(
            SchedulePolicy.Create("Company calendar")
                .ExcludeDatesFrom("Public holidays")
                .Build(), cancellationToken)));
```

`IsExcluded` is called synchronously while the scheduler searches candidate instants. Keep it fast,
deterministic, and free of network calls. Refresh remote calendar data into a local immutable cache
outside the evaluator. Register the same provider name on every scheduler node.

## Operational boundaries

- **Run now and retry are explicit operator actions.** They bypass the root's schedule policy and
  use the current time for admission, while retaining the existing execution or occurrence identity
  rules.
- A disabled cron remains disabled even when its policy allows an occurrence.
- `SkipIfAlreadyRunning`, worker capacity, retries, timeouts, and run conditions are evaluated
  independently of schedule policies.
- A policy change wakes the local scheduler. Shared-store nodes observe the new definition during
  their next acquisition calculation; deploy identical date-provider code to every node.
- A custom persistence provider must implement `ISchedulePolicyStore`. Otherwise policy operations
  fail clearly while schedules without policies continue to work.
- The policy model and built-in serializers are compatible with Native AOT. Application request and
  result payload types still follow the JSON source-generation requirements in
  [Native AOT](native-aot.md).

For EF Core, add the policy table and ticker columns to the application-owned migration before
deploying this feature to an existing store. See
[Upgrading CronTicker and TimeTicker storage](upgrading-cron-time-tickers.md#schedule-policy-schema).
