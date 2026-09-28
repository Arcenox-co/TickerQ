# TickerQ.MongoDB

MongoDB persistence provider for [TickerQ](https://tickerq.net/). Drop-in alternative to `TickerQ.EntityFrameworkCore` — use one or the other, not both.

Uses the official `MongoDB.Driver` directly. No EF Core, no ODM.

## Install

```bash
dotnet add package TickerQ.MongoDB
```

## Usage

```csharp
builder.Services.AddTickerQ(options =>
{
    options.UseDefinedCronApplicationNamespace("orders-api");
    options.UseReconciliationEpoch(1);
    options.AddOperationalStore(mongoOptions =>
    {
        mongoOptions.UseTickerQMongoClient(
            connectionString: "mongodb://localhost:27017",
            databaseName: "tickerq");
    });
});
```

Or reuse an `IMongoClient` already registered in DI:

```csharp
services.AddSingleton<IMongoClient>(_ => new MongoClient("mongodb://localhost:27017"));

builder.Services.AddTickerQ(options =>
{
    options.UseDefinedCronApplicationNamespace("orders-api");
    options.UseReconciliationEpoch(1);
    options.AddOperationalStore(mongoOptions =>
    {
        mongoOptions.UseExistingMongoClient(databaseName: "tickerq");
    });
});
```

## Notes

- `UseDefinedCronApplicationNamespace(...)` physically partitions runtime documents, results,
  locks, evidence, outbox, and metadata. Applications sharing a database may reuse GUIDs only with
  distinct explicit namespaces. Scheduler mode also requires the exact positive activation epoch;
  namespaced queue-only mode does not activate one. Namespace-less queue-only explicitly targets the
  legacy-global compatibility partition.
- `UseLegacyRuntimePartitionAdoption("orders-api", epoch)` is only for a verified single-owner
  pre-partition database. It holds one global authority, rejects a different owner, and is
  transactional, idempotent, and resumable on a replica set or mongos. Standalone MongoDB rejects
  adoption because it cannot provide the required transaction.
- See **[Upgrading CronTicker and TimeTicker storage](../../docs/upgrading-cron-time-tickers.md)**
  before rolling an existing MongoDB store across scheduler versions.
- Single-document operations use atomic `findAndModify`. Atomic graph fencing, terminal result publication, retention, the durable Node finalization outbox, and reconciliation activation require multi-document transactions on a replica set. Reconciliation activation is persisted in an application-scoped `ticker_StoreMetadata` document with a monotonic epoch, ordered checkpoint, and CAS version; standalone MongoDB fails activation before claiming an epoch, so the local scheduler gate remains closed.
- Protocol-aware rolling binaries serialize publication by touching the application-scoped activation metadata document in the same transaction as related writes. Readers treat only `Activated(epoch)` as ready, so they observe either the pre-epoch/`Activating` state or the complete committed publication. **This fence cannot control binaries that predate the protocol:** the first rollout must drain/quiesce every pre-protocol writer before activation. Do not attempt first adoption as an unconstrained mixed-version rolling deployment.
- Legacy code-defined Cron documents remain unclaimed unless ownership is explicit. Shared databases must configure the same `MapLegacyDefinedCronOwnership(function, ownerNamespace)` map in every application. `AdoptLegacyDefinedCronTickers()` is a catch-all only for a verified single-application database and is unsafe when applications share the database.
- Indexes are created idempotently on startup, including a unique index on `(CronTickerId, ExecutionTime)` for cron occurrence deduplication and the unique identity/due indexes for the Node finalization outbox.
- Collection names default to `ticker_TimeTickers`, `ticker_CronTickers`, `ticker_CronTickerOccurrences`, `ticker_TickerResults`, `ticker_NodeFinalizationOutbox`, and `ticker_StoreMetadata`. Override with `SetCollectionPrefix`.
