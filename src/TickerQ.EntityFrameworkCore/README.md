# TickerQ.EntityFrameworkCore

Entity Framework Core integration for [TickerQ](https://github.com/arcenox/TickerQ), a high-performance background job scheduler for .NET.

This package enables persistence of time-based and cron-based jobs using EF Core, allowing for robust tracking, retry logic, and job state management.

---

## 📦 Installation

```bash
dotnet add package TickerQ.EntityFrameworkCore
```

## Database schema upgrades

The EF provider maps the durable `NodeFinalizationOutbox` table in both the built-in TickerQ
`DbContext` and the application-`DbContext` customizer. The table includes exact UTC creation ticks
and a terminal-mutation digest in addition to the delivery fields. Existing databases must install
all of these additive columns through the application's normal EF Core migration
(`dotnet ef migrations add ...` then `dotnet ef database update`, or the opt-in
`AutoMigrateDatabase()` flow). `EnsureCreated()` creates the complete table for a new database only;
it does not upgrade an existing schema.

> **Node rollout gate:** Do not enable or register Node remote functions until the migration is
> installed in every scheduler database **and every scheduler writer has been upgraded** to a
> version that writes the durable outbox shape. Mixed old/new writers are unsupported. At startup,
> the EF provider probes the exact model mapping and performs a bounded access to the mapped table;
> Node dispatch remains disabled until that probe succeeds. A missing mapping/table logs a clear
> migration error without stopping ordinary non-Node EF persistence. With `AutoMigrateDatabase()`,
> migration runs before the probe; after a successful migration (or `EnsureCreated()` for a new
> database), the provider marks Node finalization ready.
