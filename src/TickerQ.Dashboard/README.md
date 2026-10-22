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
| Worker model | The built-in scheduler uses async consumers that wait without holding native threads, so idle worker retirement is unnecessary. |
| Stale-job recovery configured | Configuration only; recovery additionally requires provider lease support. |
| Your dashboard access / Authentication | Current request's access mode and configured authentication mode. |
| Live updates / AI assistant configured | Feature configuration, not a health check of the connection or model service. |
| Retention windows | Age from `ExecutedAt` at which a terminal record becomes eligible. Cleanup waits for a sweep, obeys batch limits, and keeps a time chain until all nodes are eligible. Null windows keep that status. |
| Delay between sweeps | Wait before the first sweep and between completed sweeps. |
| Retention limits | Batch size counts chains or occurrences, not time-chain rows. Both streams share the batch-call limit; oversized chains are kept intact. |

Runnable basic, retention, and read-only examples are documented in the
[license smoke sample](../../samples/TickerQ.Sample.LicenseSmoke/README.md#configured-dashboard-examples).

## Scheduler host controls

Overview groups scheduler state, running attempts, queued attempts, and the runtime-health
link in one host card. Running attempts are shown against the host's concurrency limit;
each chain step counts individually. Diagnostics separates function-capacity waits, retry
delays, and active workflows. Waiting for a function permit or a retry does not use an
execution slot. Workflow counts overlap the attempt counts and should not be added to them.

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

`SetBackendDomain("https://api.example.com")` accepts an HTTP(S) origin, including
an optional port, without a path, credentials, query or fragment. REST, authentication,
AI streaming/history and SignalR use that origin plus the dashboard's complete base
path (including the reverse proxy path prefix). Browser routes and assets stay under
the frontend mount. An HTTPS frontend requires an HTTPS backend. Requests include
cookies and the dashboard bearer token when present; cross-origin requests still
require the backend's explicit `AllowOrigins` configuration, and browser cookie
SameSite restrictions still apply. Bearer session storage is scoped to the backend
and mount; upgrading from an unscoped token requires a fresh login.

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

## Saved templates, drafts, and tests

The dashboard can save a complete chain as a reusable template. Templates keep
the schedule configuration and graph shape; request payloads are optional so teams can avoid
copying customer data or secrets. Existing time and cron chains expose **Save template**, and the
chain builder lists saved templates alongside its starter layouts. Saved templates can be opened
in the chain builder and updated in place with optimistic version checks. Template names are unique
per application namespace and scheduler type; matching ignores case and repeated whitespace. The
**Saved work** view can search definitions and sort them by creation date.

Time-ticker drafts are saved definitions, not ticker rows. The scheduler cannot acquire them and
they do not appear as `Idle` jobs. A draft can be edited and later activated; activation publishes
the complete time-ticker graph once using a durable, versioned claim that is safe across dashboard
replicas. Leaving a new time-ticker builder can save the work as a draft; a configured chain can
also be saved as a template. Cron chains use templates rather than drafts.

**Test once** and **Test chain** execute the selected functions through the normal execution path
but retain their state only in the bounded test-run registry used by the current dashboard view.
They do not create a time ticker, cron ticker, or cron occurrence. Use **Run now** when durable
execution history is required.

EF Core deployments must add the `TickerQDefinitions` table from the current model before starting
the upgraded runtime. MongoDB provisions its collection/index, Redis uses partition-scoped keys,
and the in-memory provider retains saved definitions only for the lifetime of the process. See the
[store upgrade guide](../../docs/upgrading-cron-time-tickers.md#add-saved-templates-and-drafts).

## Manual cron runs

**Run now** on a cron definition creates a separate occurrence and dispatches it immediately through the scheduler's worker queue. **Re-run** on an existing occurrence instead revives that occurrence in place, preserving its ID and replacing its previous status, progress, error and result. A recurring-chain occurrence retries its existing durable graph. Neither action changes the cron expression or replaces its next scheduled occurrence.

Successful manual runs finish as **Done**. **DueDone** means overdue work completed successfully through the fallback scanner, for example after an outage. Existing execution history keeps its original status. The scheduler must be running and the license must permit execution to accept an immediate run.

## Chain deletion

Choose **Cascade** (default) or **Restrict** in the root job editor. Existing chains expose the setting in the flowchart and root details, including completed chains. Restrict prevents deletion or replacement while any descendant is unfinished. It releases automatically once all descendants are Done, DueDone, Failed, Cancelled, or Skipped. Retention ages and active-job safety checks still apply. Switching the policy manually requires confirmation.

See [chain deletion behavior and storage upgrades](../../docs/chain-deletion.md) before deploying to an existing database.

### Recurring cron chains

Cron Tickers supports **Create recurring chain** and **Add chain / Edit chain** on an existing schedule. Every trigger creates an independent execution graph; its row offers **View chain** for job statuses and logs. Choose whether overlapping chains are allowed or the new trigger is skipped while a prior chain has unfinished jobs. See [recurring chain setup, storage upgrades and execution semantics](../../docs/recurring-chains.md).

Cron create/edit forms expose **Skip if already running** per schedule. See [cron overlap policy](../../docs/cron-overlap.md) for runtime scope and migration requirements.

## AI assistant

Configure an `IChatClient` through the existing dashboard builder:

```csharp
options.AddDashboard(dashboard =>
{
    dashboard.AddAssistant(ai => ai
        .UseChatClient(serviceProvider => serviceProvider.GetRequiredService<IChatClient>())
        .WithMaxToolIterations(8));
    // Optional: reuse your host application's authorization policy.
    dashboard.AllowIntegrationManagement(context => context.User.IsInRole("Admin"));
});
```

The assistant can inspect registered functions and their request contracts, executions,
cron definitions, chain steps, retained execution logs, and node-local runtime health.
Failed and cancelled executions also expose **Investigate with AI**. The built-in run
investigator assembles bounded exception, log, retry and workflow evidence; request
payload values are excluded. Applications can optionally resolve a small source excerpt
from a local checkout, SourceLink artifact or private repository without exposing repository
credentials to the browser. See the [AI run investigator guide](../../docs/ai-run-investigator.md).

The AI page is an operations workspace with **Overview**, **Chat**, and **Investigations**.
When `AddAssistantHistory()` is enabled on the EF operational store, the overview shows saved
diagnoses, unread findings, repeated failure patterns, and later successful recoveries.
The complete TickerQ EF model already contains the assistant tables; enabling the service later
does not change the model or require another migration.
Investigations can be marked as read, resolved, or reopened; those review changes persist in the
operational store.
Investigations are shared within the application namespace; conversations remain private to the
authenticated user. The investigator can also be explicitly configured to diagnose exhausted
failures in the background. Automatic mode is disabled by default, bounded by batch/hourly limits,
and uses a renewable database lease per failure fingerprint. Scheduler nodes sharing the same
namespace and operational store also share one rolling hourly model-call budget. Repeated failures
reuse the completed diagnosis without another model call.

For an explicitly allow-listed local checkout:

```csharp
assistant.ConfigureRunInvestigator(investigator => investigator
    .UseLocalSourceContext(builder.Environment.ContentRootPath, source => source
        .WithRevision(Environment.GetEnvironmentVariable("GIT_COMMIT_SHA") ?? "local-working-tree")
        .MapFunction("Payments.Charge", "Jobs/PaymentJob.cs")));
```

Local source lookup never scans the checkout. Unmapped files remain unavailable, and production
repository adapters should resolve an immutable deployed revision with server-side credentials.
With `TickerQ.Integrations` configured, dashboard viewers can also inspect destinations,
rules, monitoring state and recent delivery outcomes. The Integrations page provides
health and delivery status to read-only users and users without management permission.
Editing settings, drafting integration changes and sending tests still require
integration-management permission and a writable dashboard.
Webhook URLs, headers, message templates and delivery response bodies are excluded
from integration tool results.

The assistant prepares review cards for:

- One-time jobs and one-time chains, saved as non-executable drafts.
- Cron schedules and recurring chains.
- Cron schedule updates, including enabling/disabling a schedule.
- Execution retries, cancellation, and deletion.
- New alert rules and deadline, long-running or consecutive-failure monitors, using
  existing integration destinations and optional recovery notifications.

Examples: “Read the logs for the most recent failed execution”; “Run Prepare then
Publish every weekday at 08:00”; “Alert Operations if GenerateInvoices fails three
times”; “Monitor GenerateInvoices: finish by 08:00 Europe/Belgrade, with ten minutes
of grace and a recovery message.”

**Changes require an explicit card action.** One-time jobs and chains use **Save draft**;
the saved card returns an **Open draft** link to the chain builder, where the operator can
edit and activate it. Saving the draft does not enqueue or execute any job. Other changes
use **Confirm**. Tools never write data or send test messages by themselves. Card actions
use the normal authenticated dashboard APIs, including their read-only, license and
integration-management checks. Execution cards recheck observed status
and execution timestamps before submitting; the underlying APIs still enforce current
state. Integration proposals retain the configuration version and fail on concurrent
edits. Read-only users receive no proposal tools.

A running cancellation requests cooperation through the handler's cancellation token;
it does not prove that the job stopped. Retrying a cron occurrence or standalone time
ticker reuses its persisted execution ID. Retrying a recurring-chain occurrence resets
and dispatches its existing durable graph. Deletion cards explain their permanent
effect. Monitors track individual function executions, including chain steps, rather
than completion of a whole workflow.

Cron proposals show the scheduler time zone. Deadline monitors have their own explicit
time zone. Configure new destinations and edit webhook secrets/templates in Integrations.

Logs and job data inspected by the assistant are sent to your configured model provider
and may appear in conversation history. Apply your application's log redaction and
model/data policies accordingly. Log reads verify that the execution belongs to the
current runtime and return at most 100 retained lines and 24,000 characters; retention,
truncation and storage availability can limit what the assistant sees. Request contracts
are capped at 12,000 characters. Chat requests use the last 20 turns, with a 128 KB body
limit and limits of 16,384 characters per message and 65,536 in total. AI proposals cap
chains at 100 steps, ten levels, and five children per step.

Assistant history uses a stable authenticated identity, separate from the display
name. Host authentication requires `sub` or `ClaimTypes.NameIdentifier` on the
authenticated identity; keys include its issuer and authentication scheme. Basic
authentication uses the validated username and realm. Dashboard-issued cookie and
bearer tokens share an issuer-scoped subject. A custom validator can populate an
authenticated `ClaimsIdentity` with a stable subject; a boolean validator alone
does not establish an individual history owner. Anonymous sessions and identities
without a stable subject can chat, but persist no history in the server or browser.
Custom `IAuthService` implementations should return a stable, scoped `AuthResult.UserId`.

Browser fallback transcripts are isolated by user, backend and dashboard mount,
and are cleared on logout/session expiry. Unscoped legacy browser transcripts are
discarded; server history saved under legacy display-name keys is not automatically
migrated because ownership cannot be inferred safely. Conversation switches cancel
prior loads, ignore late responses, and disable sending until the selected thread loads.

### Dashboard memory usage

Built-in providers use dashboard metadata projections for lists, history, graphs and
chain summaries. EF Core and MongoDB select fields before materializing rows, excluding
request payloads and child navigation graphs. Chain summaries read flat descendants for
the displayed roots, with the existing 500-node/20-level limit per chain. In-memory
metadata reads retain scalar snapshots and child-existence markers. Ordinary entity
queries and request/detail editing APIs retain their original payload behavior. Custom
providers can opt into `ITickerProjectionQueryable<T>`; providers without that capability
continue using the compatible entity-query path.

Redis reads payloads in batches of at most 128 documents and keeps bounded sort candidates.
Each scan retains unique IDs to avoid duplicate rows during Redis rehashing; that metadata
still grows with the number of stored IDs. Deep sorted pages can require additional scans
and do not provide a snapshot during concurrent writes. MongoDB's chain/standalone filter reads distinct
parent IDs within the runtime partition without hydrating child documents. Dashboard
execution-history merging and graph aggregation still scale with the number of matching
metadata rows, so time filters and job retention remain useful for large histories.

The browser remembers at most 2,048 recent row IDs for highlight animations, expires
those IDs after the 30-second freshness window, and cleans its single animation timer
on navigation. Assistant transcripts retain the latest 40 messages and 20 proposal cards,
with a 65,536-character display limit per message. Proposal cards use stable identities
so an asynchronous confirmation still updates the correct card after older items expire.
Server-side conversation history stores messages, proposal cards, confirmation state and
the saved-draft link. Reopening a conversation therefore restores pending cards as well as
confirmed cards and their **Open draft** action. Custom text-only history stores that do not
implement `IAssistantProposalHistoryStore` remain compatible, but do not persist cards.
Browser fallback history retains the same 40-message limit and proposal cards in that browser.

Dashboard pages load on demand. Navigation keeps the shell visible and shows a loading
indicator while the requested page's JavaScript is downloaded.
