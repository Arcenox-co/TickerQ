# Reliability & Execution Contracts

This page documents the execution guarantees TickerQ makes, the ownership model that
keeps distributed nodes from stepping on each other, and the knobs that control lease
renewal, stale-job recovery, timeouts, and graceful shutdown.

Scheduler options live on `SchedulerOptionsBuilder`, configured through `AddTickerQ`:

```csharp
builder.Services.AddTickerQ(options =>
{
    options.ConfigureScheduler(scheduler =>
    {
        scheduler.NodeIdentifier = "web-01";
        scheduler.LeaseDuration = TimeSpan.FromSeconds(45);
        scheduler.LeaseRenewalInterval = TimeSpan.FromSeconds(15);
        scheduler.MaxConcurrency = Environment.ProcessorCount;
        scheduler.DefaultExecutionTimeout = TimeSpan.FromMinutes(10);
    });
});
```

## Per-function concurrency

Set `maxConcurrency` on a function to limit how many jobs may invoke it at the same
time in one application instance:

```csharp
[TickerFunction("ProcessInvoices", maxConcurrency: 1)]
public async Task ProcessInvoices(TickerFunctionContext context, CancellationToken cancellationToken)
{
    await ProcessInvoicesAsync(cancellationToken);
}
```

The limit is shared by every job using that registered function name, including
standalone jobs, cron occurrences, chained children at every depth, run-now dispatch,
and overdue recovery. Different function names have independent limits. The default
`0` adds no per-function limit; the scheduler's worker capacity is configured separately.

Each attempt acquires a slot before invoking the function and releases it when the
attempt exits. Retry delays and child traversal do not hold the parent's slot, so a
parent and child can safely use the same function with `maxConcurrency: 1`. A child
with `RunCondition.InProgress` still waits for a slot if its function is at capacity.
Waiting order is not guaranteed.

Waiting is cancellable, and an acquired root stays visible to lease renewal while
waiting. The execution timeout starts after a slot is acquired. If a timed-out function
ignores cancellation, its slot remains occupied until the function actually exits.
Await all work inside the handler: detached work that continues after it returns is
outside this limit. This setting is local to the application instance, not a distributed lock.

## Execution guarantee: at-least-once

TickerQ is an **at-least-once** scheduler. A job that is acquired and starts executing
will run to completion on its owning node, but under node failure a job can be executed
more than once:

- A node acquires a ticker atomically (a conditional update that transitions the row to
  `InProgress` and stamps ownership). Only one node wins any given row.
- While the job runs, the owning node **renews a lease** on the row (see below). If the
  node crashes, is OOM-killed, or is evicted, the lease expires and a watchdog on another
  node applies the ticker's `OnStale` policy — by default it **restarts** the job.
- Because the crashed node may have partially completed side effects before dying, the
  restarted run can repeat work already done.

**Design your job bodies to be idempotent.** Use the job id (`context.Id`) as an
idempotency key, guard external side effects, and treat a second delivery as possible,
not exceptional.

## Overdue schedules

On each scheduling pass, the normal scheduler considers persisted, acquirable cron
occurrences and time tickers even when their scheduled time is more than one second in the past.
Continuous TimeTicker traffic does not exclude them: when both kinds of work are due,
they can be dispatched in the same batch. Recovery preserves the occurrence's original
`ExecutionTime` and the job's `ScheduledFor` value.

The fallback service remains a recovery path for missed work. Its default idle scan
interval is 30 seconds (`FallbackIntervalChecker`); normal scheduling can discover
overdue work independently of that interval. Acquisition checks prevent the two
paths from restarting an already-running occurrence or taking work owned by another
live node. Store failures in fallback recovery are logged before retrying.

By default this recovers existing occurrences once and does not create cron slots missed while the
application was offline. A ticker can instead bound how late persisted work may start with
`WhenMissed(MissedExecutionPolicy.SkipAfter(...))`. Time-chain expiry settles the complete graph as
`Skipped`; cron expiry settles that occurrence without publishing a recurring chain. No user retry
is consumed because the handler was never entered.

Cron definitions may opt into `CatchUp(CronCatchUpMode.RunLatest)`. This creates at most the latest
unpublished slot inside the same bounded lateness window after an outage. It never replays the whole
gap. The durable defaults remain `RunOnce` for a persisted late execution and `Skip` for cron slots
that were never persisted. See [Schedule policies and local-time calendars](schedule-policies.md#missed-execution-windows).
Actual start time still depends on available workers and concurrency limits.

The normal scheduler drains due time tickers in bounded batches without applying
`MinPollingInterval` between successful due acquisitions. It yields between batches and
waits asynchronously for workflow capacity; empty acquisitions still back off. Scheduled
timestamps remain unchanged, and each pass considers due cron work alongside time tickers.
Jobs sharing a second are still checked against their exact scheduled timestamps; a later
job is left unacquired until its turn.
Acquired jobs remain registered for generation-fenced lease renewal during that wait.

Before transitioning an acquired batch to `InProgress`, the scheduler rechecks its
absolute UTC due time. If a timer wakes early, it waits again using a delay rounded
up to whole milliseconds. This applies to time tickers and cron occurrences; it does
not add a delay to already-overdue work. Worker capacity and operating-system scheduling
can still make the actual start later than the scheduled time.
Stopping the queue or invalidating execution permission releases unstarted acquisitions.
Cancelling a job before execution marks it and its unfinished descendants `Cancelled`;
it does not invoke a handler or materialize a new cron chain. Running jobs retain their
normal cooperative cancellation behavior.

## Lease fencing

Every terminal write (marking a job `Done`, `Failed`, `Cancelled`, etc.) is **fenced by
lease ownership**. A node may only write a terminal result for a row it still owns:

- On acquisition the provider stamps the row with a `LockHolder` (the acquiring node's
  execution owner id, see below), a `LockedAt` timestamp, a `LeaseUntil` deadline, and a
  fresh `AcquisitionToken` (a per-acquisition generation marker).
- Lease renewal and terminal writes are matched on **both** the row id **and** its
  `AcquisitionToken`. If ownership has already transferred to another node (new token), the
  old owner's late write is rejected rather than clobbering the new run.
- This prevents a slow or paused predecessor from overwriting a successor's result after
  the lease has been reclaimed.

### Editing persisted time tickers

Configuration updates in EF Core, MongoDB, and Redis compare the incoming execution
snapshot with the current stored status, ownership, lease, and chain generation. An
unowned Idle ticker can be edited normally. If execution advances after the ticker
was read, the update fails without overwriting the newer state. Reload the ticker and
reapply the intended configuration change. A missing ticker is never recreated by an
update, and a rejected single-ticker update returns `IsSucceeded == false`.

Use the dedicated acquisition, cancellation, and execution methods to change runtime
state. MongoDB chain configuration updates require replica set transactions. Redis
configuration edits preserve committed results and terminal evidence. General configuration
updates do not reparent EF Core or Redis chains; use chain replacement to change their
structure. MongoDB retains its transactional reparent operation for a complete terminal
subtree between inactive roots, resolving the destination root and generation and
rejecting cycles, foreign partitions, and stale execution snapshots.

### Lease renewal & stale-job recovery

While a job is executing — or is *acquired and waiting* for a concurrency slot — its lease
is periodically renewed so a live-but-busy node is never mistaken for a dead one. A single
recovery service per node runs independent renewal and stale-job watchdog loops. A slow
stale-job sweep cannot delay the next renewal. Each database operation receives a
cancellation deadline equal to `LeaseRenewalInterval`; providers must honor cancellation.
Renewals continue for already-owned executions even if the license expires while they drain.

| Option | Default | Meaning |
|--------|---------|---------|
| `StaleJobRecoveryEnabled` | `true` | Master switch for lease renewal + stale watchdog. Disable to revert to "dead node leaves rows `InProgress` forever." |
| `LeaseDuration` | `45s` | How long a lease lasts from each renewal. Must be comfortably larger than `LeaseRenewalInterval` (3× or more) so a transient DB hiccup or GC pause doesn't get a live job treated as stale. |
| `LeaseRenewalInterval` | `15s` | Renewal cadence and per-operation cancellation deadline for renewal/recovery. |
| `FallbackBatchSize` | `100` | Maximum overdue roots of each ticker type acquired per scan by the built-in providers. Children stay attached to their root. |
| `QueuedLockTimeout` | `2m` | Max age of the short persisted `Queued` handoff lock before recovery resets it to `Idle`. Protects the crash window between queue acquisition and the `InProgress` transition without releasing a live sibling. |
| `MaxStaleRestarts` | `3` | Poison-job guard: after this many stale restarts the ticker is `Cancelled` instead, so a job that kills its host can't crash-loop the cluster. |

> There is **no explicit "lease ratio" option**. The ratio is the relationship between
> `LeaseDuration` and `LeaseRenewalInterval`; keep the duration ≥ 3× the interval. The
> defaults (45s / 15s) give a 3× ratio.

## Node identity vs. execution ownership

TickerQ separates a **human-readable node label** from the **unique owner id used for row
fencing**. This is the single most important operational distinction for multi-instance
deployments.

| Member | Type | Default | Purpose |
|--------|------|---------|---------|
| `NodeIdentifier` | settable `string` | `Environment.MachineName` | Logical node label shown in dashboards, metrics, and heartbeats. Meant to be stable and readable. |
| `ExecutionOwnerId` | read-only `string` | `"{NodeIdentifier}:{ProcessId}:{nonce}"` | Process-instance-unique owner written to `LockHolder`. Computed; you don't set it directly. |

```csharp
scheduler.NodeIdentifier = "worker-pool-a";
// ExecutionOwnerId becomes e.g. "worker-pool-a:48213:9f3c1b2e7a0c4d..."
```

**Why the split matters:** two processes on the same machine (or two replicas that
intentionally share a `NodeIdentifier`) get **different** `ExecutionOwnerId` values because
each embeds the process id and a random startup nonce. They can therefore never own or
release each other's rows. A node no longer releases all `InProgress` rows for its label on
startup — a restarted process gets a new owner id, and any rows still held by a
now-dead predecessor are reclaimed through normal lease expiry, not a blind startup release.

> **Compatibility note:** `NodeIdentifier` is a public contract. Rows written by older
> versions carry a machine-name `LockHolder`; those are reclaimed by lease/`QueuedLockTimeout`
> recovery rather than by exact owner match, so an upgrade does not strand queued jobs.

## Provider reliability capabilities

Redis heartbeat owners are registered with atomic set operations, scoped by runtime
partition and Redis `InstanceName`. Discovering a dead owner does not remove it. Its
registration is acknowledged only after both cron and time-ticker resource recovery
succeed; failed recovery remains available for retry. Acknowledgement atomically checks
that the heartbeat has not renewed before removing the owner. Recovery is idempotent
when multiple replicas observe the same dead owner.

Heartbeat renewal, dead-node recovery, and pending cache invalidations run independently
inside the existing Redis hosted service. Dashboard notifications are best effort, with
at most one outstanding send, so a slow dashboard does not block renewal. During rolling
upgrades, the service also discovers and acknowledges owners in the older JSON registry.
Older binaries retain their previous coordination behavior until upgraded.

Lease-based renewal and stale recovery require a provider that actually implements them.
Rather than optimistically assuming support, TickerQ exposes an explicit capability that
**fails closed**:

```csharp
// ITickerPersistenceProvider
bool SupportsLeaseBasedRecovery => false; // default: unsupported
```

| Provider | `SupportsLeaseBasedRecovery` | Notes |
|----------|:---:|-------|
| EF Core (`TickerQ.EntityFrameworkCore`) | ✅ `true` | Full lease renewal + stale recovery. |
| MongoDB (`TickerQ.MongoDB`) | ✅ `true` | Full lease renewal + stale recovery. |
| Redis (`TickerQ.Caching.StackExchangeRedis`) | ✅ `true` | Heartbeat/dead-node coordination. |
| In-memory (built-in) | ❌ `false` | Single-process only; no cross-node recovery. |
| Third-party / custom | ❌ `false` (inherited default) | Opt in only after implementing the full contract. |

When the active provider reports `false`, the recovery background service logs a single
warning at startup and disables **both** the renewal loop and the stale watchdog for that
node:

> `Persistence provider does not support lease-based stale-job recovery; lease renewal and the stale-job watchdog are disabled for this node`

The renewal/recovery/held-id members default to "nothing renewed, nothing held, nothing
recovered," so a partially-implemented custom provider can never report fake reliability
success while inheriting real recovery behavior.

## EF Core cron-expression cache

When Redis caching is enabled alongside EF Core, the scheduler's cron-expression snapshot
expires after ten minutes. Startup reconciliation and manager mutations invalidate the
affected runtime partition after the database commit; staged cron additions invalidate
after their transaction commits.

Each cached snapshot carries an expiration and an invalidation generation. Readers reject
a slow, stale refill that finishes after another replica invalidates the cache. A missing
generation marker also forces a refresh. Redis stores one small generation marker per
cache key without expiration, so removing a payload does not erase its invalidation history.

Entries written by older versions without expiration are treated as misses and replaced
on first read. No Redis flush or database migration is required. Upgrade all replicas to
get the new behavior; older binaries do not understand the snapshot format and may cause
extra database reads during a rolling deployment.

Cache read/write failures log a warning and fall back to the database. Caller cancellation
and database errors still propagate before a write commits. After commit, invalidation
uses an independent token: a Redis error or late request cancellation cannot turn that
committed write into a reported persistence failure. Failures are logged and retained for
retry every five seconds. Local reads retry pending invalidations and bypass cached data
while invalidation is failing. The retry queue is process-local; if that process exits
before retry succeeds, the ten-minute snapshot expiry bounds stale reads on other replicas.

The in-process cron parser retains at most 4,096 schedules, evicting the least recently
used entry. Invalid expressions and normalized keys longer than 1,024 characters are
not retained. Oversized valid expressions still parse and execute normally.

## Cooperative in-process timeouts

A per-attempt timeout can be set globally or per ticker:

| Setting | Type | Default | Scope |
|---------|------|---------|-------|
| `SchedulerOptionsBuilder.DefaultExecutionTimeout` | `TimeSpan?` | `null` (no timeout) | Applies to every ticker that doesn't set its own. |
| Per-ticker `TimeoutSeconds` | `int?` | `null` | `> 0` enables, `<= 0` explicitly disables, `null` falls back to the global default. |
| `SchedulerOptionsBuilder.TimeoutGracePeriod` | `TimeSpan` | `5s` | Grace after cancellation before a still-running delegate is logged as timeout-pending. |

Timeouts are **cooperative** — TickerQ cannot forcibly kill running .NET code. When a
timeout fires:

1. The `CancellationToken` passed to your function is cancelled.
2. If the delegate observes cancellation and exits within `TimeoutGracePeriod`, the job is
   persisted as **`Cancelled`** with a timeout reason. **Timeouts do not consume retry
   budget.**
3. If the delegate **ignores** cancellation, TickerQ does not lie about completion. It keeps
   the job's registration, renewable lease, and DI scope alive, and logs a critical event:

   > `TickerQ job {Function} ({JobId}) exceeded timeout {Timeout} and grace {Grace}; the delegate ignored cancellation and remains owned, scoped, and lease-renewed until it exits`

   The same job is **not** retried or reacquired while that zombie task is still live on the
   owning node. Only after the delegate actually exits is the terminal timeout outcome
   written once, the lease released, and the scope disposed.

> **True hard timeouts are impossible in-process.** If you must forcibly bound untrusted or
> uncancellable work, run it in an external process you can kill. Always honor the
> `CancellationToken`.

## Graceful shutdown / drain

On host shutdown TickerQ stops accepting new root jobs, then waits for accepted workflows
(queued, waiting for capacity, retrying, and executing) to finish. Accepted chains can
continue scheduling their remaining steps during this drain:

| Option | Default | Meaning |
|--------|---------|---------|
| `ShutdownDrainTimeout` | `30s` | How long to wait for in-flight executions before letting the host exit. `Zero` disables draining (jobs are abandoned and later healed by stale-job recovery). Also bounded by `HostOptions.ShutdownTimeout`. |

Drain completion includes queued attempts, active executions, accepted workflows, and acquired
ownership registrations. A retry delay or a chain between steps is still unfinished work.
If the timeout is hit, the remaining counts are logged and abandoned jobs are recovered
later through lease expiry.

Acquired-job cancellation callbacks run asynchronously. A callback that blocks cannot hold
host shutdown past its deadline: callback completion and queued-generation releases share
a ten-second cleanup budget, bounded by the remaining host deadline. The cancellation source
stays alive until its callbacks finish, including after shutdown stops waiting.

The host shutdown deadline covers **all** services. Reserve time after scheduler drain for
integration delivery and execution-log persistence, for example:

```csharp
builder.Services.Configure<Microsoft.Extensions.Hosting.HostOptions>(options =>
{
    options.ShutdownTimeout = TimeSpan.FromSeconds(60);
    options.ServicesStopConcurrently = false;
});
```

Scheduler timing options are validated during `AddTickerQ`: finite positive maintenance
intervals, a lease at least three times the renewal interval, positive worker/batch counts,
and a nonnegative shutdown drain. A zero drain remains supported. Overdue and regular
scheduling share ownership registration and generation-fenced release; queued acquisitions
remain visible to lease renewal before any worker-capacity wait.

## Global concurrency

`SchedulerOptionsBuilder.MaxConcurrency` (default `Environment.ProcessorCount`) bounds
**executing function attempts on one host**. Standalone jobs, cron occurrences, and every
individual chain step share this budget. For example, a limit of four allows at most four
function attempts across all chains and standalone jobs combined.

An attempt retains its slot across asynchronous I/O and until its delegate actually exits,
including a delegate that ignores timeout cancellation. Waiting for a per-function permit,
waiting between retries, and coordinating child jobs do not occupy execution slots. A job
can schedule another immediate job using its own function without waiting for that new
job to finish; a per-function limit of one still prevents their bodies from overlapping.

The built-in scheduler uses fixed asynchronous consumers and signals them when work arrives.
Consumers wait without keeping native threads blocked, so idle worker retirement is
unnecessary.

The built-in scheduler uses the following memory limits. Configure them through
`options.ConfigureScheduler(scheduler => { ... })`:

| Option | Default | What it bounds |
|--------|---------|----------------|
| `AcquisitionBatchSize` | `100` | Time-ticker roots loaded per scheduled or immediate acquisition, before loading their chains. |
| `MaxActiveWorkflows` | `128` | Admitted roots, including chains, function waits and retry delays. |
| `MaxQueuedExecutions` | `1024` | Attempts waiting in the execution queue, independent of CPU count. |
| `MaxConcurrentChainSteps` | `16` | Live step tasks in each chain, including attempts waiting for capacity or retries. |

When root admission is full, acquired rows are released for a later scan; a committed
manager mutation remains successful. Roots beyond an acquisition batch remain in the store.
The scheduler or overdue recovery acquires them later. Under sustained overload this can
increase start latency; tune batch size and admission limits together with worker capacity.
After rejected admission, overdue recovery waits for built-in scheduler capacity and its
configured fallback interval before another acquisition attempt. Custom schedulers use the
fallback interval. Saturation therefore does not cause a rapid acquire/release loop.

Chain traversal retains pending steps as graph references rather than creating an async
task and cancellation registration for every child at once. `InProgress` children remain
eligible alongside their parent, subject to capacity. Deferred children still wait for the
parent and its concurrent subtrees. There is no guarantee that all sibling steps run at once.

Time-chain validation, execution-context construction, delegate caching, and in-memory graph
operations use iterative traversal, so valid deep time chains do not consume a CLR stack
frame per level. The whole graph still needs memory. Recurring cron-chain templates keep
their separate limits of twenty child levels and five hundred jobs including the root.

Exceptions from `ITickerExceptionHandler.HandleExceptionAsync` are logged separately from
the failed job attempt. They do not consume extra retries, replace the original failure, or
prevent the final job status from being persisted.

These are count limits, not a hard process-memory budget. A root's entire graph and request
data still need memory, and application handlers allocate their own data. Keep requests small
and reference large business records by ID. The in-memory provider also retains the stored
jobs themselves: configure job retention when you want completed history removed. Using EF,
MongoDB or Redis moves durable history outside the process, but active execution still uses RAM.
See [job retention](#job-retention) for the opt-in policy and its separate log-retention settings.

In-memory time-ticker discovery scans the partition directly and keeps at most
`AcquisitionBatchSize` candidates from the earliest eligible second. It does not copy all
retained rows into an acquisition buffer. Scan time still grows with stored history, and
selected chains are loaded separately; this does not make retained history free.

Per-function concurrency permits are reclaimed when their last holder or waiter exits.
The legacy raw-semaphore API retains its semaphore because callers can keep that reference;
custom callers should use the owned `AcquireAsync` permit and dispose it after execution.

`High`, `Normal`, and `Low` priorities use separate FIFO queues with weighted service of
4:2:1 while all three queues have work. This favors higher priorities while allowing lower
priorities to make progress. Priority does not preempt an already-running attempt and is
applied after a function's concurrency permit becomes available.

`LongRunning` uses the normal-priority queue and counts against the same execution limit.
Its delegate starts on a dedicated thread; after an `await`, continuations can run on the
CLR thread pool. Use asynchronous APIs for I/O and avoid `.Wait()` or `.Result` in ordinary
job handlers. TickerQ's execution limit does not limit threads created by user code.

The dashboard shows running attempts and queued attempts in Scheduler Host. Diagnostics
also shows function-capacity waits, retry delays, and active workflows. A workflow may
contain several running or waiting steps, so these counts are not additive.

## Nodes and cross-node cancellation

Built-in operational providers publish a lightweight heartbeat for every scheduler process.
The dashboard **Nodes** page shows its friendly name, logical node identifier, version,
registered function count, scheduler state, execution capacity, queue pressure, function
waits, retry delays, start time, and last heartbeat. Heartbeats are scoped by
`ApplicationNamespace`, so applications sharing one physical database do not discover or
control each other's workers.

Configure the identity and optional navigation address with the scheduler options:

```csharp
options.ConfigureScheduler(scheduler =>
{
    scheduler.NodeIdentifier = "orders-worker";       // same logical role across replicas is valid
    scheduler.NodeDisplayName = "Orders worker · EU 1";
    scheduler.AdvertisedDashboardUrl =
        "https://orders.internal/tickerq/dashboard";  // optional absolute HTTP(S) URL
});
```

`AdvertisedDashboardUrl` is only an operator link. TickerQ never sends control requests to
that URL, so it may be a private hostname, ingress address, or omitted entirely. Do not put
credentials in it. The process-unique execution owner remains internal and is separate from
`NodeIdentifier`.

When a cancellation is requested from a different node, TickerQ resolves the persisted owner
and writes a short-lived command to the shared operational store. The owning process polls
these commands, verifies the exact acquisition or chain generation, and cancels the local
`CancellationTokenSource`. A delayed command therefore cannot cancel a later retry or rerun
of the same ticker. Delivery normally takes up to one second and cancellation remains
cooperative: the handler must observe its `CancellationToken`. Commands expire after 30
seconds and operational command history is pruned after one day.

This mechanism works with the built-in in-memory, EF Core, MongoDB, and Redis providers.
In-memory discovery only spans hosts inside the same process; use a shared durable provider
for separate replicas. EF Core applications must add the `TickerQNodes` and
`TickerQControlCommands` tables to their application-owned migration before deploying this
version. Custom providers can opt in through `ITickerQClusterControlStore`; providers that do
not implement it continue scheduling normally but do not expose nodes or route cancellation.

## Job retention

Completed jobs are retained by default. Automatic deletion is **opt-in**: all four
`JobRetentionOptions` status windows default to `null`, which means retain that status
indefinitely. Setting at least one window through `ConfigureJobRetention` enables the
maintenance service on hosts with background services enabled. Configuring a queue-only
host does not start retention.

For example, a database-backed service might keep successful, cancelled, and skipped
executions for one day, and failures for a week:

```csharp
builder.Services.AddTickerQ(options =>
{
    // Keep your existing namespace, scheduler, and persistence configuration.
    options.ConfigureJobRetention(retention =>
    {
        retention.DeleteSucceededAfter = TimeSpan.FromDays(1);
        retention.DeleteFailedAfter = TimeSpan.FromDays(7);
        retention.DeleteCancelledAfter = TimeSpan.FromDays(1);
        retention.DeleteSkippedAfter = TimeSpan.FromDays(1);
        retention.SweepInterval = TimeSpan.FromMinutes(5);
    });
});
```

`DeleteSucceededAfter` covers both `Done` and `DueDone`. Each other window covers its
named terminal status. Windows must be positive when set; leaving a window `null`
continues to retain that status. Eligibility uses `ExecutedAt`, strictly older than
the configured window, rather than creation time or the scheduled execution time.
Rows without `ExecutedAt`, pending or running work, and rows with an acquisition token
or an unexpired lease are retained. Cron definitions are never removed by retention.

Time chains are deleted as a whole only after **every node** meets its own status window
and ownership checks. A successful root with a failed child stays while that child is
too recent, or indefinitely if `DeleteFailedAfter` is unset. Chains larger than
`MaxNodesPerChain` stay intact. `ChainDeleteBehavior.Cascade` does not bypass these
retention checks.

| Option | Default | Scope |
|--------|---------|-------|
| `SweepInterval` | `1 hour` | Delay before the first sweep after activation and between subsequent sweeps. |
| `BatchSize` | `500` | Maximum time-chain roots examined or cron occurrences deleted per provider call. A chain can contain multiple rows. |
| `MaxBatchesPerSweep` | `10` | Shared provider-call budget across time chains and cron occurrences. |
| `MaxNodesPerChain` | `1,000` | Maximum nodes examined for one time chain; larger chains are retained whole. |

Expiry makes a job eligible for a later sweep; it is not an immediate deletion deadline.
The two streams alternate within their shared budget, and time-chain traversal continues
across sweeps so retained chains do not repeatedly block later candidates. Account for
the sweep interval and batch budget when sizing cleanup for a steady workload. Provider
failures are logged and retried on a later sweep.

The built-in in-memory, EF Core, MongoDB, and Redis providers support retention. A custom
provider without `SupportsRetention` causes startup to fail when retention is enabled.
MongoDB retention requires transaction support for its atomic deletion operations.

With the in-memory provider, stored history, requests, and results remain in application
RAM until deleted. For independent jobs at a steady rate, retained job count is roughly
the completion rate multiplied by the retention window, plus cleanup lag; mixed statuses
and chains can retain more. Choose windows that fit the workload, or use a database
provider for longer history. For example, 50 completions per second with a five-minute
window means roughly 15,000 retained jobs before cleanup lag, plus their requests and results.
The one-day example above is not a memory budget for a high-volume in-memory host.
A database provider stores that history outside the scheduler
process, but still needs retention to control storage growth. Deleting rows makes their
objects eligible for collection; it does not guarantee an immediate fall in process
working set or release of collection capacity.

Execution logs have an **independent** lifetime, configured through
`options.ConfigureExecutionLogs(...)`. Deleting a job does not clear its in-memory log
tail. By default, tails retain up to 500 executions, 200 lines and 32 KiB of encoded data
per execution, and 16 MiB of encoded data in total. `MemoryRetention` defaults to
30 minutes since the last appended line; the hosted log service requests pruning on its
`CleanupInterval`, normally one minute when database log persistence is off. These
expiration scans are throttled to the lesser of `MemoryRetention` and one minute.
The encoded-data limits exclude managed-object overhead. If `PersistToDatabase` is enabled,
archive `Retention` defaults to seven days and `MaxStoredExecutions` to 5,000; neither
setting changes job-row retention.

## Execution progress

Long-running functions can publish a percentage and a short status message through their
execution context:

```csharp
[TickerFunction("Invoices.Import")]
public async Task ImportAsync(TickerFunctionContext context, CancellationToken cancellationToken)
{
    for (var page = 1; page <= 20; page++)
    {
        await ImportPageAsync(page, cancellationToken);
        await context.ReportProgressAsync(
            page * 5,
            $"Imported page {page} of 20",
            cancellationToken);
    }
}
```

`ReportProgressAsync` accepts values from 0 through 100 and messages up to 500 characters.
Each write is fenced to the active acquisition generation. A late update from an execution
that was cancelled, completed, or superseded is rejected and cannot overwrite the current
attempt. Progress is cleared when a new attempt is acquired. The dashboard shows progress on
time jobs, cron occurrences, and chain steps. Report at meaningful checkpoints rather than
inside a tight record loop because progress is a durable provider write.

Custom persistence providers can implement `UpdateExecutionProgressAsync`; the default
interface implementation rejects progress writes. EF Core applications must add and apply an
application-owned migration for the nullable `ProgressPercent`, `ProgressMessage`, and
`ProgressUpdatedAt` columns on time tickers and cron occurrences.

## Run-now, chain retry & bulk retry

The dashboard can re-run terminal jobs. See the [Dashboard security](./security.md) page for
authentication; the execution contract is:

- **Run a time ticker now** — `POST {basePath}/time-tickers/{id}/run`. A terminal job
  (`Done`, `Failed`, `Cancelled`, `Skipped`) or an `Idle`/`Queued` job is atomically
  re-acquired: reset to `InProgress`, given fresh ownership (`LockHolder`, `LockedAt`,
  `LeaseUntil`, new `AcquisitionToken`), and prior run state is cleared (`RetryCount`,
  exception, skip reason, executed-at, elapsed time, stale-restart count) before dispatch.
  Returns **204** on success, **404** if not found, and **409** if the job is already
  `InProgress` (never duplicated).
- **Run a cron ticker now** — `POST {basePath}/cron-tickers/{id}/run`.
- **Retry a chain from a selected job** —
  `POST {basePath}/time-tickers/{id}/retry-from-here`. TickerQ keeps the original chain and job
  IDs, clears the selected job and its affected downstream path, and runs that path under a new
  acquisition generation. Completed ancestors are traversal points and are not invoked again.
  When the selected job is inside a group, completed sibling branches keep their recorded
  outcomes while the group and its success/failure/final continuations are evaluated again.
  Selecting the structural group itself reruns every grouped job and its descendants, then
  evaluates the group's outcome continuations from the new aggregate result.
  The whole chain must be settled before this operation can acquire it, and concurrent retry
  requests cannot both win. If scheduler admission or host shutdown prevents the retry from
  starting, the acquired root is failed and its pending retry path is skipped under the same
  generation fence. The root durably records the selected retry point and its previous outcome
  until terminal completion, so stale recovery reconstructs the same pass-through path after a
  node failure instead of invoking completed ancestors as ordinary chain steps. EF Core stores
  require nullable `ChainRetryFromId` and `ChainRetryPreviousStatus` columns from the current model.
- **Bulk retry** — `POST {basePath}/executions/bulk-retry`. Runs each selected time ticker
  through the same single-acquisition command; cron selections are de-duplicated per parent
  cron id so one cron fires once regardless of how many occurrences are selected. The
  response's `Affected` count reflects only rows that were actually revived.

Time run-now performs an atomic acquisition rather than merely changing `ExecutionTime`.
Only one caller wins while that job is acquired. Cron run-now creates a new occurrence for
each accepted request; `SkipIfAlreadyRunning` controls overlap for that cron definition.
These operations do not guarantee exactly-once application side effects across retries or crashes.

The chain flowchart exposes this operation as **Retry from this job**. Selecting a group header
instead exposes **Retry this group**, with an impact preview listing the jobs that will run again.

## Dashboard test runs

The chain editor can execute one locally registered function without creating a time ticker,
cron definition, or occurrence row. It uses the editor's request payload, respects the
function's local concurrency limit and a 1–300 second timeout, and returns the terminal status,
duration, error, and optional function result directly to the editor. Integration failure
notifications and application `ITickerExceptionHandler` callbacks are suppressed for this
ephemeral execution.

Test runs still invoke real application code and can write to databases or call external
services. A function can detect this mode through `context.IsTestRun` when it needs a safe
preview path. Remote SDK functions require a durable execution and cannot use this endpoint.
Progress reports during an ephemeral test are not persisted.

## Manager operations and committed results

Both managers expose `RunNowAsync` and `RequestCancellationAsync` for running jobs.
Time tickers also support `CancelAsync` to atomically cancel pending jobs and their pending descendants. Cron also exposes `SetEnabledAsync`
and typed creation overloads for functions registered with `MapTicker<TFunction>`:

```csharp
var created = await cronManager.AddAsync<DailySummary>("0 0 8 * * *", cancellationToken);
if (!created.IsSucceeded)
    throw created.Exception;

var manualRun = await cronManager.RunNowAsync(created.Result.Id, cancellationToken);
if (!manualRun.IsSucceeded)
    throw manualRun.Exception;

// Cron cancellation takes the occurrence ID returned by RunNowAsync.
var cancellation = await cronManager.RequestCancellationAsync(manualRun.Result.Id, cancellationToken);
await cronManager.SetEnabledAsync(created.Result.Id, false, cancellationToken);

// Time operations use the time ticker ID; run-now accepts root jobs only.
var timeRun = await timeManager.RunNowAsync(timeTickerId, cancellationToken);
var timeCancellation = await timeManager.RequestCancellationAsync(timeTickerId, cancellationToken);
```

For request-bearing functions use
`AddAsync<TFunction, TRequest>(expression, request, cancellationToken)`; the manager resolves
the registered function name and validates its request contract just as it does for entity-based
creation. Use entity-based creation to configure retries, overlap policy, or a recurring chain.

Run-now requires a running, licensed dispatcher. Success means the work was accepted, not
that its handler finished successfully. Time run-now returns `AffectedRows = 1` without a
`Result` entity. Cron run-now returns the new occurrence so callers can track its ID. Immediate
dispatch and notifications continue independently of caller cancellation after persistence.
If dispatch fails after acquisition, the result remains accepted, the error is logged, and
normal lease-expiry recovery handles the persisted work when recovery is enabled.

Cancellation is cooperative and applies only to executions registered on the current host
in the manager's runtime partition. `AffectedRows` is `1` when a matching execution was
signalled, or `0` when none was found. It does not delete a job, cancel an entire chain, or
cancel work running on another server. Handlers must observe their cancellation token.
Disabling a cron prevents future scheduling and does not cancel an active occurrence.

Manager insert operations fail when the provider reports zero inserted rows; an empty add
batch is a successful no-op. Update/delete operations preserve their existing semantics:
check `AffectedRows` to distinguish a change from a successful no-op. Batch counts come from
the provider and may include descendants; they are not necessarily the number of input roots.
Batch atomicity remains provider-specific.

Once persistence succeeds, a failed dashboard notification or scheduler follow-up is logged
and does not turn the result into a failed save. Retrying a committed insert can create duplicate
work. Dashboard notifications are best-effort refresh hints, including for updates, deletes,
and cron enable/disable; clients can refetch persisted state if a notification is missed.
