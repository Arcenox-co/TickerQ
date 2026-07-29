# Reliability & Execution Contracts

This page documents the execution guarantees TickerQ makes, the ownership model that
keeps distributed nodes from stepping on each other, and the knobs that control lease
renewal, stale-job recovery, timeouts, and graceful shutdown.

All options below live on `SchedulerOptionsBuilder`, configured through `AddTickerQ`:

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
recovery loop per node handles both renewal and the stale-job watchdog.

| Option | Default | Meaning |
|--------|---------|---------|
| `StaleJobRecoveryEnabled` | `true` | Master switch for lease renewal + stale watchdog. Disable to revert to "dead node leaves rows `InProgress` forever." |
| `LeaseDuration` | `45s` | How long a lease lasts from each renewal. Must be comfortably larger than `LeaseRenewalInterval` (3× or more) so a transient DB hiccup or GC pause doesn't get a live job treated as stale. |
| `LeaseRenewalInterval` | `15s` | How often the running node renews leases for its active jobs. |
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

Run-now performs a real acquisition rather than merely bumping `ExecutionTime`, so a job
runs **exactly once** per successful trigger even under concurrent clicks.
