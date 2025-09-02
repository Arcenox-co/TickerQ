# API Reference

## Main Classes
- `TickerOptionsBuilder`
- `TickerQServiceExtensions`
- `DashboardConfiguration`
- `EfCoreOptionBuilder`

## Key Methods
- `AddTickerQ`
- `UseTickerQ`
- `AddDashboard`
- `AddOperationalStore`

### Builder Extensions
- `SetMaxConcurrency(int)` – Override default (logical processor count)
- `UpdateMissedJobCheckDelay(TimeSpan)` – Adjust missed job scan interval
- `SetExceptionHandler<THandler>()` – Register custom exception handler
- `SetInstanceIdentifier(string)` – Provide node identity
- `UseTickerSeeder(...)` – Seed initial tickers (time & cron)

### EF Core Options
- `UseModelCustomizerForMigrations()` – Use model customizer for schema isolation
- `CancelMissedTickersOnAppStart()` – Cancel missed tickers bound to this node
- `IgnoreSeedMemoryCronTickers()` – Do not persist memory-only cron tickers

### Function Registration
Ticker functions are discovered at compile time (source generator) and registered into `TickerFunctionProvider`:
- `TickerFunctionProvider.TickerFunctions` – Map of function name to cron expression, priority, and delegate
- `TickerFunctionProvider.TickerFunctionRequestTypes` – Map of function name to request payload type

### Request Retrieval
Use `TickerRequestProvider.GetRequestAsync<T>(serviceProvider, tickerId, tickerType)` to fetch a typed request payload for a running ticker when implementing custom managers or diagnostics.

See [TickerQ API Reference](https://tickerq.arcenox.com/api/index.html).
