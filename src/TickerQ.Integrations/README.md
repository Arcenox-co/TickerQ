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
    options.UseDefinedCronApplicationNamespace("orders-production");
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
3. In **Configure**, preview the payload using sample values. Slack, Teams, and custom webhooks offer a default payload or a custom JSON template, with insertable placeholders. Headers are under **Advanced settings**; blank keeps saved values and **Remove saved headers** clears them.
4. Use **Send test** on a saved connection to send a sample message after confirmation, including when paused. No job runs. **Activity** shows recent delivery attempts and suppressed/dropped counts on this node.

Each integration and rule saves independently. Cancelling an editor discards its changes after confirmation. Removing an integration also removes any rules that use only that connection; the confirmation shows the affected count. Configure the package with `AddIntegrations` and grant dashboard access with `AllowIntegrationManagement`.

Available events are final failure (`failed`), execution timeout (`timeout_cancelled`), abandoned-job recovery (`stale_recovered`), and request-contract drift (`contract_drift`). Rules operate on the events emitted by the scheduler; they do not add alerts for every retry, successful execution, or manual cancellation.

Create an incoming webhook using [Slack's setup instructions](https://docs.slack.dev/messaging/sending-messages-using-incoming-webhooks). For Teams, configure a [Workflows webhook that accepts Adaptive Cards](https://learn.microsoft.com/microsoftteams/platform/webhooks-and-connectors/how-to/add-incoming-webhook). Use a trigger compatible with the configured webhook URL and authentication headers; the package does not acquire or refresh OAuth tokens. Assign appropriate workflow co-owners so delivery does not depend on one employee account.

URLs and header values are write-only in the dashboard API. Blank URL/headers preserve saved values; `{}` clears headers. Changing provider requires a new URL and does not reuse the previous provider's headers. Saves use a version token, so concurrent edits fail with a reload prompt instead of overwriting another operator's changes.

## Custom Slack, Teams, and webhook payloads

In **Integrations → edit a connection → Payload**, choose **Custom JSON** to edit that connection's message. The editor starts with a Slack Block Kit message, a Teams Adaptive Card message, or a generic webhook example. Use placeholders to insert job details, then **Preview payload** to see the rendered JSON with sample values. Preview does not send a message. Save the connection to use the template for alerts and test deliveries; select **Default** and save to restore the built-in format.

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

Supported tokens: `eventId`, `application`, `environment`, `event`, `function`, `reason`, `tickerId`, `tickerType`, `node`, `retryCount`, `retries`, `occurredAtUtc`, `dashboardUrl`. Links open the configured dashboard URL. The event contract does not currently carry enough information to deep-link every cron occurrence or chain step.

## Delivery behavior and limits

- Job execution only enqueues the event. A background worker sends HTTP requests, with at most four destinations in parallel.
- Defaults: 1,000 queued events and 10 seconds per HTTP attempt. A full queue drops the new event, increments the visible counter, and logs a warning. Queue capacity and timeout are host options.
- At most three attempts per delivery. Network failures, timeouts, HTTP 408/429, and 5xx are retried with 1s and 5s delays. `Retry-After` up to 60s is honored; longer delays are reported as failed. Other 4xx and redirects are not retried. HTTP success means the endpoint accepted the request, not that a person saw it.
- Overlapping rules send once per destination per event. The smallest matching cooldown wins. Cooldown applies to attempts for the same destination, event kind, and function, including failed attempts. Zero disables it.
- **Settings are persistent; delivery is best effort.** Pending events, cooldowns, counters, and the latest 50 delivery results are local to each process and reset on restart. There is no durable outbox, replay, cluster-wide cooldown, or exactly-once delivery. HTTP retries can duplicate a message; custom receivers can deduplicate with `eventId`.
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
