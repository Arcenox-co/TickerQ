## Table Setup & Configuration

Your `DbContext` should include configuration for three main tables:
- Time tickers
- Cron tickers
- Occurrences (job history)

Apply these configurations in your context class to ensure jobs are tracked and persisted.

## Migration Troubleshooting

If you encounter errors during migration (e.g., referential actions or rows not affected), check your migration files for `OnDelete: SetNull` and change to `NoAction` as needed. See GitHub issues for more details.

Some timer-based triggers may not persist correctly in the database due to current library limitations. Always verify your tables after migration and job creation.

TickerQ is evolving rapidly; check the official repo and issues for updates and fixes.
# Entity Framework Core

TickerQ.EntityFrameworkCore enables persistence of jobs using EF Core for tracking, retry logic, and job state management.

## Persistence & Migrations

TickerQ stores all scheduled jobs in your database, allowing jobs to be restarted and tracked even after service restarts. This enables robust scheduling and recovery.

### Creating Migrations
Run the following commands to set up the required schema:

```bash
dotnet ef migrations add TickerQInitialCreate
dotnet ef database update
```

This will create tables for time tickers, cron tickers, and job occurrences. The schema is designed for reliability and tracking job history.

### Schema Overview
- **TimeTickers**: Time-based jobs
- **CronTickers**: Cron-based jobs
- **Occurrences**: Job execution history

TickerQ supports application lifetime hooks to handle missed jobs and cancellations during restarts.

## Installation
```bash
dotnet add package TickerQ.EntityFrameworkCore
```

## Setup
```csharp
options.AddOperationalStore<MyDbContext>(efOpt =>
{
    efOpt.SetExceptionHandler<MyExceptionHandlerClass>();
    efOpt.UseModelCustomizerForMigrations();
});
```

See [EF Core Integration](https://tickerq.arcenox.com/intro/entity-framework.html).
