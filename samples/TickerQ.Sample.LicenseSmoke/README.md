# Real-license smoke test

This console app uses the checked-out TickerQ source, its normal pinned signing keys,
source-generated job registration, hosted services, scheduler, and in-memory store.
It does not connect to the licensing portal, send messages, or use a production database.

Keep your original portal-issued `.tqlicense` file outside this repository. From the
repository root, with the .NET SDK for this branch installed:

```sh
dotnet run --project samples/TickerQ.Sample.LicenseSmoke --configuration Release -- \
  --license "/absolute/path/to/your.tqlicense" --suite
```

Alternatively set `TICKERQ_LICENSE_PATH` and omit `--license`. Omit `--suite` to test
only the original certificate.

To view the real dashboard, first build its embedded frontend from this checkout:

```sh
npm --prefix src/TickerQ.Dashboard/wwwroot ci
npm --prefix src/TickerQ.Dashboard/wwwroot run build
dotnet run --project samples/TickerQ.Sample.LicenseSmoke --configuration Release -- \
  --license "/absolute/path/to/your.tqlicense" --dashboard
```

Open <http://127.0.0.1:5217/tickerq/dashboard/about> or the **License** page. No login
is needed for this loopback-only preview. Three harmless license-check jobs run after
startup; see them in Overview, Executions, or Time tickers. Five function definitions
are registered, including two cron examples. Jobs reset on restart. Stop the app with Ctrl+C.

## Configured dashboard examples

Use `--example basic|retention|readonly` with `--dashboard`. Each process has its own
in-memory store; `--port` lets you compare them side by side. The default is `basic`
on port 5217. For example, after building the frontend above:

```sh
dotnet run --project samples/TickerQ.Sample.LicenseSmoke --configuration Release -- \
  --license "/absolute/path/to/your.tqlicense" --dashboard --example retention --port 5217
```

In separate terminals, change the final arguments to `--example basic --port 5218`
and `--example readonly --port 5219`. Open `/tickerq/dashboard/about` on each port.

| Setting | Basic | Retention | Read-only |
| --- | --- | --- | --- |
| Dashboard access | Manage jobs | Manage jobs | Read-only |
| Concurrency per host | Processor count | 4 | 2 |
| Scheduler time zone | Machine local zone | UTC | Europe/Berlin |
| Default attempt timeout | None | 2 minutes | 30 seconds |
| Minimum scheduler wait | 1 second | 250 ms | 1 second |
| Overdue-job check delay | 30 seconds | 15 seconds | 15 seconds |
| Idle worker retirement | 1 minute | 30 seconds | 30 seconds |
| Succeeded retention | Unconfigured | 30 days | 7 days |
| Failed retention | Unconfigured | 90 days | 30 days |
| Cancelled retention | Unconfigured | No automatic deletion | 7 days |
| Skipped retention | Unconfigured | 7 days | 1 day |
| Sweep delay | No retention loop | 1 hour | 15 minutes |
| Batch size / batches per sweep / nodes per chain | Not active | 500 / 10 / 1,000 | 100 / 4 / 100 |

These are illustrative settings, not a production recommendation. The in-memory provider
supports retention; the configured examples use its real maintenance service. Fresh jobs
are kept until eligible, so you will not see immediate deletion with these day-based
windows. Existing retention tests exercise actual deletion and chain safety.

All examples deliberately disable stale-job recovery because this sample does not use
durable lease recovery. They use anonymous access only on loopback, live updates, and no
AI service. The read-only example prevents dashboard mutations while its scheduler still
runs. No external mail, files, or customer systems are touched by the preview jobs.

The registry includes `LicenseSmoke`, `PreviewHeartbeat` (every minute),
`PreviewDailySummary` (02:00 in the scheduler zone), `PreviewOnDemand`, and
`PreviewSlowJob` (a cancellable log demo lasting about seven seconds). The log demo
emits 36 lines: batch progress, an optional-field warning, a clearly labelled simulated
error and recovery, and a multiline summary. The simulated error is handled; the job
finishes successfully. Configured examples also enqueue
the on-demand and slow jobs at startup. Cron preview seeding is disabled for console
smoke checks so it cannot affect the license test.

## Persistent log example

Use `--dashboard --example database --port 5220` to try optional database logs with
SQLite. This example uses the retention configuration above and stores jobs and log
tails in `tickerq-database-logs-preview.db` in the system temporary directory. It creates
only this isolated demo database directly; production databases need a reviewed migration.

Open <http://127.0.0.1:5220/tickerq/dashboard/executions>, view the `PreviewOnDemand` or
`PreviewSlowJob` logs, wait a few seconds, stop and restart the sample. The old execution
and its stored tail remain. To run the expanded log demo again, create a new time ticker
for `PreviewSlowJob` in the dashboard. Logs use the bounded
defaults described in [Execution log storage](../../docs/execution-logs.md).

## License checks

The suite runs each case in a fresh process:

1. A missing certificate blocks scheduling and direct dispatch while the host starts.
2. A malformed certificate is rejected.
3. A temporary copy with a modified signature is rejected.
4. The original certificate validates, a harmless scheduled job runs, and its stored
   status becomes `Done` or `DueDone`.

Exit code `0` means every selected check passed; `1` means a check failed. Each case
has a timeout. Active and expiring certificates pass the positive check. Expired
certificates fail that check even though TickerQ may continue execution for runtime
continuity. Evaluation certificates remain for non-production use only.

Output contains safe verification metadata, never the certificate payload, signature,
workspace name, or customer identifiers. Temporary negative-test files are created in
a private system-temp directory and deleted afterward; the original is never modified.
No trust override or injected license state is used.

This verifies source-level integration on the checked-out branch. It does not verify
published NuGet packaging or durable database providers. `--suite` checks console
behavior; `--dashboard` provides a separate manual browser preview.

Paid schema-v4 certificates use the signed subscription end. Certificate issuance or
reissuance can occur after payment confirmation, so the remaining runtime need not be
exactly one year from the certificate's issue date.
