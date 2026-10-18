# TickerQ Dashboard Authentication

Simple, clean authentication for your TickerQ Dashboard.

Optional [database execution logs](../../docs/execution-logs.md) retain compressed,
bounded diagnostic tails with EF Core, MongoDB, or Redis. The About page shows their
configuration; log panels identify truncation and storage-read failures.

## About page

Open **System → About** to inspect the current host's registered function count,
searchable function registry, scheduler settings, dashboard version, and access mode.
When job retention is configured, the page shows each status window, sweep interval,
and batch limits. A status without a retention window has no automatic deletion.
These are configured policies, not cleanup-run history; changes are made in the host
application. The page uses the existing authenticated read endpoints and is also
available in read-only dashboards.

About shows one section at a time: **General**, **Scheduler**, **Retention**,
**Execution logs**, or **Functions**. Use the section menu on desktop or the
dropdown on smaller screens. The `section` query parameter keeps the selected
section on refresh and supports browser Back/Forward and direct links, for example
`/tickerq/dashboard/about?section=retention` (adjust for your dashboard base path).
An absent or unrecognized section opens General. Function search and pagination
are retained while switching sections within About.

The timing fields describe separate mechanisms:

| About field | Meaning |
| --- | --- |
| Registered functions | Definitions discovered on this host; not a count of scheduled jobs or executions. A default cron expression does not prove the schedule is enabled. |
| Dashboard version / Node | Dashboard assembly version and configured scheduler node identifier. |
| Scheduler time zone | Time zone used for cron scheduling; independent of the toolbar's display time zone. |
| Concurrency limit per host | Host scheduler's shared concurrency budget, not a cluster-wide limit. |
| Configured default timeout | Per-attempt default, overridden by a job's timeout. Null or non-positive disables the default; the runtime caps timer durations. |
| Minimum scheduler wait | Existing `MinPollingInterval` option. A floor on the calculated wait, not polling every N seconds. With a 1-second floor, a job due in 100 ms gives a 1-second wait; a job due in 10 minutes gives a 10-minute wait. Notifications can interrupt waits and immediate dispatch is separate. |
| Overdue-job check delay | `FallbackIntervalChecker`: delay after an empty or failed fallback check; work can be checked sooner during catch-up. |
| Idle worker retirement delay | `IdleWorkerTimeOut`: allows extra idle workers to retire; at least one remains available. |
| Stale-job recovery configured | Configuration only; recovery additionally requires provider lease support. |
| Your dashboard access / Authentication | Current request's access mode and configured authentication mode. |
| Live updates / AI assistant configured | Feature configuration, not a health check of the connection or model service. |
| Retention windows | Age from `ExecutedAt` at which a terminal record becomes eligible. Cleanup waits for a sweep, obeys batch limits, and keeps a time chain until all nodes are eligible. Null windows keep that status. |
| Delay between sweeps | Wait before the first sweep and between completed sweeps. |
| Retention limits | Batch size counts chains or occurrences, not time-chain rows. Both streams share the batch-call limit; oversized chains are kept intact. |

Runnable basic, retention, and read-only examples are documented in the
[license smoke sample](../../samples/TickerQ.Sample.LicenseSmoke/README.md#configured-dashboard-examples).

## Scheduler host controls

Overview groups scheduler state, worker threads, concurrency, and the runtime-health
link in one host card. Worker threads include idle workers; the concurrency limit is
the maximum concurrent executions per host, not a count of busy threads.

Start, Stop, and Restart require confirmation. Stop follows the host's configured
shutdown behavior. Restart re-reads pending work by restarting the scheduling loop;
the application process remains running. Controls apply to the responding host and
are hidden in read-only dashboards or when current status is unavailable. A failed
request leaves the confirmation open for retry. A changed or unavailable status
blocks confirmation until the user closes the dialog and reviews the current state.

## Diagnostics page

Open **System → Diagnostics** for scheduler state, overdue-work scan results, and,
when database execution logs are enabled, pending writes, write failures, dropped
unflushed tails, and flush timestamps. Current problems appear first with suggested
checks; historical failures remain in the counters after recovery. Overview includes
a compact runtime-health link, while About keeps configuration and retention limits.

Diagnostics is read-only and refreshes every five seconds while its page is visible
(the Overview indicator refreshes while Overview is visible). Counters describe the
responding node and reset on restart, not cluster-wide totals. Failed refreshes show
an unavailable state instead of treating cached success as current health. Timestamps
follow the toolbar's display time zone. Individual execution logs stay with each job.

## 🚀 Quick Examples

### No Authentication (Public Dashboard)

Anonymous access is an **explicit opt-in**. If no auth scheme is configured and you don't
acknowledge public exposure, startup throws. Call `AllowAnonymousDashboard()` to opt in:

```csharp
services.AddTickerQ<MyTimeTicker, MyCronTicker>(config =>
{
    config.AddDashboard(dashboard =>
    {
        dashboard.AllowAnonymousDashboard(); // deliberately public — anyone reachable can control jobs
    });
});
```

> ⚠️ A public dashboard grants full control over your jobs to anyone who can reach it. Only
> opt in when another layer (private network, authenticating gateway, mTLS) protects it.

### Basic Authentication
```csharp
services.AddTickerQ<MyTimeTicker, MyCronTicker>(config =>
{
    config.AddDashboard(dashboard =>
    {
        dashboard.WithBasicAuth("admin", "secret123");
    });
});
```

### API Key Authentication
```csharp
services.AddTickerQ<MyTimeTicker, MyCronTicker>(config =>
{
    config.AddDashboard(dashboard =>
    {
        dashboard.WithApiKey("my-secret-api-key-12345");
    });
});
```

### Use Host Application's Authentication
```csharp
services.AddTickerQ<MyTimeTicker, MyCronTicker>(config =>
{
    config.AddDashboard(dashboard =>
    {
        dashboard.WithHostAuthentication();
    });
});
```

### Use Host Authentication with Custom Policy
```csharp
services.AddTickerQ<MyTimeTicker, MyCronTicker>(config =>
{
    config.AddDashboard(dashboard =>
    {
        dashboard.WithHostAuthentication("AdminPolicy");
    });
});
```

### Dedicated OpenAPI Group
```csharp
services.AddTickerQ<MyTimeTicker, MyCronTicker>(config =>
{
    config.AddDashboard(dashboard =>
    {
        dashboard.SetGroupName("tickerq");
    });
});
```

## 🔧 Fluent API Methods

**Authentication**

- `WithBasicAuth(username, password)` - Enable username/password authentication
- `WithApiKey(apiKey)` - Enable API key authentication
- `WithHostAuthentication(policy)` - Use your app's existing auth with optional policy (e.g., "AdminPolicy")
- `WithAuthentication(auth => …)` - Composable multi-scheme builder; use `AddJwtBearer(...)` / `AddCookieLogin(...)` to set the signing key, token lifetime, and cookie behavior
- `AllowAnonymousDashboard()` - Explicitly acknowledge an unauthenticated (public) dashboard

**Deployment hardening**

- `AllowOrigins(params string[] origins)` - Exact browser origins allowed for credentialed cross-origin access (required for split-origin hosting)
- `EnableForwardedHeaders()` + `TrustForwardedProxy(ip)` / `TrustForwardedNetwork(cidr)` - Honor `X-Forwarded-*` only from trusted proxies/networks
- `SetBackendDomain(domain)` - Set backend API domain (does **not** by itself grant cross-origin trust — pair with `AllowOrigins`)
- `SetCorsPolicy(policy)` - Configure a fully custom CORS policy

**Other**

- `SetBasePath(path)` - Set dashboard URL path
- `SetGroupName(name)` - Set OpenAPI group name for dashboard endpoints

> **Deployment hardening details** — CORS allow-lists, trusted proxies, the anonymous opt-in,
> JWT signing keys, and sliding token-renewal semantics are documented in
> **[docs/security.md](../../docs/security.md)**. Unsafe combinations fail validation at
> startup rather than exposing the dashboard silently.

## 🔒 How It Works

The dashboard automatically detects your authentication method:

1. **No auth configured** → startup **fails** unless you call `AllowAnonymousDashboard()` to explicitly acknowledge public exposure
2. **Basic auth configured** → Username/password login
3. **Bearer token configured** → API key authentication
4. **Host auth configured** → Delegates to your app's auth system

## 🌐 Frontend Integration

The frontend automatically adapts based on your backend configuration:
- Shows appropriate login UI
- Handles SignalR authentication 
- Supports both header and query parameter auth (for WebSockets)

That's it! Simple and clean. 🎉

## Manual cron runs

**Run now** creates a separate occurrence and dispatches it immediately through the scheduler's worker queue. Bulk retry of a cron occurrence uses the same path. Both respect worker capacity and per-function concurrency limits; they do not change the cron expression or replace its next scheduled occurrence.

Successful manual runs finish as **Done**. **DueDone** means overdue work completed successfully through the fallback scanner, for example after an outage. Existing execution history keeps its original status. The scheduler must be running and the license must permit execution to accept an immediate run.

## Chain deletion

Choose **Cascade** (default) or **Restrict** in the root job editor. Existing chains expose the setting in the flowchart and root details, including completed chains. Restrict prevents deletion or replacement while any descendant is unfinished. It releases automatically once all descendants are Done, DueDone, Failed, Cancelled, or Skipped. Retention ages and active-job safety checks still apply. Switching the policy manually requires confirmation.

See [chain deletion behavior and storage upgrades](../../docs/chain-deletion.md) before deploying to an existing database.

### Recurring cron chains

Cron Tickers supports **Create recurring chain** and **Add chain / Edit chain** on an existing schedule. Every trigger creates an independent execution graph; its row offers **View chain** for job statuses and logs. Choose whether overlapping chains are allowed or the new trigger is skipped while a prior chain has unfinished jobs. See [recurring chain setup, storage upgrades and execution semantics](../../docs/recurring-chains.md).

Cron create/edit forms expose **Skip if already running** per schedule. See [cron overlap policy](../../docs/cron-overlap.md) for runtime scope and migration requirements.
