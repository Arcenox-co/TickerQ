# Upgrading CronTicker and TimeTicker storage

This guide covers upgrading an existing TickerQ store to physical application runtime partitions,
seed ownership, non-destructive retirement, pending-occurrence reconciliation, and TimeTicker chain repair.

The upgrade is additive, but it is not schema-optional. Install the required schema before starting upgraded scheduler nodes.

## Guarantees and boundaries

The upgrade is designed around these invariants:

- Existing CronTicker and TimeTicker primary keys are never rewritten.
- The canonical application namespace physically owns every runtime row, document, key, result,
  outbox, lock/evidence record, and maintenance operation; it is not merely Cron seed identity.
- Applications sharing a backend may reuse identical public GUIDs only when each uses a distinct,
  stable explicit namespace.
- A code-defined cron adopts a stable `SeedKey` in place.
- User-, dashboard-, and SDK-created cron rows are never claimed by code reconciliation.
- Removed code definitions enter retirement; terminal and in-flight history is not deleted.
- Expression and request-contract changes invalidate only unleased `Idle`/`Queued` occurrences.
- Legacy TimeTicker descendants receive a persistent `ChainRootId` before fenced child writes depend on it.
- New persisted fields are additive. This supports a rolling deployment, but old binaries cannot understand or enforce a newer store-version gate.

## Recommended rollout

> **First-adoption limitation:** a binary released before the reconciliation-activation protocol
> cannot observe or honor its epoch fence. Before activating this protocol for the first time, drain
> or quiesce **all** pre-protocol scheduler writers. There is no additive column, transaction, or
> compatibility shim that can make an unaware old process respect the new gate. After the fleet has
> adopted the protocol, later rollouts use the durable epoch/checkpoint boundary. Configure the same
> positive reconciliation epoch on every node in one deployment, and increase it for every deployment
> whose startup reconciliation manifest or semantics can differ.

1. Back up the durable store and record the currently deployed application migration.
2. Stop schema changes from older releases while the migration is being installed.
3. Upgrade every TickerQ package to the same version. Then generate and review an application-owned EF migration, or validate MongoDB/Redis operational prerequisites.
4. Deploy the additive schema everywhere before deploying upgraded scheduler binaries.
5. Configure `UseReconciliationEpoch(N)` with a positive deployment-controlled value. Keep `N` stable
   across replicas and restarts of the identical deployment; increase it before changing code-defined
   schedules, seed identity/contract metadata, repair rules, or finalization semantics.
   Every scheduler also requires a stable `UseDefinedCronApplicationNamespace(...)`; a scheduler
   without one is invalid. Namespaced queue-only producers are admitted without activation. A
   namespace-less queue-only producer explicitly targets the legacy-global compatibility partition.
6. Start one upgraded scheduler node and wait for the provider-specific bootstrap, data reconciliation, and finalization phases it implements.
7. For EF Core, verify the reported schema/data version and chain-repair postcondition. For MongoDB, verify index/bootstrap completion and transaction topology. For Redis, verify repair checkpoints separately; Redis does not currently expose provider-neutral store-version readiness.
8. Roll out the remaining scheduler nodes with that exact epoch. A node configured with a lower epoch
   fails closed when it observes a higher durable epoch and cannot reconcile or open its local gate.
9. Keep the cron retirement grace period longer than the maximum rolling-deployment window.

When a restart observes the exact target epoch already `Activated`, TickerQ opens only that process's
local gate and skips all fenced startup mutations. It does not assume that equal epoch numbers prove
equal manifests. Therefore **never reuse an epoch for changed reconciliation inputs**. An epoch bump
atomically moves the store back to `Activating`, reruns reconciliation under the new fence, and publishes
the exact new epoch as `Activated` before any local scheduler, retention loop, or Redis heartbeat proceeds.

Retirement grace does not apply to a code-defined cron whose current request contract requires a payload but whose seed has no payload. An upgraded node retires that blocked definition immediately and quarantines its unleased pending occurrences as durable `Skipped` evidence while removing them from runnable indexes. Before rollout, inventory code-defined crons on required-payload functions and explicitly migrate or remove those schedules.

Do not run destructive down-migrations during an emergency rollback. Leave additive columns, tables, hashes, and indexes in place; deploy the previous binary only after considering that it cannot enforce metadata introduced by the new release.

## Entity Framework Core

### Migrations remain application-owned

TickerQ contributes model mappings, but your application owns migration history because the final model depends on your database provider, naming conventions, custom entities, and whether TickerQ uses its own `TickerQDbContext` or your application `DbContext`.

`EnsureCreated()` is for a new database only. It does not upgrade an existing database.

For an application context, ensure the context applies the TickerQ model customizer before generating the migration. For a dedicated TickerQ context, ensure the provider and migrations assembly match the runtime registration.

### Generate and inspect the migration

Replace the project, startup project, context, and migration names with your application values:

```bash
dotnet tool update --global dotnet-ef --version 10.*

dotnet ef migrations add AddTickerQSeedOwnershipAndStoreMetadata \
  --project src/MyApp.Persistence \
  --startup-project src/MyApp \
  --context AppDbContext \
  --output-dir Migrations

dotnet ef migrations script <LAST_DEPLOYED_MIGRATION> AddTickerQSeedOwnershipAndStoreMetadata \
  --project src/MyApp.Persistence \
  --startup-project src/MyApp \
  --context AppDbContext \
  --idempotent \
  --output artifacts/tickerq-upgrade.sql
```

Review the generated migration and model snapshot together. The provider-specific SQL must be produced by the same EF provider used in production; do not copy a SQLite migration into PostgreSQL or SQL Server.

Apply through your normal deployment system, or explicitly:

```bash
dotnet ef database update AddTickerQSeedOwnershipAndStoreMetadata \
  --project src/MyApp.Persistence \
  --startup-project src/MyApp \
  --context AppDbContext
```

### Required additive model shape

The generated model must include:

- A non-null bounded `ApplicationNamespaceKey` at the start of every runtime identity, uniqueness,
  and runnable index for Cron definitions/occurrences, TimeTickers, result sidecars, store metadata,
  node-finalization outbox, and other runtime ownership/evidence rows.

- CronTicker:
  - nullable `SeedKey` with a unique nullable/filtered index;
  - nullable `SeedOwnerNamespace` (maximum 128 characters; an EF concurrency token), which records the application that owns a code-defined row;
  - nullable `SeedLastSeenAt`;
  - nullable `RetirementRequestedAt`;
  - nullable `RetiredAt`;
  - nullable `SeedWasEnabledBeforeRetirement`, used to avoid re-enabling a user-disabled definition.
- TimeTicker:
  - nullable `ChainRootId` during rollout/backfill;
  - nullable `ChainGeneration`, preserving the root's exact fencing token during repair;
  - indexes used by parent traversal, chain traversal, retention, and acquisition.
- The singleton `TickerQStoreMetadata` row, including schema/data versions, optimistic-concurrency
  `Version`, durable `ActivationEpoch`, `ActivationPhase`, and nullable `ActivationCheckpoint`.
- The complete `NodeFinalizationOutbox` table and its current identity/due indexes whenever the current TickerQ EF model maps it.

The repository's ApplicationDbContext, Console, and WebApi samples contain reviewed host-owned
`ApplicationRuntimePartitioning` migrations and matching snapshots. Consumers must generate and
review their own equivalent migration; the provider does not own application migration history.

On SQL Server, the unique nullable `SeedKey` index must exclude null rows. SQLite and PostgreSQL allow multiple null values in a unique index. Use the generated provider-specific model rather than hard-coding one provider's quoting or filter syntax.

### Startup and data repair

Schema migration must finish before TickerQ initialization. In particular, the activation metadata
columns must be deployed before a new binary can begin its epoch; `AutoMigrateDatabase()` is not a
substitute for draining pre-protocol writers during first adoption. Startup then follows this order:

1. run schema-only prerequisite bootstrap while the local gate is closed, so a pre-protocol store can install the activation metadata shape;
2. when explicitly configured, complete legacy runtime partition adoption under its store-global lease;
3. atomically begin/resume the durable activation epoch;
4. run in-epoch provider bootstrap and store-version/pending-migration validation;
5. repair TimeTicker chains and discoverability;
6. reconcile code-defined CronTickers;
7. finalize provider state, including uniqueness constraints that require converged data;
8. run consumer seed callbacks and stale-occurrence/provider startup actions;
9. atomically commit the epoch;
10. open the process-local gate, allowing scheduler, fallback, stale-recovery, retention, and Redis
   heartbeat/resource-recovery loops to perform their first persistence I/O.

Each completed phase advances the durable checkpoint. A crash or cancellation leaves the epoch in
`Activating` at the last truthful checkpoint and leaves the local polling gate closed. Restarted nodes
rerun idempotent work and advance from that checkpoint. The final EF commit uses a serializable
transaction plus metadata CAS; once its durable commit succeeds, cancellation is not re-checked so
startup cannot report a false failure after publication.

The TimeTicker chain repair is idempotent and checkpointed. It walks legacy parent relationships, preserves IDs/status/results, assigns each reachable descendant its root ID, and verifies the postcondition before marking the migration complete.

A store that reports a data version newer than the running binary supports is rejected. Upgrade the binary; do not decrement the metadata row manually.

### Malformed TimeTicker graphs

A missing parent, self-cycle, or multi-row cycle is data corruption, not a row that can be guessed safely. The data migration fails with an actionable exception and does not mark its checkpoint complete.

Before retrying:

1. preserve a backup of the affected rows and results;
2. identify the reported ticker IDs and parent edges;
3. decide from domain history whether to restore the missing parent, detach the malformed row as a root, or cancel/archive the malformed chain;
4. make the smallest explicit data correction;
5. rerun the migration and verify every non-root row has the expected `ChainRootId`.

Do not bulk-set `ChainRootId = ParentId`: that is wrong for grandchildren and deeper descendants.

## Code-defined CronTicker behavior

### Stable ownership

A code-defined cron receives a deterministic ownership key derived from an application namespace plus its stable local definition identity. Configure a stable namespace on every application that seeds code-defined crons, especially when applications share a store:

```csharp
services.AddTickerQ(options =>
{
    options.UseDefinedCronApplicationNamespace("billing-api");
    options.UseReconciliationEpoch(1);
    // provider/dashboard configuration...
});
```

The value is persisted in `SeedOwnerNamespace`; use the same value on every node of one application and a different value for every independently deployed application sharing the store. Do not derive it from a host name, replica ID, deployment slot, or other value that changes between restarts. A definition's stable ID defaults to its function name, so its resulting `SeedKey` and deterministic ID remain stable across expression and request-contract changes.

Scheduler hosts must configure both `UseDefinedCronApplicationNamespace(...)` and an explicit positive `UseReconciliationEpoch(...)`; startup fails closed when either is omitted. Dashboard/manager-only processes that disable scheduler background services do not participate in activation. Keep the namespace stable for the lifetime of the application. Keep the epoch stable across replicas and restarts of an identical deployment, and increment it before deploying changes to definitions, contracts, repair semantics, or finalization behavior.

On the first v2 reconciliation, exactly one legacy code-owned row for a definition may be adopted in place when it has no owner and its key is null, the function/stable-definition ID, or the pre-v2 `namespace:stableDefinitionId` form. Adoption rewrites ownership to the bounded v2 key while preserving the row's primary key and occurrence references. **Unclaimed legacy rows are not adopted automatically.** Reconciliation fails closed before writes when no explicit owner is configured, candidates are duplicated, the local manifest has multiple possible definitions for that function, or another namespaced owner already conflicts. Resolve ownership explicitly before retrying; do not delete historical rows merely to bypass the diagnostic.

### Explicit legacy ownership migration

For a store shared by multiple applications, inventory every pre-namespace code-defined Cron row and
configure one authoritative owner per function. Deploy the same complete mapping to every application
that uses the store so startup order cannot change the result:

```csharp
services.AddTickerQ(options =>
{
    options.UseDefinedCronApplicationNamespace("orders-api");
    options.UseReconciliationEpoch(2);

    options.MapLegacyDefinedCronOwnership("ProcessOrders", "orders-api");
    options.MapLegacyDefinedCronOwnership("SendInvoice", "billing-api");
});
```

Function and namespace values are trimmed, compared ordinally, and bounded by the persisted Cron seed
identity contract. Repeating the same mapping is idempotent. Mapping one normalized function to two
different namespaces is rejected during configuration instead of using last-call or startup-order wins.
An explicit mapping can name another application; only the named owner's matching desired manifest may
claim that row. A mapping does not transfer an already namespaced row.

For a store that is provably used by exactly one application, a shorter migration is available:

```csharp
options.UseDefinedCronApplicationNamespace("orders-api");
options.AdoptLegacyDefinedCronTickers();
```

`AdoptLegacyDefinedCronTickers()` is an explicit catch-all: the current application claims each
otherwise-unmapped legacy function in its desired manifest. **Do not use it when applications share a
store.** A legacy row contains no trustworthy application identity, so the first application to opt in
could claim another application's schedule. Per-function mappings take precedence over the catch-all.
Without either API, adoption remains disabled and startup fails closed with remediation guidance.

Rows without framework seed ownership—including dashboard and SDK rows with the same function name—are not modified.

### Retirement grace

Removing a code-defined schedule requests retirement instead of deleting the row immediately. During the configured grace period, a reappearing definition clears the framework retirement request. After finalization, the definition is disabled/retired; unleased pending occurrences are quarantined as durable `Skipped` evidence and deindexed from runnable work.

The following survive retirement and expression/contract changes:

- leased or `InProgress` work;
- terminal occurrences;
- persisted results and historical snapshots.

Set the grace period longer than the longest expected rolling deployment. Immediate finalization is suitable only for an explicitly controlled single-node or fully quiesced deployment.

Configure it through the scheduler options API:

```csharp
options.ConfigureScheduler(s =>
    s.DefinedCronRetirementGracePeriod = TimeSpan.FromDays(2));
```

Blocked required-payload definitions are the exception: they retire immediately regardless of this grace period because continuing to generate an invalid empty request is unsafe.

## MongoDB

MongoDB startup now has explicit prerequisite and finalization phases. Non-unique operational indexes and transaction capability are prepared before reconciliation; the partial unique `SeedKey` index is created only after duplicate convergence.

Operational requirements:

- Use a replica set or mongos for multi-document transactions, durable graph fencing, terminal result publication, and durable Node finalization.
- Standalone MongoDB remains available only for operations with documented non-transactional behavior; unsupported transactional capabilities fail closed.
- The application identity needs collection read/write and index-management privileges during startup.
- Allow one node to complete the first upgraded bootstrap before scaling the upgraded fleet.
- Legacy runtime adoption requires a replica set or mongos. Standalone MongoDB rejects it rather
  than attempting a partial ownership move without transactions.

The partial unique index includes only rows whose `SeedKey` is a BSON string, so user rows and unclaimed legacy rows with a missing/null key do not collide.

## Redis

Redis stores entities and discoverability indexes separately. Upgraded standalone Redis deployments persist each application's reconciliation epoch in a scoped `tq:reconciliation:activation:scope:<scope-key>` metadata hash. Begin/checkpoint/commit are one-key Lua/CAS transitions; admission, recovery, release, and maintenance scripts use that same scoped hash, and the final commit is the publication boundary after all awaited definition mutations and repair checkpoints complete.

Operational requirements:

- The application identity needs `EVAL`/script execution plus read, write, and scan access to the configured TickerQ key prefix.
- **Drain every pre-protocol Redis scheduler binary before first activation.** Those binaries do not read the metadata hash and can acquire or publish work while an upgraded node is reconciling. The epoch fence protects later rollouts only after the entire fleet runs a protocol-aware binary.
- Scheduler activation and scheduler-side multi-key mutation fail closed on Redis Cluster. Use
  standalone Redis, including ordinary primary/replica deployments, for scheduler processes.
- Namespaced queue-only Redis Cluster producers support TimeTicker insert/update and Cron
  insert/update/remove through deliberately cluster-safe single-key commands. Cron removal rejects a
  definition while occurrences still reference it because that cross-key proof is not safe.
- Namespace-less queue-only is the explicit legacy-global compatibility mode, never inferred ownership.
- Legacy runtime adoption is unsupported on Redis Cluster because the global lease, legacy keys, and
  target partition cannot be moved atomically across slots. Perform adoption on standalone Redis first.
- Do not flush or edit TickerQ keys during rolling deployment.
- Configure `UseDefinedCronApplicationNamespace(...)` on every Redis scheduler host. Redis stamps `SeedOwnerNamespace` in the cron JSON, derives new document IDs from the bounded namespaced `SeedKey`, and scopes activation metadata to that application; one application's empty manifest retires only rows carrying that same owner.
- Treat corrupt serialized ticker documents as an operational incident. They must be reported/quarantined for explicit remediation rather than silently replaced.
- Inspect the activation metadata checkpoint when diagnosing an interrupted startup. `Activating` is intentionally fail-closed; restart the idempotent initializer rather than editing the hash manually.

Pending cron acquisition refreshes the authoritative current definition instead of executing an embedded stale expression or request-contract identity.

| Redis topology and mode | Support |
|---|---|
| Standalone/primary-replica scheduler with namespace and exact epoch | Supported |
| Standalone namespaced queue-only | Supported |
| Cluster namespaced queue-only | Supported with the single-key constraints above |
| Cluster scheduler/activation | Unsupported; fails closed |
| Cluster legacy runtime adoption | Unsupported; fails closed before moving keys |

## Explicit legacy runtime partition adoption

Use this only after inventory proves one application owns all namespace-less runtime state:

```csharp
options.UseLegacyRuntimePartitionAdoption("billing-api", reconciliationEpoch: 12);
```

The target must be non-legacy and the epoch positive. The call binds both the runtime namespace and
exact reconciliation epoch. Before inspecting state, each provider acquires a store-global authority
record. A different owner/epoch, or runtime state belonging to multiple other owners, is rejected.
The same owner and epoch may restart safely: EF and supported MongoDB topologies transact the move;
Redis standalone uses a durable resumable lease; in-memory moves atomically within the process.
Adoption completes before reconciliation and activation. It does not replace the separate reviewed
per-function `MapLegacyDefinedCronOwnership` mapping when legacy Cron seed ownership is ambiguous.

## In-memory provider

The in-memory provider follows the same ownership, retirement, and pending-only reconciliation semantics for behavioral parity, but it is process-local and has no durable schema or data migration.

## Verification checklist

Before completing rollout, verify:

- the EF migration/snapshot or provider indexes contain every required additive field and constraint;
- for EF Core, `TickerQStoreMetadata` reports a supported schema/data version and no framework data migration remains pending;
- for MongoDB, bootstrap indexes and required transaction topology are ready; for Redis, available repair checkpoints have been inspected separately;
- every reachable non-root TimeTicker has the expected `ChainRootId`;
- no primary key, terminal status, or result changed during backfill;
- exactly one active code-owned CronTicker exists per `SeedKey`;
- user/dashboard/SDK cron rows remain unowned and unchanged;
- every legacy code-defined Cron row has either an explicit `MapLegacyDefinedCronOwnership` owner or a
  reviewed single-application `AdoptLegacyDefinedCronTickers` opt-in; unclaimed rows fail closed;
- no unleased stale pending occurrence remains after an expression, contract, block, or finalized-retirement change;
- leased/in-progress and terminal work remains intact;
- a second reconciliation is a no-op, and any provider-specific repair reports completion through an actual supported checkpoint/status surface;
- each scheduler node has completed the readiness phases its provider implements before accepting work.

## Rollback limitations

The schema additions are intentionally readable by prior binaries, but downgrade behavior is operational, not magical:

- old nodes ignore new retirement and store-version metadata;
- old nodes can republish old code-defined schedules during a mixed-version rollback;
- old nodes cannot enforce the new uniqueness/data-repair lifecycle;
- destructive down-migrations can make upgraded data unreadable and are unsupported during emergency rollback.

Prefer binary rollback with the additive schema left intact, pause definition retirement during the rollback window, and verify the cron manifest after the fleet converges.
