# Issue: Add Periodic Ticker Support

## Summary

Add support for **Periodic Tickers** - jobs that execute at fixed time intervals (e.g., every 5 minutes, every hour).

## Motivation

Currently TickerQ supports:
- **Time Tickers** - one-time scheduled jobs at a specific time
- **Cron Tickers** - recurring jobs based on cron expressions (calendar-based)

**The gap:** When you need a job that runs **both regularly AND at specific time intervals**, neither option fits perfectly:
- Time Tickers are one-time only
- Cron Tickers are calendar-based (e.g., "at minute 0 of every hour") rather than interval-based

**Periodic Tickers fill this gap** by providing:
1. **Interval-based scheduling** - "every 5 minutes" regardless of wall-clock time
2. **Simpler API** - `TimeSpan.FromMinutes(5)` vs cron syntax `*/5 * * * *`
3. **Sub-minute precision** - intervals like 30 seconds, which cron cannot express
4. **Drift-free execution** - maintains consistent intervals from last execution

## Precision Comparison

| Feature | Time Ticker | Cron Ticker | Periodic Ticker |
|---------|-------------|-------------|-----------------|
| Minimum interval | N/A (one-time) | 1 second* | **1 millisecond** |
| Scheduling type | One-time | Calendar-based | Interval-based |
| Sub-second support | ✅ (DateTime) | ❌ (NCrontab limitation) | ✅ (TimeSpan) |

*NCrontab supports seconds with `IncludingSeconds = true`, but minimum granularity is 1 second.

### Millisecond Support in TickerQ Architecture

**Yes, millisecond intervals are supported!**

The scheduler uses `Task.Delay(TimeSpan)` which natively supports millisecond precision:

```csharp
// In TickerQSchedulerBackgroundService.cs
sleepDuration = timeRemaining <= TimeSpan.Zero
    ? TimeSpan.FromMilliseconds(1)  // Minimum 1ms
    : timeRemaining;

await Task.Delay(sleepDuration, cancellationToken);
```

**Periodic Ticker uses `TimeSpan` for intervals:**
```csharp
public class PeriodicTickerEntity
{
    public virtual TimeSpan Interval { get; set; }  // Supports ms precision!
}

// Usage examples:
new PeriodicTickerEntity { Interval = TimeSpan.FromMilliseconds(100) }  // 100ms
new PeriodicTickerEntity { Interval = TimeSpan.FromSeconds(0.5) }       // 500ms
new PeriodicTickerEntity { Interval = TimeSpan.FromMinutes(5) }         // 5 min
```

### Performance Considerations for High-Frequency Jobs

For very short intervals (< 100ms), consider:
- Database overhead if using EF Core persistence
- In-memory provider recommended for high-frequency scenarios
- Thread pool saturation at extremely high frequencies

## Proposed Solution

### New Entity Types

```csharp
public class PeriodicTickerEntity : BaseTickerEntity
{
    public TimeSpan Interval { get; set; }
    public byte[] Request { get; set; }
    public int Retries { get; set; }
    public int[] RetryIntervals { get; set; }
    public bool IsActive { get; set; }
    public DateTime? StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public DateTime? LastExecutedAt { get; set; }
    public int ExecutionCount { get; set; }
}

public class PeriodicTickerOccurrenceEntity<TPeriodicTicker>
{
    public Guid Id { get; set; }
    public Guid PeriodicTickerId { get; set; }
    public TPeriodicTicker PeriodicTicker { get; set; }
    public DateTime ExecutionTime { get; set; }
    public TickerStatus Status { get; set; }
    // ... other occurrence properties
}
```

### Usage API

```csharp
// Enable Periodic support
services.AddTickerQ(options => {
    options.EnablePeriodic();
    options.AddOperationalStore(ef => ef.UseTickerQDbContext<MyDbContext>(...));
});

// Create periodic ticker via manager
var manager = serviceProvider.GetRequiredService<IPeriodicTickerManager<PeriodicTickerEntity>>();

await manager.CreateAsync(new PeriodicTickerEntity
{
    Function = "MyPeriodicJob",
    Interval = TimeSpan.FromMinutes(5),
    IsActive = true
});
```

## Implementation Scope

### Phase 1 (This PR) ✅
- [x] `PeriodicTickerEntity` and `PeriodicTickerOccurrenceEntity`
- [x] `IPeriodicTickerPersistenceProvider<T>` interface
- [x] `PeriodicTickerInMemoryPersistenceProvider<T>` implementation
- [x] `IPeriodicTickerManager<T>` and `PeriodicTickerManager<T>`
- [x] `InternalTickerManagerWithPeriodic<,,>` for scheduling
- [x] `EnablePeriodic()` / `EnablePeriodic<T>()` API in `TickerOptionsBuilder`

### Phase 2 (Future PR)
- [ ] EF Core configurations for Periodic tables
- [ ] `PeriodicTickerEfCorePersistenceProvider<T>`
- [ ] EF Core migrations support

### Phase 3 (Future PR)
- [ ] Dashboard UI for Periodic Tickers
- [ ] Dashboard API endpoints for Periodic management

## Breaking Changes

**None** - Periodic support is opt-in via `EnablePeriodic()`.

## Related Issues

- N/A (new feature)

---

# Pull Request: Add Periodic Ticker Support

## Description

This PR adds support for Periodic Tickers - jobs that execute at fixed time intervals.

## Changes

### TickerQ.Utilities

**New Files:**
- `Entities/PeriodicTickerEntity.cs` - Periodic ticker entity
- `Entities/PeriodicTickerOccurrenceEntity.cs` - Occurrence entity for tracking executions
- `Interfaces/IPeriodicTickerPersistenceProvider.cs` - Persistence provider interface
- `Interfaces/Managers/IPeriodicTickerManager.cs` - Manager interface
- `Managers/PeriodicTickerManager.cs` - Manager implementation
- `Managers/InternalTickerManagerWithPeriodic.cs` - Internal scheduler with Periodic support
- `Models/InternalManagerContext.cs` - Added `NextPeriodicOccurrence` property

**Modified Files:**
- `TickerOptionsBuilder.cs` - Added `EnablePeriodic()` and `EnablePeriodic<T>()` methods

### TickerQ

**New Files:**
- `Src/Provider/PeriodicTickerInMemoryPersistenceProvider.cs` - In-memory provider
- `DependencyInjection/TickerQPeriodicExtensions.cs` - Extension methods (deprecated, use `EnablePeriodic()`)

**Modified Files:**
- `DependencyInjection/TickerQServiceExtensions.cs` - Register Periodic services when enabled

## Usage

```csharp
// Simple usage with default PeriodicTickerEntity
services.AddTickerQ(options => {
    options.EnablePeriodic();
});

// With custom entity type
services.AddTickerQ(options => {
    options.EnablePeriodic<MyCustomPeriodicTicker>();
});

// With EF Core (Phase 2)
services.AddTickerQ(options => {
    options.EnablePeriodic();
    options.AddOperationalStore(ef => {
        ef.UseTickerQDbContext<TickerQDbContext>(db => db.UseSqlServer(...));
    });
});
```

## Testing

- [ ] Unit tests for `PeriodicTickerManager`
- [ ] Unit tests for `PeriodicTickerInMemoryPersistenceProvider`
- [ ] Integration tests for scheduling
- [ ] Manual testing with sample application

## Checklist

- [x] Code compiles without errors
- [x] No breaking changes to existing API
- [x] XML documentation added
- [ ] Unit tests added
- [ ] README updated
- [ ] Sample application updated

## Screenshots

N/A (no UI changes in this PR)

## Notes

### Why a separate `InternalTickerManagerWithPeriodic`?

The current TickerQ architecture has tight coupling between managers and persistence interfaces:

```csharp
// InternalTickerManager is tightly bound to ITickerPersistenceProvider
internal class InternalTickerManager<TTimeTicker, TCronTicker> : IInternalTickerManager
{
    private readonly ITickerPersistenceProvider<TTimeTicker, TCronTicker> _persistenceProvider;
    // Constructor requires this specific interface
}
```

**The problem:** Adding Periodic support to the existing `InternalTickerManager` would require:
1. Breaking change to constructor signature (add `IPeriodicTickerPersistenceProvider`)
2. Or making it optional via `IServiceProvider` (anti-pattern, hides dependencies)
3. Or modifying `ITickerPersistenceProvider` to include Periodic methods (breaks all existing implementations)

**Our solution:** Create `InternalTickerManagerWithPeriodic<TTime, TCron, TPeriodic>` that:
- Extends functionality without modifying existing classes
- Registered only when `EnablePeriodic()` is called
- Maintains full backward compatibility

**Trade-off:** ~600 lines of duplicated scheduling logic. This is intentional debt for backward compatibility.

### Future refactoring options

1. **Extract common scheduling logic** into a base class
2. **Use composition** with separate schedulers for Time/Cron/Periodic
3. **Introduce `ISchedulingProvider`** non-generic interface for core operations

These changes would be breaking and are deferred to a major version bump.
