# AI run investigator

The dashboard assistant can investigate one failed or cancelled execution from its exception
panel. Operators can also ask it to inspect a skipped execution by supplying that execution's
exact id in **Chat AI**. The panel action opens a reviewable prompt; nothing is sent to the AI
provider until the operator submits it.

The investigator reads a bounded evidence set from the same authenticated dashboard backend:

- execution status, timing, retries, and node holder;
- the structured exception and application stack captured by TickerQ;
- up to 50 retained execution-log lines;
- up to 50 related workflow steps;
- an optional source excerpt supplied by the application.

Request payload values are not included. Log, exception, and source text is treated as untrusted
data by the assistant, and proposed retry or cancellation actions still require the existing
confirmation card.

The **AI Operations** page has three views:

- **Overview** shows unread investigations, diagnoses completed today, detected
  recoveries, recent investigations, and repeated function/classification patterns;
- **Chat** keeps the operational assistant and its confirmation cards;
- **Investigations** provides searchable, namespace-scoped diagnoses with **Unread**, **Read**,
  and **Resolved** states. Review changes are saved to the operational store, and resolved items
  can be reopened. A saved investigation can be continued in Chat without losing its original
  evidence.

Conversation history is private to the authenticated user. Investigations are operational records
shared by authorized dashboard users in the same TickerQ application namespace. A unique
namespace/execution key prevents two dashboard nodes from purchasing duplicate diagnoses.

## Configuration

The investigator is available whenever the dashboard assistant is configured. Limits and
redaction can be adjusted alongside the existing chat client:

```csharp
options.AddDashboard(dashboard =>
{
    dashboard.AddAssistant(assistant =>
    {
        assistant.UseChatClient(chatClient)
            .WithModelName("operations-model")
            .ConfigureRunInvestigator(investigator => investigator
                .WithMaxLogLines(40)
                .WithMaxChainSteps(40)
                .RedactEvidenceWith(RedactApplicationSecrets));
    });
});
```

The redactor runs on exception messages, application stack traces, log messages, and source code
before those values reach the chat client. If redaction throws, the affected evidence is omitted.

Persist chat history and structured investigations through the existing EF operational store:

```csharp
options.AddOperationalStore(efOptions =>
{
    efOptions.UseTickerQDbContext<AppDbContext>(db =>
        db.UseNpgsql(connectionString));

    efOptions.AddAssistantHistory(history =>
    {
        history.MaxConversationsPerUser = 50;
        history.MaxMessagesPerConversation = 80;
        history.MaxInvestigationsPerNamespace = 500;
        history.InvestigationRetention = TimeSpan.FromDays(30);
    });
});
```

`AddAssistantHistory(...)` adds `AssistantConversations`, `AssistantMessages`,
`AssistantInvestigations`, `AssistantInvestigationLeases`, and
`AssistantInvestigationBudgets` to the application-owned EF model. Generate and apply an EF
migration before enabling it on an existing database. The final two tables coordinate automatic
investigations across nodes; they contain fingerprints, lease ownership, and model-call timestamps,
not prompts or model responses. Without a durable history store, Chat falls back to browser-local
history and the shared Overview/Investigations views remain unavailable.

## Automatic investigations

Automatic investigation is deliberately opt-in because it sends bounded execution evidence to
the configured AI provider without waiting for an operator to click **Investigate with AI**:

```csharp
dashboard.AddAssistant(assistant => assistant
    .UseChatClient(chatClient)
    .WithModelName("operations-model")
    .ConfigureRunInvestigator(investigator => investigator
        .EnableAutomaticInvestigations(automatic => automatic
            .Every(TimeSpan.FromSeconds(30))
            .WithLookback(TimeSpan.FromMinutes(15))
            .WithBatchSize(10)
            .WithHourlyLimit(30)
            .WithCoordinationLease(TimeSpan.FromMinutes(5))
            .IncludeCancellations()
            .DetectSuccessfulRecovery())));
```

Only terminal failures whose configured retries are exhausted are investigated. Cancellations are
excluded unless enabled. Each sweep is bounded by its batch size. The hourly limit is one rolling,
namespace-wide budget shared by every node using the operational database. Before a model call,
TickerQ takes an exclusive lease on the evidence fingerprint; another node seeing the same failure
pattern waits for a later sweep and then reuses the completed diagnosis. The lease is renewed during
the model call. If its owner exits, another node can recover the work after the configured lease
duration. When a later execution of the same function succeeds, TickerQ marks older completed
investigations as recovered. Recovery is evidence shown to the operator; it does not automatically
mark the incident resolved.

The lease prevents simultaneous calls by healthy nodes. As with any external API, a process can
terminate after the provider accepts a request but before TickerQ commits the response. TickerQ
keeps that call in the shared hourly budget and retries only after the lease expires; it cannot
prove whether the external provider completed or billed the interrupted request.

Repeated automatic failures at the same application source line reuse the latest completed
diagnosis in the same application namespace. TickerQ still creates a separate investigation record
for the new execution so operators can trace every occurrence, but it copies the existing summary
without calling the AI provider or consuming the hourly model-call allowance. The match includes
the function, terminal status, exception type, safe source filename, and source line. When no
application source frame is available, the normalized exception message is included so unrelated
failures are not grouped together.

Automatic investigation requires both `IAssistantInvestigationStore` and
`IAssistantInvestigationCoordinator`. Startup fails with a clear configuration error when either
is missing. EF `AddAssistantHistory()` registers one scoped implementation for both contracts.
Custom stores must provide equivalent atomic, namespace-scoped lease and rolling-budget behavior.

## Local checkout source

For development, or a deployment that deliberately includes the matching source revision, use
the built-in allow-listed local provider. It never searches the checkout: every readable file must
be mapped explicitly. Function mappings take precedence over filename mappings, which avoids
ambiguity when several projects contain files such as `Program.cs`.

```csharp
var revision = Environment.GetEnvironmentVariable("GIT_COMMIT_SHA")
    ?? "local-working-tree";

options.AddDashboard(dashboard =>
{
    dashboard.AddAssistant(assistant => assistant
        .UseChatClient(chatClient)
        .ConfigureRunInvestigator(investigator => investigator
            .UseLocalSourceContext(builder.Environment.ContentRootPath, source => source
                .WithRepository("Orders API")
                .WithRevision(revision)
                .WithContextLines(before: 20, after: 20)
                .MapFunction("Payments.Charge", "Jobs/PaymentJob.cs")
                .MapSourceFile("InvoiceJob.cs", "Jobs/InvoiceJob.cs"))
            .WithSourceContextLimits(maximumLines: 60, maximumCharacters: 10_000)
            .RedactEvidenceWith(RedactApplicationSecrets)));
});
```

Paths must be relative to the configured source root and cannot traverse outside it. Missing,
unmapped, or out-of-range source references simply leave source context unavailable; exception,
log, retry, and workflow evidence still reaches the investigator.

## Private repositories

TickerQ does not need a repository token and does not clone or store source code. Register an
application-owned `IExecutionSourceContextProvider` when source context is useful:

```csharp
builder.Services.AddSingleton<IExecutionSourceContextProvider, PrivateRepositorySourceProvider>();

options.AddDashboard(dashboard =>
{
    dashboard.AddAssistant(assistant =>
    {
        assistant.UseChatClient(chatClient)
            .ConfigureRunInvestigator(investigator => investigator
                .UseSourceContextProvider(sp =>
                    sp.GetRequiredService<IExecutionSourceContextProvider>())
                .WithSourceContextLimits(maximumLines: 60, maximumCharacters: 10_000)
                .RedactEvidenceWith(RedactApplicationSecrets));
    });
});
```

The provider receives only the execution id, function, exception type, method, safe source file
name, and line number. It should resolve an immutable deployed commit and return a small
repository-relative excerpt. A private-repository implementation should use a read-only GitHub App,
deployment identity, or equivalent server-side credential. Never return a credential-bearing URL;
TickerQ also strips URL user information before including the repository label.

Source-provider credentials stay in the host application's secret store. They are not sent to the
browser, saved in TickerQ persistence, or passed to the AI model.

## Operational behavior

Source lookup runs for an explicit or enabled automatic investigation. If the provider is
unavailable or cannot match the deployed revision, the diagnosis continues with exception, log,
and workflow evidence.
This keeps the assistant useful for container images that do not contain a source checkout and for
private repositories that temporarily cannot be reached.
