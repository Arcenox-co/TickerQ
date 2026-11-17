# Promises

Retries, timeouts and alerts cover a job that runs and fails. They cannot see a job that was
never created: the app saved an order as paid and crashed before it scheduled `ProcessOrder`,
a deploy dropped a code path, or a job sat in the queue because no node ran its function.
Nothing failed, so nothing alerts.

A promise closes that gap. It states, in code, that **every row returned by a query gets a
successful job of a function within a time limit**. TickerQ checks the promise on a schedule,
compares your rows with its jobs, and lists the rows whose work was missed. A person runs a fix
or dismisses the row from the dashboard's **Promises** page. Rows that were never scheduled can
also be fixed automatically.

Promises only report. They never change your data, never retry failed jobs (retries own those),
and never create a job unless a person runs a fix or automatic fixing is turned on.

## Declare a promise

A promise belongs to a function. For a class-based function, chain `.Promise(...)` onto its
typed `MapTicker` registration. This one says that every paid order is processed within a
minute of payment:

```csharp
builder.Services.AddDbContext<BillingDbContext>(o => o.UseSqlite(connectionString));

builder.Services.MapTicker<ProcessOrderJob, ProcessOrderRequest>()
    .Promise((BillingDbContext db) => db.Orders.Where(o => o.Status == "Paid" || o.Status == "Processed"), p => p
        .Within(TimeSpan.FromMinutes(1), after: o => o.PaidAt)
        .Job(o => new ProcessOrderRequest
        {
            OrderId = o.Number,
            Customer = new CustomerInfo { Email = o.Email, DisplayName = o.Customer },
            Amount = o.Amount,
        }, matchBy: r => r.OrderId)
        .DoneWhen(o => o.Status == "Processed")
        .Label(o => $"{o.Number} for {o.Customer}")
        .Named("Paid orders are processed"));
```

`[TickerFunction]` methods have no registration builder, so declare their promises in
`ConfigurePromises`, through the source generator's `TickerFunctions` reference. This one says
that every finalized invoice is sent within 20 seconds, and schedules invoices that were never
scheduled without waiting for a person:

```csharp
builder.Services.AddTickerQ(options =>
{
    options.ConfigurePromises(promises =>
    {
        promises.CheckInterval = TimeSpan.FromSeconds(15);
        promises.For(TickerFunctions.BillingJobs.SendInvoice,
            (BillingDbContext db) => db.Invoices.Where(i => i.Status != "Draft"), p => p
                .Within(TimeSpan.FromSeconds(20), after: i => i.FinalizedAt)
                .Job(i => new SendInvoiceRequest { InvoiceId = i.Id }, matchBy: r => r.InvoiceId)
                .DoneWhen(i => i.Status == "Sent")
                .Label(i => $"{i.Number} for {i.Customer}")
                .Named("Finalized invoices are sent")
                .AutoFix(maxPerCheck: 5));
    });
});
```

Both examples come from [`samples/DashboardTestApp`](../samples/DashboardTestApp), whose seed
data includes an invoice finalized without its job and an order whose job "succeeds" without
marking it processed.

The rows come from any service in DI, here the application's `DbContext`, resolved from a new
scope for every check. An overload takes `Func<IServiceProvider, IQueryable<TRow>>` instead.
`ConfigurePromises` also has `p.For<ProcessOrderJob, ProcessOrderRequest>().Promise(rows, configure)`
for class-based functions declared away from their registration.

### The builder

| Method | Required | What it says |
|---|---|---|
| `Within(within, after: row => row.Time)` | yes | Each row's job must succeed within `within` of the row's `after` time. Rows whose time is still empty are not checked yet. `after` may be a `DateTime`, `DateTime?`, `DateTimeOffset` or `DateTimeOffset?`; at most 365 days. |
| `Job(row => request, matchBy: r => ...)` | yes | The request a row's job runs with, and which request values tie a job to its row. |
| `DoneWhen(row => ...)` | no | When the row itself shows the work is done, such as a status. Matching rows are done whatever their jobs did. Without it, a row is done once one of its jobs succeeds. |
| `Label(row => text)` | no | How the dashboard names a row. Defaults to its `matchBy` values. |
| `Named(name)` | no | The promise's name, unique among promises (ignoring case). Defaults to `"<Function> · <Row type>"`. |
| `AutoFix(maxPerCheck: 50)` | no | Schedules the job for rows that were never scheduled, without waiting for a person. See [Automatic fixing](#automatic-fixing). |
| `AllowRepeats()` | no | Lets a row have more than one successful job, for functions meant to run again, such as reminders. Without it, rows whose job ran or will run twice are findings. See [Duplicates](#duplicates). |

The rows query, `after` and `DoneWhen` are expressions the query provider translates, so for
EF Core they must be translatable to SQL. `Job` and `Label` run in memory on the rows a check
reads.

### Matching jobs to rows

`matchBy` names the request values that identify a row. TickerQ reads them from the request
`Job` builds for the row, and from the stored request of each job of the function; a job whose
values are equal belongs to the row.

```csharp
matchBy: r => r.OrderId                              // one value
matchBy: r => new { r.TenantId, r.OrderId }          // several values
matchBy: r => (r.TenantId, r.OrderId)                // a tuple works too
matchBy: r => new { r.Customer.Id, r.OrderId }       // nested members
```

Use identity values only, the ones that stay the same for a row's whole life. An amount, a status
or a timestamp that changes after the job is scheduled makes the job stop matching its row, and
the row is reported as never scheduled.

Values are compared by meaning, not by text: `12` and `12.0` are equal, a `Guid` matches in any
letter case, `DateTime` and `DateTimeOffset` values are compared in UTC, and enums by name. When
a `matchBy` value is empty (`null`) for a row, no job can match it: the row is counted as
**unmatchable** and named in a log warning, never reported as a finding.

## What a check finds

Each check reads the function's jobs, then the rows whose time limit has passed, newest first,
and puts every row in one of these groups:

| Group | Meaning |
|---|---|
| Done | `DoneWhen` matches, or (without `DoneWhen`) one of the row's jobs succeeded. |
| Running | A job is in progress, queued and recently claimed by a node, or waiting and not yet late. |
| Failed | The latest job failed. Counted only: retries, executions and alerts already own failures. |
| Needs review | One of the findings below, not dismissed. |
| Dismissed | A finding a person dismissed. |
| Unmatchable | A `matchBy` value is empty. |

A finding is one of five kinds, each with a plain-words reason. The first three are missed work;
the last two are [duplicates](#duplicates):

| Finding | Shown as | Meaning | Fix (the row's button) |
|---|---|---|---|
| `NoJob` | Never scheduled | No job was ever created for the row. | Schedules a new job with the row's request. |
| `NeverRan` | Never ran | The row's job was cancelled or skipped before it ran, or it is still waiting `NeverRanAfter` after it was due (for example, no node runs its function). | For a cancelled or skipped job, schedules a new one. For a waiting job, starts it now, if it is a standalone job (not a chain step) and the serving node runs jobs; otherwise there is no fix to run. |
| `DoneButNotApplied` | Job ran, row not updated | A job succeeded, but the row in the application's data still doesn't meet `DoneWhen`. The job ran and its change was lost, or the handler forgot to update the row. The dashboard quotes the condition, for example “Status is Sent”. | None. Running it again could repeat the side effect, so this is for a person to investigate. |
| `WillRunTwice` | Will run twice | A job is waiting for a row whose job already succeeded, is running, or is waiting too. | **Cancel extra job** cancels the waiting copies (all of them when one already ran or is running; all but the first otherwise). A copy that started meanwhile is left alone. |
| `RanTwice` | Ran twice | The row's job succeeded more than once, so its work happened twice. | None: the work is done. For a person to check what the extra run did. |

### Duplicates

Unless the promise calls `AllowRepeats()`, each check also looks for rows whose job ran, or will
run, more than once. It reads this from the jobs the check already loads, grouped by their
`matchBy` values, so it costs no extra query and covers rows already marked done. Only standalone
jobs created in the check window count. Retries are the same job and never count, and neither do
failed, cancelled or skipped jobs, which a fix is expected to follow. A fix job never duplicates a
row: its id comes from the finding.

A row that is already done isn't read, so its finding is named by the row label a fix job kept,
the job's own description, or else its `matchBy` values. Duplicates count toward "needs review"
and also show as `counts.duplicates`; they aren't part of the watched rows' done/running split,
since the row itself may be done.

A fix (**Schedule job**, **Schedule again**, **Start now** or **Cancel extra job** in the
dashboard) checks the promise again first. If the row changed in the meantime (its job started,
or the row became done), the fix is refused with "This row changed since it was checked. Refresh
and try again." A new job gets an id derived from the finding, so running the same fix twice, on
one node or two, creates one job. Its description reads
`Promise fix · <promise> · <row label> · by <user>`.

**Fix all** runs one fresh check and fixes up to 100 open, fixable findings, skipping
dismissed ones; it reports how many were fixed, how many failed and how many are left.

**Dismiss** hides a finding from "needs review" with an optional reason (up to 500 characters),
recorded with who dismissed it. The dismissal is kept while the finding keeps turning up and is
removed once a complete check no longer finds it. If the row's situation changes, for example a
new job is cancelled, that is a new finding and needs review again.

Every fix, dismissal and settings change is recorded in the promise's activity list.

## Automatic fixing

`.AutoFix()` lets the scheduled check schedule the job for rows that were **never scheduled**,
the one case where creating the job is clearly right. It never touches the other findings: a
waiting job may still start, and running a job twice is worse than reporting it.

`maxPerCheck` (1 to 1,000, default 50) is a safety limit. When one check finds more
never-scheduled rows than the limit, it fixes **none** of them and pauses automatic fixing,
with a reason such as "42 Invoice rows were never scheduled — more than the limit of 5, so
automatic fixing paused". A sudden batch usually means something else is wrong, such as a broken
`matchBy` or a bad deploy, and scheduling hundreds of jobs would make it worse. Turning automatic
fixing back on in the dashboard ends the pause.

Automatic fix jobs are described as `Promise auto-fix · <promise> · <row label>`. Dismissed
findings are never fixed automatically. The dashboard can turn automatic fixing on or off for a
promise; its setting wins over the code until it is reset.

## Settings from the dashboard

Promises are declared in code; the dashboard cannot create or delete them. It can change three
settings per promise, stored with the promise:

- **Pause** stops scheduled checks. "Check now" and fixes still work.
- **Time limit** replaces the code's `Within` (1 second to 365 days) until it is reset.
- **Fix automatically** turns automatic fixing on or off, replacing `.AutoFix()` until reset.

## Options

```csharp
options.ConfigurePromises(promises =>
{
    promises.CheckInterval = TimeSpan.FromMinutes(1);   // how often each promise is checked; at least 5 s
    promises.NeverRanAfter = TimeSpan.FromMinutes(10);  // a job waiting this long past due "never ran"
    promises.RowUpdateGrace = TimeSpan.FromMinutes(1);  // how long after its job succeeds a row may still be updated
    promises.MaxRowsPerCheck = 1000;                    // rows one check reads, newest first (1 to 100,000)
    promises.MaxJobsPerCheck = 50_000;                  // jobs one check keeps per function, newest first (100 to 1,000,000)
    promises.FullReloadInterval = TimeSpan.FromHours(1); // how often a node re-reads a function's whole job window (at least 1 minute)
    promises.LookBack = TimeSpan.FromDays(30);          // how far back rows are checked without retention
});
```

When more rows are due than `MaxRowsPerCheck` (with `DoneWhen`: more rows not yet done), the
check reads the newest and marks its result **truncated**. The findings and the running and failed
counts then cover the rows it read; the watched count, and with `DoneWhen` the done count, still
cover every row.

When a function has more jobs in the window than `MaxJobsPerCheck`, the check keeps the newest and
narrows its window to the rows those jobs cover (from a day after the oldest job it kept), so no
row is reported as never scheduled because its job wasn't read. The result is marked truncated and
`windowStartUtc` shows the narrowed start.

### How a check reads jobs

Jobs are matched to rows by a key inside each job's stored request, which no store can filter on.
So each node keeps a small **job index per function** in memory: every job of the window with its
key per promise, no payload. A check refreshes the index, then matches in memory. How a refresh
reads depends on the store:

- **EF Core and MongoDB** (`SupportsPromiseDeltaReads`): the time-ticker indexes serve filters on
  status and finish time, so a refresh reads only jobs that finished since the last refresh
  (`ExecutedAt` in the last refresh's half hour and later, so a job stamped by a node whose clock
  lags is still seen; roots and chain steps apart, so MongoDB's parent-first index serves both),
  jobs still waiting or running whatever their age, and, by id, the few jobs the index held as
  waiting that neither read returned (deleted, or finished without a finish time). The request of
  each job not seen before is then read by id.
- **Redis** (`SupportsPromiseChangeFeed`): the provider keeps a sorted set of time-ticker roots by
  the time they were last written, maintained wherever its other derived sets are. A refresh reads
  the aggregates written since the last refresh, chain steps included, and whole aggregates by
  root for the jobs it held as waiting. Nothing is scanned.
- **Any other provider** reads the window each check, as the in-memory store would without its
  opt-in. A custom provider opts in by overriding one of the two capabilities once its reads can
  honour them.

A job's request is parsed once, when the job is first seen or after it was written again (so a
request edited while the job waited gets its new key), and once for every promise on the function.
Cost then follows what changed since the last check and the open rows, not the month's job volume.
The whole window is read again on the node's first check, every `FullReloadInterval`, after a gap
of more than an hour between refreshes, and when more changed than `MaxJobsPerCheck`. That reload is
what catches a finished job deleted outside retention, or one whose finish time was stamped more
than half an hour behind this node's clock; until it runs, such a job still counts as it last was. A
node's index is its own: a node that takes over a promise reloads on its first check. Error messages
are not kept in the index; a check reads them for its findings only.

A row whose job succeeded within `RowUpdateGrace` (one minute by default) counts as running, not
as "job ran, row not updated": many applications mark the row a moment after the job, for example
from a message handler.

Each promise reports `nextCheckAtUtc` (null while paused) and `checkIntervalSeconds`. The dashboard
shows the next check as a countdown, and warns that checks are late when the next one is more than
an interval overdue. That usually means no node is running TickerQ's background services: checks
run only on nodes that run jobs.

### Retention and the check window

[Job retention](reliability.md#job-retention) deletes finished jobs, and a row whose job was
deleted would look never scheduled. So when retention is on, a check only looks at rows whose
time is within the **shortest** configured retention window (`DeleteSucceededAfter`,
`DeleteFailedAfter`, `DeleteCancelledAfter`, `DeleteSkippedAfter`) instead of `LookBack`. Each
result shows the window it covered.

### Chains and cron

A promise's function may run as a standalone job or as a step of a chain; chain steps match their
rows like any other job. A waiting chain step is never started out of order by a fix.

Jobs created by cron schedules, including the steps of recurring chains, are not matched. Promises
are for work your application schedules per row; a function that only runs from a cron schedule
would show every row as never scheduled.

If some of the function's stored requests cannot be read as the promise's request type, for
example after an incompatible request change, the check says how many, and does not report
"never scheduled" that time, since one of those jobs might be the row's.

## Several nodes

Results are stored, so every node and every dashboard shows the same thing.

- **One checker per promise.** Every node that runs background services also runs the promise
  watcher. Before a scheduled check, a node takes the promise's lease (an optimistic update of
  its stored row) for twice the check interval; other nodes skip it while the lease is held, and
  take over once it expires. A promise checked in the last half interval, by another node or a
  person, is skipped.
- **Only the lease holder fixes automatically and publishes events,** so a transition is
  announced once. A check that fails releases the lease at once, and a result is never replaced
  by an older one: if a check finished meanwhile, on any node or from the dashboard, the newer
  result stays.
- **Check now runs on the node serving the request** and stores its result; it does not need the
  lease.
- **Dashboard-only nodes** (`DisableBackgroundServices()`) never check on a schedule. They read
  stored results, and can check now, fix and dismiss; their fix jobs are run by the scheduler
  nodes. Starting a waiting job now needs a node that runs jobs, so on a dashboard-only node a
  "never ran" finding for a waiting job has no fix.

## Storage

Each promise keeps **one row in the `TickerQPromises` table** (a collection or key family of the
same name on MongoDB and Redis; a dictionary in the in-memory store). The row holds the dashboard
settings, dismissals, lease, the latest result (at most 200 findings) and the last 50 activity
entries, as JSON, and is only ever changed at its current version, with retries. The column is
unbounded on every database (`NCLOB` on Oracle), so the whole state always fits.

**EF Core deployments add the table with a migration** generated from the current model, like
the other TickerQ tables (`dotnet ef migrations add AddTickerQPromises`, then
`dotnet ef database update`); startup fails with a clear message while it is missing. MongoDB
provisions the collection and its index itself, Redis needs nothing, and the in-memory store
keeps state for the lifetime of the process.

The row id is derived from the application namespace (`UseApplicationNamespace`) and the promise
name, so two applications sharing a database never share a promise's row, even on MongoDB, where
document ids are global. Renaming a promise starts it with fresh state; the old row is left in
place.

A custom provider without `ITickerPromiseStateStore` keeps promise state in its definitions
store instead, as rows with `Kind = Promise` that the dashboard's saved-work pages and
`/definitions` routes never list or change. Where that store's column is bounded (Oracle's
default NVARCHAR2(2000)), the state is shortened to fit, oldest first: findings down to 10, then
activity down to 5 entries, then the rest of the findings, the rest of the activity, and
dismissals last; settings, the lease and the counts always stay. A provider with neither store
answers promise routes with 501.

## Events

A promise announces changes through `ITickerQEventSink`, a general hook for TickerQ events. The
dashboard registers one that refreshes open Promises pages through SignalR; register your own to
forward events to chat, push or a queue:

```csharp
using TickerQ.Utilities.Events;

public sealed class SlackPromiseSink(SlackClient slack) : ITickerQEventSink
{
    public async ValueTask PublishAsync(TickerQEvent e, CancellationToken ct)
    {
        if (e.Severity == TickerQEventSeverity.Info)
            return;
        await slack.PostAsync($"{e.Title}{(e.Detail is null ? "" : $": {e.Detail}")}", ct);
    }
}

builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<ITickerQEventSink, SlackPromiseSink>());
```

A sink gets five seconds per event before it is abandoned, and its exceptions are logged, never
thrown to the checker, so hand slow work to a queue. Events are published only by the lease
holder and only on transitions:

| Kind | Severity | When |
|---|---|---|
| `promise.needs_review` | Warning | Rows needing review went from none to some, or grew. |
| `promise.recovered` | Info | Rows needing review went back to none. |
| `promise.check_failed` | Error | A check reported an error (it failed, or some jobs could not be read) after the previous one did not; `Detail` says why. |
| `promise.auto_fixed` | Info | Automatic fixing scheduled jobs; `Data["fixed"]` says how many. |
| `promise.auto_fix_paused` | Warning | Automatic fixing paused over its limit; `Detail` says why. |

`TickerQEvent.Id` is the same for the same transition on every node, so a receiver can drop
repeats. `Subject` is the promise's name, `DashboardPath` is `promises/<id>` relative to the
dashboard's base path, and `Data` carries `promiseId`, `promise`, `function` and `needsReview`.

## Startup validation

A promise that cannot work stops the host at startup with an `InvalidOperationException` that
lists every problem, for example:

```text
TickerQ promises are not set up correctly:
- Promise 'Paid orders are processed' needs .Within(...): how soon after which time each row's job must succeed.
- Promise 'Paid orders are processed' needs .Job(...): the request each row's job runs with, and how jobs are matched to rows.
- Promise 'Paid orders are processed' reads its rows from BillingDbContext, which is not registered as a service.
- Promise 'ProcessOrderJob · Order': ProcessOrderJob is not registered as a ticker function. Register it with MapTicker<ProcessOrderJob, ProcessOrderRequest>().
- Promise 'Finalized invoices are sent': no ticker function named 'SendInvoice' is registered.
- Promise 'Finalized invoices are sent': SendInvoice takes a SendInvoiceRequest, not a ProcessOrderRequest.
- 2 promises are named 'Paid orders are processed'. Give each its own name with .Named(...).
- Promises 'A' and 'B' both watch Order rows for ProcessOrderJob. Combine them into one promise.
```

`ConfigurePromises` also rejects out-of-range options immediately, and the builder rejects an
invalid `Within`, name or `maxPerCheck` where it is called.

## Dashboard API

The Promises page uses these routes under `/api/dashboard`. The `GET` routes are reads; everything
else is a write, so a read-only dashboard (`SetReadOnly`) or a license that blocks execution answers 403.

| Route | Does |
|---|---|
| `GET /promises` | Every promise with its latest stored result. |
| `POST /promises/{id}/check` | Checks now on this node, stores and returns the result. |
| `PUT /promises/{id}/settings` | `{ "paused": bool, "withinSeconds": int or null, "autoFix": bool or null }`; an omitted property is unchanged, `null` goes back to the code. |
| `POST /promises/{id}/findings/{findingId}/fix` | Runs the finding's fix; 409 when the row changed or has no fix. |
| `POST /promises/{id}/fix-all` | Fixes up to 100 open findings. |
| `POST /promises/{id}/findings/{findingId}/dismiss` | `{ "reason": "..." }`, optional. |
| `GET /promises/{id}/jobs` | The function's newest 100 jobs in the window, with who scheduled each (the app, a person's fix, or automatic fixing), its row, run time, retries and error, and totals by status for the whole window. |
| `GET /promises/analytics?days=14&offsetMinutes=60` | Every promise's jobs per day for the last 1–90 days, bucketed in the caller's UTC offset: done, failed, cancelled or skipped, pending, fixes and automatic fixes, average and p95 run time, and who scheduled them. Reads at most 20,000 jobs per function; `truncated` says when more were created. |
| `GET /promises/{id}/analytics?days=…&offsetMinutes=…` | The same for one promise. |

An unknown promise is 404. Bodies are JSON (415 otherwise) of at most 16 KB.

## Native AOT

The promise engine and its dashboard routes are trim- and AOT-safe: requests are read and
written through the same source-generated `JsonTypeInfo` as jobs (so the request type must be in
the context passed to `WithJsonContext`), and promise state uses its own generated context. The
`matchBy` parts are compiled once per promise; in a native application that uses the expression
interpreter, which is fine for a handful of member reads.

The rows are read through your `IQueryable`, so its provider must work in Native AOT. EF Core and
MongoDB are managed-only (see [native-aot.md](native-aot.md)), and LINQ to objects through
`AsQueryable()` is marked `RequiresUnreferencedCode` and `RequiresDynamicCode`. In practice,
promises are a managed-runtime feature unless the rows come from an AOT-compatible query
provider.
