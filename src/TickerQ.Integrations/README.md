# TickerQ.Integrations

Optional job alerts for Slack, Teams Workflows, and custom JSON webhooks. Operators manage destinations, function filters, event types, and payload templates in the TickerQ dashboard. Settings are encrypted and saved in the existing operational store, scoped to the application's runtime namespace.

## Enable once in the host

Add `TickerQ.Integrations` alongside the matching versions of TickerQ, the dashboard, and your storage provider. The core scheduler and dashboard do not depend on this package.

```csharp
using Microsoft.AspNetCore.DataProtection;
using TickerQ.DependencyInjection;
using TickerQ.Dashboard.DependencyInjection;
using TickerQ.Integrations;
using TickerQ.Integrations.DependencyInjection;

// Reuse your application's existing persistent Data Protection configuration.
// Every replica needs the same application name and access to the same key ring.
builder.Services.AddDataProtection()
    .SetApplicationName("orders-production")
    .PersistKeysToFileSystem(new DirectoryInfo("/var/lib/orders/data-protection"));

builder.Services.AddTickerQ(options =>
{
    options.UseApplicationNamespace("orders-production");
    // Keep your existing operational-store and scheduler configuration here.
    options.AddDashboard(dashboard =>
    {
        dashboard.WithHostAuthentication("SchedulerOperators");
        dashboard.AllowIntegrationManagement(context =>
            context.User.IsInRole("SchedulerAdmin"));
    });

    options.AddIntegrations(integrations =>
    {
        integrations.ApplicationName = "Orders";
        integrations.EnvironmentName = "Production";
        integrations.DashboardUrl = "https://orders.example.com/tickerq/dashboard";
        // Optional: restrict outbound destinations to exact DNS host names.
        // integrations.AllowedHosts = ["hooks.slack.com", "alerts.example.com"];
        // integrations.ReasonSanitizer = reason => RedactApplicationSecrets(reason);
    });
});
```

`options.AddIntegrations()` also works with defaults. It uses the same builder registration pipeline as the other TickerQ packages, supports custom ticker entity types, and can appear before or after `AddDashboard` and `AddOperationalStore`.

Configure the host's authentication and `SchedulerOperators` authorization policy as usual. Integration management is denied by default. The permission callback is additional to dashboard authentication, read-only restrictions, and the existing license write gate. Grant it only to operators allowed to configure outbound HTTP destinations. With dashboard Basic authentication, the authenticated username is available through `context.Items["auth.username"]`.

Protect the key-ring directory with service-account permissions, persist it across restarts, and share it across replicas. Use your deployment's supported Data Protection key-encryption mechanism for keys at rest. Losing the key ring makes existing destinations unreadable; back it up with the operational database. Do not use an ephemeral key provider in production.

Use **either** this package **or** `NotifyFailuresViaWebhook(...)`. Registering both fails with a configuration error. To migrate, remove the old option, then add its URL, headers, and an appropriate rule in Integrations. Opting into this package replaces the `ITickerQFailureNotifier` registration.

## Storage and schema

| Operational provider | Settings storage |
| --- | --- |
| EF Core | One `IntegrationSettings` row per runtime namespace; uses ordinary EF operations, without PostgreSQL-specific SQL. |
| MongoDB | One document per namespace in `<TimeTickersCollectionName>_IntegrationSettings`; created on first save for new installations. |
| Redis operational store | One `<runtimePrefix>integrations:settings` hash, atomically updated by Lua, without an expiration. Configure Redis persistence and an eviction policy appropriate for operational data. |
| In-memory | Process-local settings, lost on restart. Intended for development. |

For a new EF Core installation, **generate and apply a migration before enabling integrations**:

```sh
dotnet ef migrations add AddTickerQIntegrationSettings --context AppDbContext
dotnet ef database update --context AppDbContext
```

The TickerQ model customizer and standalone context include the mapping automatically. If you use `ConfigurationType.IgnoreModelCustomizer` and maintain the model manually, add this alongside the existing TickerQ mappings before generating the migration:

```csharp
modelBuilder.ApplyConfiguration(
    new TickerQ.EntityFrameworkCore.Configurations.IntegrationSettingsConfigurations("ticker"));
// Replace "ticker" with your configured TickerQ schema.
```

A Redis cache layered over EF does not move these settings into Redis: the operational provider remains the source of truth. Custom providers can implement `ITickerIntegrationSettingsStore`; unsupported providers report an explicit error. All replicas sharing settings must use the same formatters and host-level endpoint policy.

### Upgrade existing notification settings

The former Notifications feature is now Integrations throughout the package, configuration contracts, dashboard API, and storage. Update callers to `AddIntegrations`, `AllowIntegrationManagement`, and the `Integration*` types in `TickerQ.Utilities.Integrations`. Dashboard API paths are now `/integrations`, `/integrations/preview`, and `/integrations/destinations/{id}/test` under the configured dashboard base path plus `/api/dashboard`. The old `/notifications` routes are not aliases.

Before upgrading an existing installation, stop all old replicas and back up the settings and Data Protection key ring. Keep the existing runtime namespace, Data Protection application name, and keys. Rename the storage before starting the new version:

- **EF Core:** generate a migration for your application context. Review the generated operations: changing both the CLR entity and table name can scaffold a destructive drop/create. Replace those operations with a table rename from `NotificationSettings` to `IntegrationSettings` and a primary-key rename from `PK_NotificationSettings` to `PK_IntegrationSettings` (drop/add the constraint, retaining its `ApplicationNamespaceKey` column). Keep your configured schema. The [Console sample migration](../../samples/TickerQ.Sample.Console/Migrations/20261018190000_RenameIntegrationSettings.cs) shows the provider-independent EF operations; matching migrations are included in the other samples. Existing encrypted payloads and version tokens remain intact.
- **MongoDB:** rename `<TimeTickersCollectionName>_NotificationSettings` to `<TimeTickersCollectionName>_IntegrationSettings` in the same database using `RenameCollectionAsync` with `DropTarget = false`. This preserves every namespace's document. If both collections exist, resolve them before migration; do not drop the target.
- **Redis operational store:** for each existing runtime prefix, use `RENAMENX <runtimePrefix>notifications:settings <runtimePrefix>integrations:settings`. Preserve the entire prefix, including the runtime partition/hash tag. This keeps the version and encrypted payload together. If both keys exist, resolve them before migration; do not overwrite the target. This step is unnecessary when Redis is only a cache over EF.
- **In-memory:** settings do not survive a process restart; recreate them in the dashboard.

New writes use the encryption purpose `TickerQ.Integrations.Configuration.v1`. On the first read, the service can decrypt a legacy `TickerQ.Notifications.Configuration.v1` payload and re-encrypt it under the new purpose. This update uses the stored version token, so it cannot overwrite a concurrent edit. Existing destinations, secrets, and rules are preserved; a stale editor must reload after the version changes. Invalid ciphertext fails without clearing settings. Do not run old and new versions together: old versions cannot decrypt upgraded records. A rollback needs the matching pre-upgrade settings and key-ring backup, not just the reverse table rename.

## Configure in the dashboard

1. Open **System → Integrations → Connections** and choose Slack, Microsoft Teams, or Custom webhook. Give the connection a name and enter its HTTPS webhook URL. Save the integration in its editor.
2. Choose **Add rule** on the connection, or open **Alert rules → Create rule**. Select integrations and events, then choose all functions or specific names/patterns. Exclusions and cooldown are under advanced settings. Save the rule to start matching events.
3. In **Configure → Message templates**, choose **Job alert**, **Monitor alert**, or **Recovery**. Each has its own default or custom JSON template. Previews update with sample values without sending a message. Headers are under **Advanced settings**; blank keeps saved values and **Remove saved headers** clears them.
4. Open **Monitoring → Create monitor** to configure a daily deadline, long-running execution, or repeated-failure condition for one function. Select its connections and whether to send recovery messages.
5. Use **Send test** on a saved connection to send a sample message after confirmation, including when paused. No job runs. **Activity** combines recent delivery attempts from this node with persisted monitoring delivery history. Suppressed/dropped counters apply to event alerts on this node.

Each integration and rule saves independently. Cancelling an editor discards its changes after confirmation. Removing an integration also removes any alert rules or monitors that use only that connection; the confirmation shows the affected count. Configure the package with `AddIntegrations` and grant dashboard access with `AllowIntegrationManagement`.

Available events are final failure (`failed`), execution timeout (`timeout_cancelled`), abandoned-job recovery (`stale_recovered`), and request-contract drift (`contract_drift`). Rules operate on the events emitted by the scheduler; they do not add alerts for every retry, successful execution, or manual cancellation.

Create an incoming webhook using [Slack's setup instructions](https://docs.slack.dev/messaging/sending-messages-using-incoming-webhooks). For Teams, configure a [Workflows webhook that accepts Adaptive Cards](https://learn.microsoft.com/microsoftteams/platform/webhooks-and-connectors/how-to/add-incoming-webhook). Use a trigger compatible with the configured webhook URL and authentication headers; the package does not acquire or refresh OAuth tokens. Assign appropriate workflow co-owners so delivery does not depend on one employee account.

URLs and header values are write-only in the dashboard API. Blank URL/headers preserve saved values; `{}` clears headers. Changing provider requires a new URL and does not reuse the previous provider's headers. Saves use a version token, so concurrent edits fail with a reload prompt instead of overwriting another operator's changes.

## Custom Slack, Teams, and webhook payloads

In **Integrations → Configure → Message templates**, choose **Job alert**, **Monitor alert**, or **Recovery**. Each category can use the provider default or its own custom JSON. **Customize** starts with the current default layout and placeholders; **Use default** restores only the selected category. Switching categories preserves unsaved edits in the others.

Default Slack messages use a status heading, compact detail fields, a reason, an incident/job reference and UTC timestamp, and an optional dashboard button. Teams uses the same content in an Adaptive Card with an alert/recovery heading color. Function monitors omit unrelated worker/retry fields and show their condition, threshold and observed result instead. Default webhooks retain their event envelope and add structured monitoring context.

Preview updates automatically. Choose a long-running, repeated-failure, or deadline sample for monitoring and recovery. **View JSON** shows the rendered request; the local layout preview shows supported text elements and never loads images or follows payload links. The receiving application controls the final rendering. Previews do not send requests to the integration.

Existing connections with a single `payloadTemplate` continue to use it for every category. The editor copies it into all three categories when saving an edit. API callers can set `messageTemplates: { jobAlert, monitoringAlert, recovery }`; a null/blank category uses its provider default, and the explicit object replaces the legacy shared template. Settings use the existing encrypted storage, so no database migration is needed.

Slack templates accept message `text`, `blocks`, or `attachments`; see [Slack's incoming webhook format](https://docs.slack.dev/messaging/sending-messages-using-incoming-webhooks/). Teams templates use a `type: "message"` envelope with Adaptive Card attachments; see [Microsoft's Adaptive Card webhook format](https://learn.microsoft.com/en-us/microsoftteams/platform/webhooks-and-connectors/how-to/connectors-using#send-adaptive-cards-using-an-incoming-webhook). The server checks JSON, placeholders, size, and the basic provider envelope before saving. It does not validate every Block Kit or Adaptive Card element; the receiving platform still validates those. Slack's channel and sender identity remain controlled by the webhook's app configuration.

A custom destination sends an HTTP POST with `application/json`. Leave the template empty for the standard event envelope, or supply a JSON object:

```json
{
  "text": "{{application}} / {{environment}}: {{function}} — {{event}}",
  "details": {
    "reason": "{{reason}}",
    "jobId": "{{tickerId}}",
    "eventId": "{{eventId}}",
    "node": "{{node}}",
    "occurredAtUtc": "{{occurredAtUtc}}"
  },
  "dashboard": "{{dashboardUrl}}"
}
```

Placeholders work only inside JSON string values, including nested objects and arrays. Values are escaped as JSON; no scripts, expressions, or property-name substitutions run. Unknown tokens are rejected. Numeric placeholders are strings; literal JSON numbers and booleans retain their types.

Supported tokens: `eventId`, `application`, `environment`, `event`, `function`, `reason`, `tickerId`, `tickerType`, `node`, `retryCount`, `retries`, `occurredAtUtc`, `dashboardUrl`, `monitorId`, `monitorName`, `incidentId`. These monitoring identifiers are populated for monitoring messages and empty for ordinary job events.

Additional placeholders:

- Presentation: `title`, `severity` (`error`, `warning`, `success`), `statusIcon`, `statusColor` (Teams color), and `occurredAt` (readable UTC timestamp).
- Monitoring summaries: `condition`, `threshold`, and `observed`, suitable for one template shared by different monitor conditions.
- Structured monitoring values: `monitorKind`, `thresholdSeconds`, `observedDurationSeconds`, `failureCount`, `failureThreshold`, `deadlineUtc`, `timeZoneId`, and `graceMinutes`.

Unavailable values become empty strings in custom templates and null in the default webhook. Default webhook counts/durations remain JSON numbers; custom placeholders inside strings are strings. Duration uses persisted execution time for completed runs, and time since first observed InProgress for active runs. Failure counts are retained observed counts and can be lower bounds. Recovery does not invent a completed job duration. `deadlineUtc` is the scheduled deadline before the separately reported grace period.

An incident and its recovery share `incidentId`; each transition has its own `eventId`. Links open the configured dashboard URL. The event contract does not currently carry enough information to deep-link every cron occurrence or chain step.

## Job monitoring and deadlines

Monitoring is part of **`TickerQ.Integrations`** and uses the existing `AddIntegrations` registration, dashboard permission, connections, payload templates, and HTTP transport. No additional package or registration is required. Create rules in **System → Integrations → Monitoring**. No monitoring alerts are produced until a monitor is enabled.

| Condition | Behavior |
| --- | --- |
| Daily deadline | Require a successful execution since local midnight by a chosen time, with a time zone and optional grace period. For example, monitor `Invoices.Generate` by `08:00`, `Europe/Berlin`, with 10 minutes of grace. It can alert even when no execution row exists. A late success can send recovery. |
| Long-running execution | Alert when an active or completed execution meets the configured duration. Active timing starts at the first observation; completed runs use their recorded duration, including retry waits. This catches slow jobs that finish between checks. Queued time is excluded. A slow successful completion can produce an alert followed by recovery in the same check. It never cancels the job. |
| Repeated failures | Alert after 2–20 consecutive final failures within a 1–168 hour lookback. Retries are not separate failures. Successful or cancelled executions break the streak; skipped executions are ignored. Recovery requires a subsequent success, not just old failures aging out. |

Each monitor targets **one exact function name**, across its time tickers and cron occurrences in the current runtime namespace. Chain steps count as individual executions; a parent's success does not prove its entire chain finished. For a chain completion deadline, monitor the final successful step. Recurring-chain dispatcher occurrences are excluded to avoid counting the same function twice.

Daily deadlines run every day. New or edited monitors start with the next deadline after their first check and do not alert retroactively. Daylight-saving gaps advance to the first valid minute; repeated local times use the later occurrence. A successful run during grace counts as meeting the expectation. Monitor time zones are independent of the dashboard display time zone.

Checks default to approximately every 30 seconds. The interval can be changed within the existing registration:

```csharp
options.AddIntegrations(integrations =>
    integrations.MonitoringInterval = TimeSpan.FromSeconds(30));
```

The supported range is 5 seconds to 5 minutes. Active jobs still have observation delay. Terminal executions are processed in completion order using a persisted checkpoint, so three failures followed by success between checks produce both the incident and recovery. A deadline uses the actual cutoff including grace; a late success produces recovery even when both are first observed together. Equal completion timestamps have no portable execution order: a success or cancellation conservatively breaks the failure streak for that timestamp.

Reads use provider-independent queries in batches of up to 512 completions, with a one-second catch-up interval while more history remains. Timestamp groups are kept together; a group exceeding 512 produces an unavailable check instead of silently skipping rows. New monitors establish their baseline from retained history and start replay after their first check; an existing failure streak can alert immediately. Keep terminal rows until monitoring has processed them, including any delivery backlog. Deleting or rerunning a row before it is observed can remove its previous outcome. A captured success remains in monitor state after retention removes its row. Failed queries do not advance the checkpoint or confirm recovery. Editing a monitor restarts its observation window and discards pending deliveries for its previous version.

### Persistent incident state and delivery

- EF Core and MongoDB use a **separate record in the existing `IntegrationSettings` table/collection**, keyed by `monitor:<runtime-partition hash>`. No further schema migration is needed once the integrations settings table exists. Redis uses `<runtimePrefix>integrations:monitoring`. In-memory state is process-local and disappears on restart.
- State is encrypted with Data Protection purpose `TickerQ.Integrations.Monitoring.v1`. Retain/share the same application name and key ring as configuration. State updates use version comparisons; a one-minute scan lease coordinates replicas without PostgreSQL-specific behavior. Custom providers must explicitly implement the monitoring storage capability; otherwise the dashboard reports it unavailable.
- Each monitor opens one incident and sends one transition per enabled destination. Optional recovery is queued when success confirms recovery and the monitored condition is clear. Monitoring selects destinations directly; it does not need a separate Alert rule or use event-rule cooldowns.
- A bounded persistent queue holds up to 256 delivery attempts. Detected transitions are saved first, including paired alert/recovery transitions, even when that queue is full. Each monitor retains at most one bounded observation batch of transitions and waits for them to enter the delivery queue before reading another batch. The checkpoint and transition records commit together. Pending counts include both stages; restart and queue pressure do not discard already detected incidents. Delivery runs separately from checks.
- Each pending delivery has a ten-minute expiring lease. Failed delivery batches are retried after one and two minutes, with at most three batches; each batch uses the HTTP transport's retry policy below. Results appear in Activity. A crash after remote acceptance can cause a duplicate after lease expiry: receivers can deduplicate using the stable `eventId`. This is not exactly-once delivery or guaranteed escalation.
- Persisted monitoring delivery history is bounded to the latest 50 results and seven days. It is operational feedback, not a permanent audit log. Pausing/removing a monitor or connection discards its pending deliveries; an already in-flight HTTP request may finish.

Limits: 20 monitors per namespace, at most 200 matching cron definitions and 1,000 active executions per function. Exceeding query limits produces an unavailable check instead of a false healthy result.

Monitoring uses `ReadMonitoringSnapshotAsync`, a dedicated provider contract for bounded execution metadata. It returns only IDs, status, completion time, elapsed duration and execution generation; it does not materialize request payloads, exception text, results or entity graphs. EF Core applies partition/function/time filters, ordering, limits and projections through LINQ in the database, with no provider-specific SQL. MongoDB applies equivalent server-side projections and limits. These query changes require no schema migration. Custom providers implementing monitoring must also implement this read contract; unsupported reads report an unavailable check.

The Redis operational provider uses cursor scans and reads one stored JSON aggregate at a time, including nested chain steps, retaining at most 513 completed and 1,001 active metadata entries. Payload fields are skipped during parsing. Redis still transfers each complete JSON value, so peak read memory depends on the largest stored aggregate, and scan cost grows with retained history. This is a bounded-memory scan, not an indexed history query. Account for that cost when choosing retention and the monitoring interval on large Redis stores. The in-memory provider scans its existing rows without cloning payloads or rebuilding chain graphs.

Monitoring runs inside your application. **If every replica is down, no checks or alerts can run until one returns.** Use an external uptime monitor for application-wide outages. Use the same package version and monitoring configuration across replicas.

## Event alert delivery behavior and limits

- Job execution only enqueues the event. A background worker sends HTTP requests, with at most four destinations in parallel.
- Defaults: 1,000 queued events and 10 seconds per HTTP attempt. A full queue drops the new event, increments the visible counter, and logs a warning. Queue capacity and timeout are host options.
- At most three attempts per delivery. Network failures, timeouts, HTTP 408/429, and 5xx are retried with 1s and 5s delays. `Retry-After` up to 60s is honored; longer delays are reported as failed. Other 4xx and redirects are not retried. HTTP success means the endpoint accepted the request, not that a person saw it.
- Overlapping rules send once per destination per event. The smallest matching cooldown wins. Cooldown applies to attempts for the same destination, event kind, and function, including failed attempts. Zero disables it.
- **Event-alert settings are persistent; event delivery is best effort.** Pending events, cooldowns, counters, and the latest 50 delivery results are local to each process and reset on restart. These event alerts have no durable outbox, replay, cluster-wide cooldown, or exactly-once delivery. Monitoring transitions use the separate persistent queue described above. HTTP retries can duplicate a message; custom receivers can deduplicate with `eventId`.
- Limits: 32 destinations, 100 rules, 16 headers per destination, 16,000 template characters, 24 KB rendered payload, and 256 KB dashboard request.
- HTTPS is required. Redirects and HTTP proxies are disabled. DNS addresses are checked and the connection uses a validated address. Loopback/private destinations are blocked unless the host explicitly enables `AllowPrivateEndpoints`; this option cannot be changed from the dashboard.
- Job request bodies and stack traces are not included. Failure reasons are flattened and truncated to 512 characters; that is **not** secret detection. Configure `ReasonSanitizer` if exception messages can contain application secrets or personal data. Delivery logs omit URLs, headers, response bodies, and exception messages.

Use your metrics/logging alert system for incidents that require durable alert history or guaranteed escalation.

## Extend to another application

Register a formatter with a unique stable name. It appears in the dashboard provider selector automatically, sharing the same storage, rules, and guarded HTTP transport:

```csharp
builder.Services.AddSingleton<IIntegrationPayloadFormatter, MyAppFormatter>();

public sealed class MyAppFormatter : IIntegrationPayloadFormatter
{
    public string Name => "my-app";

    public System.Text.Json.Nodes.JsonObject Format(
        TickerQ.Utilities.Integrations.IntegrationDestination destination,
        IntegrationMessage message) => new()
    {
        ["summary"] = $"{message.Event.Function}: {message.Event.Kind}",
        ["reference"] = message.EventId.ToString()
    };
}
```

Formatters must be thread-safe and must not perform network I/O. This extension point supports applications accepting JSON over HTTPS; OAuth flows, non-HTTP transports, and file attachments need additional adapters.

Applications without the dashboard can resolve `ITickerIntegrationAdminService` from DI to read/redact settings, save with the returned version, preview, or send an explicit test. Enforce equivalent authorization if exposing those methods through another API.
