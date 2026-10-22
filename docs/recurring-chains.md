# Recurring cron chains

A cron schedule can own a chain template. Each scheduled occurrence, dashboard **Run now**, or bulk cron retry creates a separate time-ticker chain. The cron function is the root; its children follow the existing run conditions, retries, timeouts, result propagation, and per-function concurrency limits.

Create chains directly through the existing managers. Repeated `Then` calls form one linear
path. `ThenMany` creates a durable parallel group after the current step. Each `Add` result can
continue with its own `Then` or nested `ThenMany` calls. A group continuation starts only after
every branch subtree reaches a terminal status.

```csharp
var result = await timeTickerManager.AddChainAsync(
    chain =>
    {
        var parallel = chain
            .WithDescription("Process invoice")
            .StartWith<PrepareInvoiceJob, InvoiceRequest>(request)
            .ThenMany(branches =>
            {
                branches.Add<ChargeInvoiceJob>()
                    .Then<SendReceiptJob>();

                branches.Add<UpdateAccountingJob>();
            });

        parallel.ThenAfterAll<FinalizeInvoiceJob>();
        parallel.ThenAfterAnyFailed<RecordInvoiceFailureJob>();
        parallel.ThenAfterAllCompleted<ReleaseInvoiceResourcesJob>();
    },
    cancellationToken);
```

The parallel continuation methods use the group's aggregate outcome:

- `ThenAfterAll<T>()` runs when every branch subtree succeeds.
- `ThenAfterAnyFailed<T>()` runs when at least one branch subtree fails.
- `ThenAfterAnyFailedOrCancelled<T>()` runs after a failure or cancellation.
- `ThenAfterAllCompleted<T>()` always runs after all branches settle, including skipped branches.

Failure and cancellation continuations wait for the other branches to settle. They do not race
still-running siblings. Multiple outcome continuations can be attached to the same group by
keeping the returned `parallel` builder as shown above. When a group contains different terminal
outcomes, the aggregate precedence is Failed, Cancelled, Skipped, DueDone, then Done.

Use the same shape for recurring work. The cron definition stores the graph as a template and
creates fresh time tickers for every occurrence.

```csharp
var result = await cronTickerManager.AddChainAsync(
    chain => chain
        .WithExpression("0 0 8 * * *")
        .WithDescription("Daily invoices")
        .SkipIfAlreadyRunning()
        .StartWith<PrepareInvoiceJob>()
        .Then<ChargeInvoiceJob>()
        .Then<SendReceiptJob>(),
    cancellationToken);
```

The generic overloads resolve functions registered with `MapTicker<T>()`. Generated
`TickerFunctionRef` values and explicit function names are also accepted. Step options configure
descriptions, retries, retry intervals, timeouts, stale recovery, and run conditions. The manager
still performs the normal function, request-contract, graph, and provider validation before it
saves anything.

Each step is its own durable ticker. If a later step fails, its retry starts from that step;
already completed parents are not executed again. A child can read its direct parent's committed
result through `TickerFunctionContext.GetParentResult<T>()`, so intermediate values do not have to
be rebuilt after a retry or process restart. Parallel branches receive the result from the step
immediately before their structural group. Join continuations receive that same upstream result;
branch results are not implicitly merged because their result types may differ.

Use the failed step's existing ID to retry it in place after the chain has settled:

```csharp
var retried = await timeTickerManager.RetryFromAsync(failedStepId, cancellationToken);
if (!retried.IsSucceeded)
    throw retried.Exception;
```

This preserves every job ID and the original chain. The selected job and its affected downstream
path run again under a new generation; completed ancestors are traversed without invoking their
functions. Group outcomes and their success, failure, cancellation, and final continuations are
calculated again from the new branch outcome.

In the dashboard, selecting the group header and choosing **Retry this group** reruns every
grouped job and descendant. TickerQ then waits for the group to settle and evaluates its outcome
jobs again. The chain must be fully settled before either retry operation starts.

The entity API remains available when an application needs to construct or edit the stored model
directly:

```csharp
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Models;

var result = await cronTickerManager.AddAsync(new CronTickerEntity
{
    Function = "PrepareReport",
    Expression = "0 9 * * *",
    SkipIfAlreadyRunning = true,
    Chain = new CronChainTemplate
    {
        Children =
        [
            new CronChainNode
            {
                Function = "GeneratePdf",
                RunCondition = RunCondition.OnSuccess,
                Retries = 2,
                RetryIntervals = [10, 30],
                Children =
                [
                    new CronChainNode
                    {
                        Function = "SendReport",
                        RunCondition = RunCondition.OnSuccess
                    }
                ]
            }
        ]
    }
}, cancellationToken);

if (!result.IsSucceeded)
    throw result.Exception;
```

Register every function before scheduling the chain. Use `TickerHelper.CreateTickerRequest(...)` for typed requests on the root or any child. The manager validates and stamps each request contract before saving the template. Add a schedule once, or update its existing ID; do not insert another copy on every application startup.

## Execution and history

The provider atomically creates the complete graph and records its root ID on the cron occurrence. A recovered or repeated dispatch of that occurrence cannot create a second graph. A new scheduled occurrence or explicit **Run now** has a new ID and therefore a new graph. This guarantees one graph per occurrence, not exactly-once external side effects: handlers still need their usual idempotency when retried or recovered.

The cron occurrence records **chain launch**, not completion of all jobs. The dashboard's branch badge opens that run's chain. Tables show an overall chain status badge with its finished-job count inside, such as **Running · 1/3**, including the root and every nested descendant. A completed parent with active children shows **Running**; unfinished queued or idle jobs show **Waiting**. A chain becomes **Done** only when every job is terminal without failures or cancellations. Conditional skips count as finished. Failures and cancellations remain visible while other jobs run, and determine the final chain outcome. A missing or oversized graph shows **Chain unavailable**, never a misleading completed status.

The time-ticker table, cron occurrence table, latest cron run, executions feed and Overview use this summary. Individual jobs retain their own status, logs and result in the chain view; occurrence details also retain the trigger status. Status filters continue to match the root job or trigger status, as labelled in the filter menu. In code, `Status` keeps its existing meaning; `ChainSummary` (or `LastRunChainSummary` on a cron definition) is display-only. Use the occurrence's `ChainRootId` to inspect the run. The jobs use time-ticker contexts; the cron occurrence itself does not execute the root function or publish a function result.

The template is copied when the acquired trigger is materialized. Editing or removing the template affects subsequent launches, never the copied graph of an existing run. Materialized runs cannot be replaced through the chain replacement API because the occurrence's history link must remain stable. Pausing or deleting the cron schedule does not cancel already published chains; manage those executions separately.

Publication does not wait for another worker from inside the trigger's worker. It wakes the scheduler after committing the graph, so a single-worker host can execute recurring chains without a worker-admission deadlock. A crash after commit leaves an ordinary runnable chain for the scheduler/recovery services. In-memory storage retains the existing limitation that restarting the process loses its data.

## Overlap and retention

- `SkipIfAlreadyRunning = false` (default) launches independent chains even while older runs are unfinished.
- `SkipIfAlreadyRunning = true` on the **cron job** atomically skips the new occurrence when **any job** from a previous run of this cron is Idle, Queued, or InProgress. It considers all descendants, even if the root is already Done. The skipped occurrence records the reason. It is not queued for later.

For recurring chains, this is a persisted policy shared by hosts using the same runtime partition. Per-function maximum concurrency still applies separately inside each host. Manual **Run now** observes the same overlap policy.

Recurring chains default to `ChainDeleteBehavior.Restrict`, preserving unfinished descendants. Terminal jobs follow configured retention. The occurrence receipt can outlive its chain; after cleanup its root ID remains a historical reference, and repeating publication still does not recreate the deleted chain. Deleting an occurrence does not delete its published chain.

Templates support 500 jobs total and 20 child levels, matching dashboard traversal limits. These are template limits; the existing one-off chain APIs are unchanged.

## Storage upgrade

EF Core users must generate and apply a host-owned migration **before starting the upgraded library**, even if recurring chains are not enabled. The model adds `CronTickers.SkipIfAlreadyRunning` (false for existing jobs), nullable `CronTickers.Chain` (JSON text), `CronTickerOccurrences.ChainRootId`, and `TimeTickers.OriginCronTickerId`, plus an index on partition/origin/status. The three relational sample applications include `AddRecurringCronChains` and `AddCronOverlapPolicy` migrations and updated snapshots. No existing jobs are converted.

MongoDB requires transactions (a replica set or sharded cluster); the provider provisions the origin/status index. Standalone MongoDB cannot atomically create this multi-document graph and is rejected. Redis uses one atomic Lua operation on standalone Redis; Redis Cluster execution remains unsupported by the scheduler's existing topology rules. Custom persistence providers must implement `SupportsCronChains` and `MaterializeCronChainAsync` before admitting recurring templates.

Deploy compatible scheduler nodes together before enabling recurring chains: older nodes do not understand the template or its launch receipt. No migration can make an older runtime execute the new feature safely.

## Dashboard and local example

Choose **Cron Tickers → Create recurring chain**, or open an existing cron and choose **Add chain / Edit chain**. Configure the schedule, overlap policy and job tree in the existing visual editor. **Remove chain** asks for confirmation and restores single-function execution for future runs.

Select a job in an executed chain to inspect its direct parent, explicit input request, input from the parent's committed output, and the job's own committed output result. The parent reference opens that parent's details in the same flowchart. Payload panels appear only when the corresponding data exists; result panels include the stored contract when available. For grouped jobs, the parent-input panel resolves through the structural group to the executable step immediately before it. Dashboard payload reads are protected by the configured dashboard authentication and use `Cache-Control: no-store`.

The local license smoke sample includes `--dashboard --example chains --port 5221`. It runs with one worker and launches a three-job chain every 20 seconds, using only in-memory demo work. Supply your license through `--license` or `TICKERQ_LICENSE_PATH`; never commit it.
