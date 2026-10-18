# Per-cron overlap policy

Set `SkipIfAlreadyRunning` on a cron job, or enable **Skip if already running** in its dashboard create/edit form. The handler no longer needs to call a helper to enforce the policy.

```csharp
var result = await cronTickerManager.AddAsync(new CronTickerEntity
{
    Function = "RefreshCatalog",
    Expression = "* * * * *",
    SkipIfAlreadyRunning = true
}, cancellationToken);
```

- The default is `false`: executions may overlap, preserving existing behavior.
- For a single-function cron, `true` admits at most one execution of that cron ID per runtime partition on this scheduler host. A competing run is recorded as **Skipped** before the function is entered; it is not queued for later and does not consume a user retry.
- The check occurs when a worker admits the execution. An occurrence waiting for a worker can still execute if the earlier run finishes before it reaches admission.
- Scheduled occurrences, **Run now**, bulk retries, and overdue recovery carry the same setting. The admitted execution holds its slot across retry waits and until cancellation/timeout cleanup actually exits. Different cron jobs using the same function do not block each other.
- For a recurring chain, the same cron-level setting checks the persisted jobs of prior chains. Any unfinished root or descendant blocks the new chain; the built-in providers check and publish atomically across hosts in the same partition. See [recurring chains](recurring-chains.md).
- Function `maxConcurrency` is separate: it limits concurrent invocations of that function across jobs on a host and waits for a permit. The cron overlap setting skips overlapping runs of one schedule.

To change an existing cron job, set `SkipIfAlreadyRunning` on its entity and pass it to `cronTickerManager.UpdateAsync`. Each cron job can have its own setting even when several jobs use the same function.

The old `context.CronOccurrenceOperations.SkipIfAlreadyRunning()` helper and `CronOccurrenceOperations` context property/type have been removed. This is a source and binary breaking API change: set `CronTickerEntity.SkipIfAlreadyRunning = true` through the manager for each applicable cron job, remove the old call from the function, and rebuild consumers against the updated library. Existing database rows are not automatically opted in based on function source code.

EF Core hosts must generate and apply a migration adding non-null `CronTickers.SkipIfAlreadyRunning` with default `false` before running the updated library. The three relational samples include `AddCronOverlapPolicy`. MongoDB and Redis default a missing flag to false. If upgrading the earlier local recurring-chain preview, move its template `OverlapBehavior = SkipIfRunning` to `CronTickerEntity.SkipIfAlreadyRunning = true`; the new editor stores the policy only on the cron job.

Ordinary cron protection is host-local, matching the prior helper. It is not a distributed lock. Multiple scheduler hosts can each run one occurrence, and external side effects still need idempotency when executions are retried or recovered.
