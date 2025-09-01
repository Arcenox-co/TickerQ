## Dashboard Path & Authorization

Set the dashboard path and credentials in your configuration:

```csharp
options.AddDashboard(uiopt =>
{
    uiopt.BasePath = "/tickerq-dashboard";
    uiopt.EnableBasicAuth = true;
});
```

In `appsettings.json`:
```json
"TickerQ:BasicAuth": {
    "Username": "admin",
    "Password": "admin"
}
```

This secures dashboard access and allows you to log in with the specified credentials.
# Configuration

## Options
- `SetMaxConcurrency(int)`
- `AddOperationalStore<TDbContext>(Action<EfCoreOptionBuilder>)`
- `AddDashboard(Action<DashboardConfiguration>)`
- `EnableBasicAuth`
- `InstanceIdentifier`

### Advanced Options
- `UpdateMissedJobCheckDelay(TimeSpan)` – Frequency of scanning for missed jobs (min 30s; default 1m)
- `SetExceptionHandler<THandler>()` – Register a global handler implementing `ITickerExceptionHandler`
- `SetInstanceIdentifier(string)` – Unique node id for clustered/distributed scenarios
- `UseTickerSeeder(...)` – Seed initial time/cron tickers during startup
- `CancelMissedTickersOnAppStart()` – Cancel node-bound tickers on restart
- `IgnoreSeedMemoryCronTickers()` – Skip persisting seed cron tickers defined only in memory

## Dashboard Configuration
```csharp
options.AddDashboard(uiopt =>
{
    uiopt.BasePath = "/tickerq-dashboard";
    uiopt.EnableBasicAuth = true;
    uiopt.CorsOrigins = new[] { "*" };
});
```

### Basic Auth Setup

To enable dashboard authentication, configure Basic Auth in your `appsettings.json`:

```json
"TickerQ:BasicAuth": {
    "Username": "admin",
    "Password": "admin"
}
```

This secures the dashboard and allows you to log in with the specified credentials.

### Seeding Tickers
```csharp
options.AddOperationalStore<MyDbContext>(ef =>
{
    ef.UseTickerSeeder(
        timeTickerAsync: async tm => { /* seed time tickers */ },
        cronTickerAsync: async cm => { /* seed cron tickers */ });
});
```

### Handling Missed Jobs on Restart
```csharp
options.AddOperationalStore<MyDbContext>(ef =>
{
    ef.CancelMissedTickersOnAppStart();
});
```

### Global Exception Handler
```csharp
builder.Services.AddTickerQ(o =>
{
    o.SetExceptionHandler<MyTickerExceptionHandler>();
});
```
Implement `ITickerExceptionHandler` to centralize error handling for failed executions.

See [TickerQ Configuration](https://tickerq.arcenox.com/intro/configuration.html).
