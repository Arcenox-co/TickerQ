# TickerQ.EntityFrameworkCore

Entity Framework Core integration for [TickerQ](https://github.com/arcenox/TickerQ), a high-performance background job scheduler for .NET.

This package enables persistence of time-based and cron-based jobs using EF Core, allowing for robust tracking, retry logic, and job state management.

---

## 📦 Installation

```bash
dotnet add package TickerQ.EntityFrameworkCore
```

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

`UseDefinedCronApplicationNamespace(...)` is the physical EF runtime partition, not only seed
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
