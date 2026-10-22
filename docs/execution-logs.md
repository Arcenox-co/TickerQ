# Execution log storage

TickerQ captures `ILogger` messages emitted inside a ticker function's execution scope.
Capture is enabled by default; database persistence is **off by default**. Console output,
host startup logs, and structured logger scopes are not captured. Normal host logging
filters also apply. The default minimum captured level is `Information`.
EF Core database categories are excluded by default, so SQL commands, connections, and
transactions produced inside a job do not crowd out its application messages.

## Scheduler lifecycle entries

Execution tails also include structured `[scheduler]` entries by default:

- Job/occurrence created after persistence accepts it.
- Queued when the scheduler queues an execution; immediate runs can go directly to running.
- Running at the start of each function attempt.
- Retry scheduled, including its delay and the previous failure message.
- Completed, failed, cancelled, or skipped after the outcome is acknowledged, with duration and retry count.

Each child has its own entries. A recurring cron's trigger reports **Recurring chain
launched**; completion of its child jobs is reported separately. The dashboard colors
scheduler entries using the corresponding job status. Ordinary application messages
keep their severity colors (`Information` stays neutral even after a successful run).

Set `logs.IncludeLifecycleEvents = false` to keep only application logs. Lifecycle
entries use `MinimumLevel`, `Redact`, and the same line/byte/retention limits. Repeated
notifications for the same lease, attempt, and event reuse the same line ID. They are
still best-effort diagnostics: jobs from before capture was enabled, direct database
edits, and bulk maintenance operations need not have a complete lifecycle trail.
Custom providers using the legacy unacknowledged cron-update contract cannot emit a
verified terminal entry; the dashboard can still show their saved failure reason.

New compressed tails retain lifecycle metadata. Existing version-1 tails remain
readable; this change does not require an additional database schema migration.

## Enable database storage

Configure the usual EF Core, MongoDB, or Redis operational store, then enable execution
logs on the same TickerQ registration. The order of these two configuration calls does
not matter.

```csharp
services.AddTickerQ(options =>
{
    // Keep your existing license, application namespace, scheduler and store configuration.
    options.ConfigureExecutionLogs(logs =>
    {
        logs.PersistToDatabase = true;
        logs.Retention = TimeSpan.FromDays(7);
        logs.MaxStoredExecutions = 5000;
        logs.MaxLinesPerExecution = 200;
        logs.MaxBytesPerExecution = 32 * 1024;
        // Add application infrastructure categories that should stay out of job logs.
        logs.ExcludedCategoryPrefixes =
        [
            "Microsoft.EntityFrameworkCore.Database",
            "Orders.Infrastructure"
        ];
    });
});
```

All three built-in database providers implement the optional
`ITickerExecutionLogPersistence` contract. Enabling persistence with only the in-memory
operational store fails at startup with an explanatory error. A custom provider can
implement that contract. Set `logs.Enabled = false` to turn capture off completely;
`PersistToDatabase` must also be false.

### EF Core

TickerQ's complete DbContext/model customizer always maps an `ExecutionLogs` table in the
configured TickerQ schema. **Generate, review and apply the current EF migration when
upgrading.** Enabling `PersistToDatabase` later registers the writer and reader services but
does not change the EF model or require another migration. When persistence is disabled, TickerQ
does not write new rows to this table. An existing database is not upgraded by `EnsureCreated`,
and TickerQ does not silently create this table in a production database.

If you configure your application model manually, include this mapping in both the
runtime and design-time model:

```csharp
modelBuilder.ApplyConfiguration(
    new TickerQ.EntityFrameworkCore.Configurations.ExecutionLogArchiveConfiguration("ticker"));
```

Use your configured schema instead of `ticker` when different. The background writer
coalesces lines into snapshots and performs conditional row updates/inserts; it does
not write a row per message. Cleanup uses bounded deletes. If an application is
permanently retired, arrange database maintenance for its expired log rows: its
background cleanup no longer runs. Expired tails are excluded from reads regardless.

### MongoDB

Logs use a separate collection named `<time-ticker-collection>_ExecutionLogs`. The
provider creates expiry and ordering indexes, including a TTL index. Its database
identity needs permission to create these indexes and read/write/delete this collection.
MongoDB TTL cleanup is asynchronous; TickerQ also performs bounded cleanup.

### Redis

Logs use separate hashes and expiry/ordering indexes under the configured Redis instance
prefix and TickerQ application partition. Compressed payloads are binary. Hashes expire
automatically; the background cleaner removes stale index entries and excess tails.
Keys for one partition share a Redis Cluster hash slot. Durability across a **Redis
server restart** depends on your Redis persistence configuration (AOF/RDB); the library
does not enable that server setting. Redis eviction policy can remove logs earlier.

## Space and retention

One compressed tail is stored per ticker ID and application partition. This is a
diagnostic view, not an immutable history of every retry/attempt. On a new host or after
memory eviction, newly captured lines for the same ticker can replace its earlier stored
tail. Keep centralized application logging if you need complete history.

| Setting | Default | Effect |
| --- | --- | --- |
| `MinimumLevel` | Information | Excludes Debug and Trace unless explicitly enabled |
| `ExcludedCategoryPrefixes` | `Microsoft.EntityFrameworkCore.Database` | Excludes matching logger categories from job capture |
| `MaxLinesPerExecution` | 200 | Keeps the latest lines per ticker |
| `MaxMessageBytes` | 2 KiB | Clips each message at a UTF-8 boundary |
| `MaxBytesPerExecution` | 32 KiB | Caps the encoded tail before GZip compression |
| `MaxBufferedExecutions` | 500 | Caps tails held in memory per host |
| `MaxBufferedBytes` | 16 MiB | Caps encoded content held in memory per host |
| `MemoryRetention` | 30 minutes | Expires idle local tails |
| `Retention` | 7 days | Expires stored tails after their last captured line |
| `MaxStoredExecutions` | 5,000 | Removes the oldest stored tails per application |
| `FlushInterval` | 2 seconds | Delay between background flushes |
| `WriteBatchSize` | 100 | Maximum snapshots written per flush |
| `CleanupInterval` | 1 minute | Idle cleanup delay; cleanup also follows writes |
| `StorageTimeout` | 5 seconds | Bounds each background flush/read operation |

Defaults target at most about **156 MiB of uncompressed encoded tail content per
application** once cleanup catches up; GZip commonly reduces it. This is not a database
file-size guarantee: indexes, metadata, managed-memory overhead, other applications,
database allocation and temporary cleanup backlog are additional. Concurrent nodes can
temporarily exceed the tail count. Deleting rows does not necessarily shrink physical
database files. Lower limits take effect as tails are rewritten/cleaned, not by an
immediate rewrite of all existing data.

Job-history retention and log retention are independent. Deleting a job does not
immediately delete its log archive; the log expiry/count policy removes it separately.
Use consistent limits and synchronized clocks across hosts sharing an application.

## Failure behavior and dashboard

Job code only writes to a bounded memory buffer. Background persistence failures do not
fail jobs. Pending snapshots retry, and older writes cannot overwrite a newer stored
version. Shutdown makes a bounded final flush attempt. Hard crashes, exhausted buffers,
memory expiry during a prolonged outage, or shutdown timeouts can lose unflushed lines.
This feature does not provide guaranteed delivery or audit-log durability.

The dashboard reads the shared store plus its local buffer. It shows storage mode,
truncation and storage-read failures, and provides a manual refresh. It polls visible
active tails and briefly refreshes after completion to catch buffered writes; longer
flush delays may require a manual refresh. A collapsed log panel stops tail polling.
Failed and cancelled executions also show a collapsed **Exception details** section.
When symbols are available, it includes the exception type, method, source filename,
source line, and a bounded application stack trace. Generated dispatch and TickerQ scheduler
frames are removed, as are server directory paths. Source lines
require PDB files to be deployed with the application. Existing rows that only contain a
message and stack trace remain readable.
The About page shows the configured limits. Existing dashboard authentication and
authorization protect the endpoint; responses use `Cache-Control: no-store`.

Avoid logging credentials or sensitive request payloads. For application-specific
redaction, set `logs.Redact` to a thread-safe `Func<string, string>` before startup. It
runs on the formatted message before either memory or database storage. If it throws,
that line is dropped so diagnostics cannot fail the job. Category and function names
are retained (bounded to 256 UTF-8 bytes each); redaction is not automatic secret detection.

## Detailed host logging

For scheduler troubleshooting, enable additional operational logs through the main TickerQ
registration:

```csharp
services.AddTickerQ(options =>
{
    options.EnableDetailedLogging();
});
```

This emits scheduler acquisition decisions, queue publication, overdue recovery, and restart
reasons through the application's normal `ILogger` pipeline. It is disabled by default and
does not include request payloads, result payloads, SQL commands, or integration secrets.
Use the normal ASP.NET Core logging configuration to route or filter the `TickerQ` categories.
Detailed host logging is separate from per-job execution-log capture and does not consume a
job's bounded dashboard log tail.

## Local example

The [license smoke sample](../samples/TickerQ.Sample.LicenseSmoke/README.md) includes
`--dashboard --example database --port 5220`, using an isolated SQLite file in the
system temporary directory. Run a preview job, wait for its logs to flush, then restart
the sample and inspect the same execution. No production database is involved.

## Batch reads and node health

The embedded dashboard uses `POST /api/dashboard/log-tail/query` for chain logs.
Each request accepts at most 50 unique ticker IDs and optional opaque cursors. EF Core
and MongoDB fetch a batch in one query; Redis pipelines its reads. Unchanged tails
return no lines. Appended lines are returned incrementally; a reset response replaces
the cached tail when retention, eviction or a late arrival changes its prefix.

The chain tree is fetched through one bounded `/api/dashboard/time-tickers/{id}/chain`
request, with one database query per level instead of one request per job. It rejects
chains exceeding 500 nodes or 20 child levels instead of returning an incomplete tree.
Chain logs stop polling settled jobs individually after final flush checks, pause when
collapsed or the browser is hidden, and retain at most 1,000 lines / approximately
1 MiB of text in the merged view. A notice indicates when that view has dropped lines.

`GET /api/dashboard/diagnostics` and **System → Diagnostics** report **the responding node's**
pending log writes, unflushed tails dropped, write failures, last successful flush,
and last successful/failed overdue-work scan. Timestamps are UTC in the API. These
counters reset when the node restarts; they are not cluster-wide totals. An unflushed
tail can be dropped by memory or retention limits even if an older snapshot is already
stored. Read availability and write health are reported separately. A successful read
never clears a write failure; only a successful write does. Before the first write,
write availability is unknown, not healthy. The Diagnostics page refreshes every five
seconds while visible and shows database-write details only when persistence is enabled.
Overview links to it with a compact health indicator; About keeps the configuration
and retention limits. Job-specific log tails stay in execution details.

The `TickerQ` meter also exports `tickerq.logs.pending_writes`,
`tickerq.logs.dropped_unflushed`, `tickerq.logs.write_failures`,
`tickerq.logs.write_available` (-1 unknown, 0 failed, 1 succeeded),
`tickerq.logs.last_successful_flush`, `tickerq.fallback.failures`, and
`tickerq.fallback.last_success`. Last-success gauges use Unix seconds (0 before first
success). Overdue-work failures generate a structured warning at most once per minute.

EF model configuration uses separate model caches for TickerQ schema/log/history
settings while preserving an application's own `IModelCacheKeyFactory`, including
its tenant discriminator. Existing application contexts do not need to replace their
custom cache-key factory to enable logs.
