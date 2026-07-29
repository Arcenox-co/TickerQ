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
    options.AddOperationalStore(mongoOptions =>
    {
        mongoOptions.UseExistingMongoClient(databaseName: "tickerq");
    });
});
```

## Notes

- Single-document operations use atomic `findAndModify`. Atomic graph fencing, terminal result publication, retention, and the durable Node finalization outbox require multi-document transactions on a replica set. Standalone or topology-ambiguous clients fail closed and do not advertise durable Node callback support.
- Indexes are created idempotently on startup, including a unique index on `(CronTickerId, ExecutionTime)` for cron occurrence deduplication and the unique identity/due indexes for the Node finalization outbox.
- Collection names default to `ticker_TimeTickers`, `ticker_CronTickers`, `ticker_CronTickerOccurrences`, `ticker_TickerResults`, and `ticker_NodeFinalizationOutbox`. Override with `SetCollectionPrefix`.
