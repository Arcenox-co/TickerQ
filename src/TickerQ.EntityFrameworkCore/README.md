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
`DbContext` and the application-`DbContext` customizer. Existing databases must add this table
through the application's normal additive EF Core migration (`dotnet ef migrations add ...` then
`dotnet ef database update`, or the opt-in `AutoMigrateDatabase()` flow). `EnsureCreated()` creates
the table for a new database only; it does not upgrade an existing schema.
