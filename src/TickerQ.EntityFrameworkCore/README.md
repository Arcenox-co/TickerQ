# TickerQ.EntityFrameworkCore

Entity Framework Core integration for [TickerQ](https://github.com/arcenox/TickerQ), a high-performance background job scheduler for .NET.

This package enables persistence of time-based and cron-based jobs using EF Core, allowing for robust tracking, retry logic, and job state management.

## Runtime compatibility

Use this provider with the managed .NET runtime. It is not supported in a Native AOT
application: EF Core's runtime model construction is unavailable after Native AOT
publishing, and TickerQ's provider queries are compiled inside the package rather than the
application's generated model. The TickerQ core scheduler, dashboard, and Redis provider
have separate verified Native AOT lanes.

## Retaining job history

EF Core keeps completed time tickers and cron occurrences until they are explicitly
deleted or an opt-in retention policy removes them. Use
`options.ConfigureJobRetention(...)` in your existing `AddTickerQ` registration to set
per-status windows. All windows default to `null` (retain indefinitely); cron definitions
are never deleted by retention.

History lives in the database, while active execution and bounded log tails still use
application memory. Time-chain retention deletes a complete eligible chain atomically
and preserves chains with active, recent, or otherwise ineligible descendants. Execution
log retention is configured separately and is not changed by deleting job rows.

See [job retention configuration and limits](../../docs/reliability.md#job-retention)
for an example, cleanup cadence, chain limits, and the separate log policy.

## Node discovery and control

The EF operational store persists scheduler heartbeats and cross-node cancellation commands
in `TickerQNodes` and `TickerQControlCommands`. Existing databases need an
application-owned migration containing both tables before upgraded scheduler replicas start.
The advertised dashboard URL is optional and used only for navigation; cancellation is routed
through the database and fenced to the exact execution generation. See
[nodes and cross-node cancellation](../../docs/reliability.md#nodes-and-cross-node-cancellation)
for configuration and behavior.

---

## 📦 Installation

```bash
dotnet add package TickerQ.EntityFrameworkCore
```

## Schedule a job with your business changes

`UseApplicationDbContext<AppDbContext>(...)` automatically supports `StageAsync` on
`ITimeTickerManager<TimeTickerEntity>` and `ICronTickerManager<CronTickerEntity>`.
There is no extra enable flag and no `DbContext`
argument on the method. Inject the manager and your application context into the same
scoped service or HTTP request, then save once:

```csharp
// dbContext and timeTickerManager are resolved from the same DI scope.
dbContext.Orders.Add(order);

await timeTickerManager.StageAsync(new TimeTickerEntity
{
    Function = "ProcessOrder", // A registered TickerQ function.
    ExecutionTime = DateTime.UtcNow
}, cancellationToken);

await dbContext.SaveChangesAsync(cancellationToken);
```

For a recurring schedule, use the cron manager in the same way:

```csharp
dbContext.Orders.Add(order);

await cronTickerManager.StageAsync(new CronTickerEntity
{
    Function = "RefreshOrderSummary", // A registered TickerQ function.
    Expression = "0 * * * *"         // Every hour, in the configured scheduler time zone.
}, cancellationToken);

await dbContext.SaveChangesAsync(cancellationToken);
```

Time jobs, cron schedules, and business changes can all be staged in the same scope and
saved together. Cron expressions and request payloads are validated when staging and again
before saving. Both five-part and six-part cron expressions are supported. Staging creates
a new user-owned schedule; code-defined schedule reconciliation continues to own its seeds.

The order and job are written in the same database transaction. If saving fails, neither
is committed. `StageAsync` returns the tracked entity and throws on invalid input or an
unsupported configuration; returning successfully does **not** mean the job is persisted.
Disposing the scope without saving discards both pending changes. `AddAsync` continues to
persist independently and does not join your application context's transaction.

For work spanning multiple saves, use the context's normal EF transaction:

```csharp
await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
dbContext.Orders.Add(order);
await timeTickerManager.StageAsync(new TimeTickerEntity
{
    Function = "ProcessOrder",
    ExecutionTime = DateTime.UtcNow
}, cancellationToken);
await dbContext.SaveChangesAsync(cancellationToken);
await transaction.CommitAsync(cancellationToken);
```

The scheduler and dashboard are notified only after commit. Time jobs are stored as idle rows
for the scheduler to acquire; they are not executed inline by `StageAsync` or `SaveChanges`.
Cron schedules invalidate the partition's schedule cache after commit, then wake the scheduler
for their next run. Disabled or system-paused schedules remain inactive. Rolling back the
transaction leaves the existing cache and scheduler notifications untouched. A post-commit
notification failure is logged and never turns a durable business commit into a failed save;
saved time jobs remain available for database recovery. A failed cron-cache invalidation can
delay discovery until that cache is refreshed.

Usage requirements:

- Use a relational EF provider with the TickerQ mappings and a **scoped** application
  context registered through DI. Standard `AddDbContext`, `AddDbContextPool`, and the
  scoped context registration supplied by `AddDbContextFactory` are supported. A context
  created separately with `new` or `factory.CreateDbContext()` is a different unit of work.
- Resolve the manager inside the same scope as the context. Root-provider calls fail
  clearly. The standalone TickerQ context, MongoDB, and in-memory providers do not support
  this EF-specific operation; they retain `AddAsync`.
- Stage new root time jobs, complete new time chains, or new cron schedules. Construct the chain before staging; do not
  change its IDs, parent links, or membership afterward. Function/request validation runs
  again when saving. Clear the complete tracked graph to discard it.
- Commit through EF's transaction API. Ambient `TransactionScope` and raw connection
  commits are not supported. When EF provider retries are enabled, wrap a user-managed
  transaction in `dbContext.Database.CreateExecutionStrategy().ExecuteAsync(...)`, as for
  other EF transactional business operations.
- A save containing staged jobs automatically uses a Serializable transaction when there
  is no existing transaction. For the legacy global partition, an explicit transaction
  must also use `IsolationLevel.Serializable`. An explicitly namespaced application can
  use its normal explicit-transaction isolation level. The existing admission and chain
  mutation locks remain held until commit, so keep transactions short.

Transactional scheduling adds no tables or columns to the current TickerQ schema.

## Concurrent startup and retries

Code-defined cron reconciliation uses stable seed identities and the unique seed index to converge
on one row when replicas start together. It runs the complete transaction through the EF Core
execution strategy configured on your `DbContext`, creating a fresh context lease for each attempt.
Use your database provider's connection-resiliency option (for example, `EnableRetryOnFailure()`
with SQL Server) for its full transient-error handling. TickerQ also performs bounded retries for
EF write conflicts and database exceptions marked `IsTransient`; it does not suppress permanent
failures or override an exhausted provider retry policy.

Provider-owned scheduling and chain mutations also retry transient transaction conflicts with
bounded backoff and a fresh context and transaction per attempt. Their serializable isolation
and graph locks remain in place. An ambiguous commit failure is propagated rather than replayed
by TickerQ's fallback retry. This does not retry an application's `StageAsync` transaction:
the application owns retrying its complete business transaction.

On SQL Server, startup reconciliation takes a table write lock on the mapped cron table before
reading seed rows. This prevents lock-upgrade and key-range ordering deadlocks between starting
replicas. The lock lasts for the reconciliation transaction, serializes seeding across application
partitions, and briefly blocks normal writes to that cron table. Other database providers retain
their existing serializable reconciliation query.

## Integration settings

The optional [TickerQ.Integrations](../TickerQ.Integrations/README.md) package stores
its encrypted configuration in an `IntegrationSettings` table, one row per runtime
namespace. Generate and apply an EF migration before enabling it. The model customizer
and standalone TickerQ context include the mapping; manually configured models must add
`IntegrationSettingsConfigurations` with their configured schema. The package guide
covers setup and sharing Data Protection keys across replicas. For an existing
`NotificationSettings` table, follow the [rename upgrade steps](../TickerQ.Integrations/README.md#upgrade-existing-notification-settings)
to preserve saved connections instead of dropping and recreating the table.

## Database schema upgrades

See **[Upgrading CronTicker and TimeTicker storage](../../docs/upgrading-cron-time-tickers.md)**
for schema-first rollout order, exact `dotnet ef` commands, required additive fields,
legacy TimeTicker chain repair, provider-specific index behavior, and rollback limitations.

The EF provider maps the durable `NodeFinalizationOutbox` table in both the built-in TickerQ
`DbContext` and the application-`DbContext` customizer. The table includes exact UTC creation ticks
and a terminal-mutation digest in addition to the delivery fields. Existing databases must install
all of these additive columns through the application's normal EF Core migration
(`dotnet ef migrations add ...` then `dotnet ef database update`, or the opt-in
`AutoMigrateDatabase()` flow). `EnsureCreated()` creates the complete table for a new database only;
it does not upgrade an existing schema.

`UseApplicationNamespace(...)` is the physical EF runtime partition, not only seed
identity. Runtime identities and runnable indexes begin with `ApplicationNamespaceKey`, so two
applications can reuse GUIDs in one database only with distinct explicit namespaces. Scheduler hosts
also require the exact positive reconciliation epoch. Namespaced queue-only hosts do not activate an
epoch; omitting their namespace deliberately uses the legacy-global compatibility partition.

Existing databases must install an application-owned migration equivalent to the samples'
generated `ApplicationRuntimePartitioning` migrations and snapshots. TickerQ supplies mappings, not
provider-owned migration history. For a verified single-owner store,
`UseLegacyRuntimePartitionAdoption("orders-api", epoch)` acquires a durable global lease and moves
legacy rows transactionally before activation; the same owner/epoch is idempotent and retry-safe.

`TickerQStoreMetadata` also carries the durable reconciliation activation epoch/checkpoint and an
optimistic-concurrency version. Deploy those additive columns before starting upgraded schedulers.
Its `Id` column must allow **80 characters**: activation rows use the complete 78-character
runtime partition key. If your existing model snapshot has a 64-character limit, generate and
apply a widening migration before starting a scheduler. Updating the package or calling
`EnsureCreated()` alone does not update an existing column. See
[widening the activation metadata key](../../docs/upgrading-cron-time-tickers.md#widen-the-activation-metadata-key)
for rollout instructions.
For the **first** protocol adoption, all binaries predating the epoch protocol must be drained: they
cannot honor a fence they do not know exists. EF scheduler pollers remain behind a process-local gate
until bootstrap/reconciliation checkpoints are durable and the epoch commit succeeds.

Existing pre-namespace code-defined Cron rows are deliberately not claimed automatically. For shared
databases, configure `MapLegacyDefinedCronOwnership(function, ownerNamespace)` identically on every
application. Use `AdoptLegacyDefinedCronTickers()` only after proving the database belongs to one
application; it is an explicit catch-all and is unsafe for a shared database. See the upgrade guide's
legacy ownership migration section before the first reconciled startup.

> **Node rollout gate:** Do not enable or register Node remote functions until the migration is
> installed in every scheduler database **and every scheduler writer has been upgraded** to a
> version that writes the durable outbox shape. Mixed old/new writers are unsupported. At startup,
> the EF provider probes the exact model mapping and performs a bounded access to the mapped table;
> Node dispatch remains disabled until that probe succeeds. A missing mapping/table logs a clear
> migration error without stopping ordinary non-Node EF persistence. With `AutoMigrateDatabase()`,
> migration runs before the probe; after a successful migration (or `EnsureCreated()` for a new
> database), the provider marks Node finalization ready.
