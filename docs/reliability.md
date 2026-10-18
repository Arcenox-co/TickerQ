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

## Overdue cron occurrences

On each scheduling pass, the normal scheduler considers persisted, acquirable cron
occurrences even when their scheduled time is more than one second in the past.
Continuous TimeTicker traffic does not exclude them: when both kinds of work are due,
they can be dispatched in the same batch. Recovery preserves the occurrence's original
`ExecutionTime` and the job's `ScheduledFor` value.

The fallback service remains a recovery path for missed work. Its default idle scan
interval is 30 seconds (`FallbackIntervalChecker`); normal scheduling can discover
overdue cron work independently of that interval. Acquisition checks prevent the two
paths from restarting an already-running occurrence or taking work owned by another
live node. Store failures in fallback recovery are logged before retrying.

This recovers existing occurrences; it does not create an occurrence for every cron slot
missed while the application was offline. Actual start time still depends on available
workers and concurrency limits.

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

On host shutdown TickerQ stops accepting new work, then waits for in-flight executions
(queued, waiting-for-capacity, and executing) to finish:

| Option | Default | Meaning |
|--------|---------|---------|
| `ShutdownDrainTimeout` | `30s` | How long to wait for in-flight executions before letting the host exit. `Zero` disables draining (jobs are abandoned and later healed by stale-job recovery). Also bounded by `HostOptions.ShutdownTimeout`. |

Drain completion is measured from the scheduler's authoritative queued + active-execution
counts, not inferred from worker threads. If the timeout is hit, the remaining queued/active
counts are logged and abandoned jobs are recovered later through lease expiry.

The host shutdown deadline covers **all** services. Reserve time after scheduler drain for
webhook delivery and execution-log persistence, for example:

```csharp
builder.Services.Configure<Microsoft.Extensions.Hosting.HostOptions>(options =>
{
    options.ShutdownTimeout = TimeSpan.FromSeconds(60);
    options.ServicesStopConcurrently = false;
});
```

The built-in webhook pump stops after scheduler/recovery producers. It completes its queue
and drains for up to 10 seconds, bounded by the remaining host deadline. HTTP timeouts,
network failures, 408, 429 and 5xx responses receive bounded retries; other 4xx responses
are logged without retry. Alerts remain best-effort: process crashes, queue overflow, or
an exhausted shutdown deadline can lose notifications.

Scheduler timing options are validated during `AddTickerQ`: finite positive maintenance
intervals, a lease at least three times the renewal interval, positive worker/batch counts,
and a nonnegative shutdown drain. A zero drain remains supported. Overdue and regular
scheduling share ownership registration and generation-fenced release; queued acquisitions
remain visible to lease renewal before any worker-capacity wait.

## Global concurrency

`SchedulerOptionsBuilder.MaxConcurrency` (default `Environment.ProcessorCount`) bounds the
number of **active executions**, not just worker-loop iterations. Accepted asynchronous work
is awaited and tracked, so a burst of `LongRunning`/async jobs cannot exceed the configured
ceiling.

## Run-now & bulk retry

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
- **Bulk retry** — `POST {basePath}/executions/bulk-retry`. Runs each selected time ticker
  through the same single-acquisition command; cron selections are de-duplicated per parent
  cron id so one cron fires once regardless of how many occurrences are selected. The
  response's `Affected` count reflects only rows that were actually revived.

Time run-now performs an atomic acquisition rather than merely changing `ExecutionTime`.
Only one caller wins while that job is acquired. Cron run-now creates a new occurrence for
each accepted request; `SkipIfAlreadyRunning` controls overlap for that cron definition.
Neither operation guarantees exactly-once application side effects across retries or crashes.

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
