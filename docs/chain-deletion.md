# Chain deletion behavior

Time-ticker roots have a `DeleteBehavior` setting. Existing jobs and newly created jobs default to `ChainDeleteBehavior.Cascade`.

| Setting | Explicit deletion and replacement | Retention |
| --- | --- | --- |
| `Cascade` | Removes the root and its complete descendant tree when the chain is safe to remove. | Uses the provider's existing retention eligibility checks. |
| `Restrict` | Keeps the complete chain while any descendant is unfinished. Once every descendant is terminal, allows cascade deletion or replacement automatically. | Unfinished descendants keep the chain. Once all nodes meet their configured terminal-status cutoffs, the provider can remove the chain. |

Terminal statuses are **Done, DueDone, Failed, Cancelled, and Skipped**. Idle, Queued, InProgress, and unknown statuses keep Restrict in effect. This includes grandchildren and deeper descendants. A stale InProgress job remains protected until recovery gives it a terminal status. Retrying a child can reapply the restriction. No policy change is needed when the last descendant finishes.

Restrict does not detach children, cancel jobs, or prevent execution. A standalone root with no children remains deletable. Configure the policy on the root only. Running jobs, live leases, and acquisition ownership continue to block deletion regardless of policy.

```csharp
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Enums;

var chain = new TimeTickerEntity
{
    Function = "PrepareReport",
    ExecutionTime = DateTime.UtcNow,
    DeleteBehavior = ChainDeleteBehavior.Restrict,
    Children = [new TimeTickerEntity
    {
        Function = "SendReport",
        RunCondition = RunCondition.OnSuccess
    }]
};

await timeTickerManager.AddAsync(chain);

// Optional override: allow cascade removal of unfinished, unowned jobs.
// Normally leave Restrict set; it releases automatically when all descendants finish.
var changed = await timeTickerManager.SetDeleteBehaviorAsync(chain.Id, ChainDeleteBehavior.Cascade);
if (!changed.IsSucceeded)
    throw changed.Exception;
```

The fluent parent builder also supports `.SetDeleteBehavior(ChainDeleteBehavior.Restrict)`.

In the dashboard, configure **Chain deletion** in the builder's root editor. Existing chains expose **Deletion: Cascade/Restrict** in the flowchart and root details. Changing it requires saving the confirmation dialog. It is available for completed chains and disabled in read-only mode. The chain editor allows replacement once all descendants are terminal, subject to its existing root-status checks. Deletion confirmations use fresh server-side status checks.

The manager returns a validation failure for known protected chains. Bulk deletion checks all selected roots before deleting any of them; callers must inspect `IsSucceeded` and `AffectedRows`. Provider operations also enforce the stored policy within their atomic deletion boundary. A provider-level bulk operation may delete other eligible roots while skipping a protected one, matching the existing behavior for active chains.

## Storage and upgrades

EF Core, MongoDB, Redis, and in-memory storage enforce the policy. MongoDB and Redis documents without the field continue to mean Cascade. Redis retention currently keeps all nontrivial chains; this change does not expand Redis's existing retention support. Explicit cascade deletion remains available there.

**EF Core deployments require a migration before running the updated application.** Add a non-null integer `DeleteBehavior` column to the mapped time-ticker table with default `0` (Cascade). Generate it using your application's context and review the table/schema names:

```sh
dotnet ef migrations add AddChainDeleteBehavior --context YourDbContext
dotnet ef database update --context YourDbContext
```

The repository's Console, WebApi, and ApplicationDbContext samples include matching migrations and model snapshots. Do not use `EnsureCreated` to upgrade an existing production database.

Upgrade every application instance that can write to the shared store before relying on Restrict. Older binaries do not enforce this setting. Custom persistence providers must implement policy enforcement and `SetTimeTickerDeleteBehaviorAsync`; the default interface implementation rejects policy changes when unsupported.

A descendant that never reaches a terminal status keeps its chain protected. Resolve or cancel unfinished jobs as appropriate; an eligible chain can then be removed by the next retention sweep without changing its policy. Completing descendants does not delete the chain immediately: explicit deletion or the configured retention sweep still has to run, and every node must meet its retention age. Execution log archives still follow their own configured storage limits and retention. The policy covers TickerQ operations and does not protect against direct database edits.
