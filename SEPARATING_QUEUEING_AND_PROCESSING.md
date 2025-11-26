# Separating Job Queueing from Job Processing

Starting with this version, TickerQ provides better separation of concerns between **job queueing** and **job processing**. This allows you to have some applications that only queue jobs, while other applications process them.

## Use Case

This is particularly useful in scenarios where you have:
- **Multiple API instances** that queue jobs but should NOT process them
- **One or more dedicated background worker services** that process all queued jobs

This prevents job competition between API instances and background workers, and provides better resource management.

## Configuration

### API Applications (Queue-Only Mode)

For applications that should only **queue jobs** without processing them:

```csharp
builder.Services.AddTickerQ(options =>
{
    // Disable background services - this app will only QUEUE jobs
    options.DisableBackgroundServices();
    
    options.AddOperationalStore(efOptions =>
    {
        efOptions.UseTickerQDbContext<TickerQDbContext>(dbOptions =>
        {
            dbOptions.UseSqlite("Data Source=tickerq.db");
        });
    });
});

// UseTickerQ will automatically detect that background services are disabled
app.UseTickerQ();
```

**What's Available in Queue-Only Mode:**
- ✅ `ITimeTickerManager<TTimeTicker>` - For queueing time-based jobs
- ✅ `ICronTickerManager<TCronTicker>` - For queueing cron-based jobs
- ✅ All persistence providers (EF Core, in-memory, etc.)
- ✅ `ITickerQHostScheduler` - NoOp implementation (safe to inject, but does nothing)
- ✅ `ITickerQDispatcher` - NoOp implementation (safe to inject, but does nothing)
- ❌ Background job processors (not registered as HostedServices)

### Background Worker Applications (Processing Mode)

For dedicated background worker services that **process jobs**:

```csharp
services.AddTickerQ(options =>
{
    // Background services are enabled by default
    // This app will both queue AND process jobs
    
    options.AddOperationalStore(efOptions =>
    {
        efOptions.UseTickerQDbContext<TickerQDbContext>(dbOptions =>
        {
            dbOptions.UseSqlite("Data Source=tickerq.db");
        });
    });
});

// Background services will start automatically
app.UseTickerQ(); // or UseTickerQ(TickerQStartMode.Immediate)
```

**What's Available in Processing Mode:**
- ✅ `ITimeTickerManager<TTimeTicker>` - For queueing time-based jobs
- ✅ `ICronTickerManager<TCronTicker>` - For queueing cron-based jobs
- ✅ All persistence providers (EF Core, in-memory, etc.)
- ✅ Background job processors (automatically registered)
- ✅ `ITickerQHostScheduler` - For manual control

## TickerQStartMode Options

The `UseTickerQ()` method accepts a `TickerQStartMode` parameter:

### `TickerQStartMode.Immediate` (Default)
- Background services start immediately (if registered)
- Jobs begin processing as soon as the application starts
- Use this for standard background worker applications

```csharp
app.UseTickerQ(TickerQStartMode.Immediate);
// or simply
app.UseTickerQ();
```

### `TickerQStartMode.Manual`
- Background services are registered but skip the first run
- Jobs won't start processing until you manually start the scheduler
- Useful when you need to perform initialization before processing jobs
- **Note:** This only works if background services are registered (i.e., you didn't call `.DisableBackgroundServices()`)

```csharp
app.UseTickerQ(TickerQStartMode.Manual);

// Later, start processing manually:
var scheduler = app.Services.GetRequiredService<ITickerQHostScheduler>();
await scheduler.StartAsync();
```

**When background services are disabled:**
- `UseTickerQ()` automatically detects this and skips background service initialization
- You can use the default call: `app.UseTickerQ();`
- The `TickerQStartMode` parameter is ignored when background services aren't registered

## Comparison Table

| Feature | Queue-Only Mode | Processing Mode |
|---------|-----------------|-----------------|
| Job Queueing | ✅ Yes | ✅ Yes |
| Job Processing | ❌ No | ✅ Yes |
| Background Services Registered | ❌ No | ✅ Yes |
| `ITimeTickerManager` | ✅ Available | ✅ Available |
| `ICronTickerManager` | ✅ Available | ✅ Available |
| `ITickerQHostScheduler` | ✅ NoOp (safe to inject) | ✅ Functional |
| `ITickerQDispatcher` | ✅ NoOp (safe to inject) | ✅ Functional |
| CPU/Memory Usage | 🟢 Low | 🟡 Higher |
| Use Case | APIs, Web Apps | Background Workers |

## Migration Guide

### Old Approach (Not Recommended)

Previously, you had to avoid calling `UseTickerQ()` in APIs to prevent job processing:

```csharp
// API - DON'T call UseTickerQ()
builder.Services.AddTickerQ(options => { /* config */ });
// app.UseTickerQ(); // ❌ Don't call this

// Background Worker - Call UseTickerQ()
builder.Services.AddTickerQ(options => { /* config */ });
app.UseTickerQ(); // ✅ Only here
```

**Problem:** This approach was unclear and background services were still registered in APIs.

### New Approach (Recommended)

Now you explicitly control background service registration:

```csharp
// API - Explicitly disable background services
builder.Services.AddTickerQ(options =>
{
    options.DisableBackgroundServices(); // ✅ Clear intent
    /* other config */
});
app.UseTickerQ(); // ✅ Automatically detects disabled background services

// Background Worker - Default behavior
builder.Services.AddTickerQ(options =>
{
    /* config - background services enabled by default */
});
app.UseTickerQ(); // ✅ Processes jobs
```

**Benefits:**
- ✅ Clear and explicit intent with `.DisableBackgroundServices()`
- ✅ Background services not registered in queue-only apps
- ✅ `UseTickerQ()` automatically adapts to the configuration
- ✅ Better resource management
- ✅ No accidental job processing in APIs

## Example: Multi-Instance Architecture

### Shared Database
Both applications must share the same operational store (database):

```csharp
// Same connection string in both apps
options.AddOperationalStore(efOptions =>
{
    efOptions.UseTickerQDbContext<TickerQDbContext>(dbOptions =>
    {
        dbOptions.UseSqlServer("Server=...;Database=TickerQOperations;");
    });
});
```

### API Instance (Queuing)
```csharp
// API Project - Only queues jobs
builder.Services.AddTickerQ(options =>
{
    options.DisableBackgroundServices(); // Queue-only mode
    options.AddOperationalStore(efOptions => { /* shared DB */ });
});

app.UseTickerQ(); // Automatically detects disabled services

app.MapPost("/schedule-job", async (ITimeTickerManager<TimeTickerEntity> manager) =>
{
    var result = await manager.AddAsync(new TimeTickerEntity
    {
        Function = "ProcessOrder",
        ExecutionTime = DateTime.UtcNow.AddMinutes(5)
    });
    
    return Results.Ok(new { result.Result.Id });
});
```

### Background Worker Instance (Processing)
```csharp
// Worker Project - Processes all jobs
services.AddTickerQ(options =>
{
    // Background services enabled by default
    options.AddOperationalStore(efOptions => { /* shared DB */ });
    
    options.ConfigureScheduler(scheduler =>
    {
        scheduler.MaxConcurrency = 10; // Process up to 10 jobs concurrently
    });
});

app.UseTickerQ(); // Start processing immediately
```

### Job Definition (Shared)
```csharp
// Can be in a shared class library
public class OrderJobs
{
    [TickerFunction("ProcessOrder")]
    public async Task ProcessOrderAsync(TickerFunctionContext context, 
        IOrderService orderService, CancellationToken ct)
    {
        // Job implementation
        await orderService.ProcessAsync(context.Id, ct);
    }
}
```

## Summary

The new `.DisableBackgroundServices()` method provides:

1. **Clear Separation**: Explicitly control which apps queue vs process jobs
2. **Better Resources**: Background services only run where needed
3. **No Competition**: Multiple APIs can safely queue jobs to a shared database
4. **Automatic Detection**: `UseTickerQ()` automatically adapts to your configuration
5. **Flexibility**: Mix and match queue-only and processing apps as needed

This approach aligns with modern microservices and distributed system architectures where job processing is often isolated to dedicated worker services.
