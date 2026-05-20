# Issue: Add Periodic Ticker Support

## Summary

Add support for **Periodic Tickers** — jobs that execute at fixed time intervals
(e.g., every 5 minutes, every 30 seconds), filling the gap between one-shot
**Time Tickers** and calendar-based **Cron Tickers**.

## Motivation

| Need | Time Ticker | Cron Ticker | Periodic Ticker |
|---|---|---|---|
| Sub-second cadence (`100ms`, `500ms`) | n/a (one-shot) | ❌ | ✅ |
| Trivial API (`TimeSpan.FromMinutes(5)`) | n/a | needs cron syntax | ✅ |
| Drift-free interval from last execution | n/a | calendar-anchored | ✅ |
| Pause / resume without rewriting cron | n/a | manual | first-class |
| `[TickerFunction(PeriodicInterval = "...")]` declarative attribute | n/a | ✅ (cron) | ✅ |

The scheduler is `TimeSpan`-based (`Task.Delay`), so millisecond precision works
natively; the entity stores `TimeSpan Interval` directly.

## Public API

```csharp
// 1. Enable periodic support (opt-in)
services.AddTickerQ(options =>
{
    options.EnablePeriodic();                      // uses default PeriodicTickerEntity
    // OR options.EnablePeriodic<MyCustomPeriodicTicker>();

    options.AddOperationalStore(ef =>
    {
        ef.EnablePeriodic();                       // mirror flag for EF Core layer
        ef.UseTickerQDbContext<MyDbContext>(db => db.UseSqlServer(...));
    });
});

// 2. Imperative scheduling
var manager = sp.GetRequiredService<IPeriodicTickerManager<PeriodicTickerEntity>>();
await manager.AddAsync(new PeriodicTickerEntity
{
    Function = "MyJob",
    Interval = TimeSpan.FromMinutes(5),
    StartTime = DateTime.UtcNow.AddMinutes(1),     // optional
    EndTime   = DateTime.UtcNow.AddDays(7),        // optional
    Retries = 3,
    RetryIntervals = new[] { 5, 30, 120 }
});

// 3. Declarative — auto-seeded at startup
public class Jobs
{
    [TickerFunction("Heartbeat", PeriodicInterval = "00:00:05")]
    public Task HeartbeatAsync(TickerFunctionContext ctx, CancellationToken ct) { ... }
}
```

## Architecture (Path A)

The pre-existing core is parameterised over two entity types
(`InternalTickerManager<TTimeTicker, TCronTicker>`). Adding a third dimension
without breaking consumers is achieved through **inheritance + reflection-gated
DI**, _not_ by reworking generic arity of every public type.

```
TickerOptionsBuilder<TTimeTicker, TCronTicker>
    .EnablePeriodic<TPeriodicTicker>()  ──── opt-in flag
                          │
                          ▼ (reflection-typed registration)
InternalTickerManagerWithPeriodic<TTime, TCron, TPeriodic>
    : InternalTickerManager<TTime, TCron>
    + IPeriodicTickerPersistenceProvider<TPeriodic>
    + scheduler hooks for periodic occurrences
```

The same gating pattern is mirrored:

* **EF Core**: `TickerModelCustomizerWithPeriodic<,,>` and
  `TickerEFCorePeriodicPersistenceProvider<TContext, TPeriodic>` registered
  only when `EfCoreOptionBuilder.EnablePeriodic()` is called.
* **Dashboard**: `IPeriodicDashboardRepository<TPeriodicTicker>` +
  `PeriodicDashboardEndpoints.MapPeriodicEndpoints<T>()` plugged in via
  reflection from `DashboardEndpoints` only when `PeriodicEnabled` is set.

Consumers who do **not** call `EnablePeriodic()` see no behavioural or schema
change.

## Implementation Status

### ✅ Phase 1 — Core
- [x] `PeriodicTickerEntity`, `PeriodicTickerOccurrenceEntity<T>`
- [x] `IPeriodicTickerPersistenceProvider<T>` + in-memory implementation
- [x] `IPeriodicTickerManager<T>` + `PeriodicTickerManager<T>` with
      `CalculateNextExecution` (start-time, catch-up alignment, end-time)
- [x] `InternalTickerManagerWithPeriodic<,,>` + scheduler integration
- [x] `EnablePeriodic()` / `EnablePeriodic<T>()` on `TickerOptionsBuilder`

### ✅ Phase 2 — EF Core
- [x] `PeriodicTickerConfigurations<T>` + `PeriodicTickerOccurrenceConfigurations<T>`
- [x] `TickerEFCorePeriodicPersistenceProvider<TContext, T>`
- [x] `TickerModelCustomizerWithPeriodic<,,>` for `UseApplicationDbContext` flow
- [x] `TickerQDbContext<TTime, TCron, TPeriodic>` with periodic mappings in
      `OnModelCreating` for the `UseTickerQDbContext` flow
- [x] `EfCoreOptionBuilder.EnablePeriodic()` mirror

### ✅ Phase 3 — Dashboard backend
- [x] `IPeriodicDashboardRepository<T>` + `PeriodicDashboardRepository<T>`:
      paginated listing, 14-day occurrence graph window, AOT-safe request
      payload validation via `DashboardJsonOptions.GetTypeInfo()`
- [x] `PeriodicDashboardEndpoints.MapPeriodicEndpoints<T>()`: 17 REST endpoints
      covering CRUD, batch, pause/resume, toggle, occurrence delete, request fetch
- [x] `DashboardOptionsBuilder` — `PeriodicEnabled` / `PeriodicTickerType`
      propagated from core options; reflection-based DI + endpoint mapping

### ✅ Phase 4 — Source Generator + auto-seeding
- [x] `[TickerFunction(..., PeriodicInterval = "00:00:30")]` named property
- [x] Source-gen emits `RegisterPeriodicIntervals()` into
      `TickerQInstanceFactory.g.cs` using
      `TimeSpan.Parse(value, CultureInfo.InvariantCulture)`
- [x] `TickerFunctionProvider.TickerFunctionPeriodicIntervals`
      (`FrozenDictionary<string, TimeSpan>`) populated in `Build()`
- [x] `TickerQInitializerHostedService.SeedDefinedPeriodicTickersAsync`
      idempotently inserts new declared periodic tickers at startup
      (skipped when a row with the same `Function` already exists);
      seeded rows are tagged with `InitIdentifier=TQ_SYSTEM_<func>_<ticks>`
- [x] `ITickerOptionsSeeding` exposes `PeriodicEnabled` / `PeriodicTickerType`

### ⏳ Out-of-scope for this PR
- Dashboard frontend (Vue) — separate PR
- RemoteExecutor SDK additions for periodic — separate PR

## Tests

| Project | Periodic-related tests | Result |
|---|---|---|
| `TickerQ.SourceGenerator.Tests` | `PeriodicIntervalGenerationTests` × 4 | ✅ 4/4 |
| `TickerQ.Tests` | `PeriodicTickerManagerCalculateNextExecutionTests` × 7 | ✅ 7/7 |
| Manual sample | `TickerQ.Sample.Console` heartbeat every 5s | ✅ verified |

`dotnet build TickerQ.slnx` → **0 errors**, 11 pre-existing NuGet/analyzer warnings.

## Backward compatibility

- All existing public types retain their generic arity.
- All persistence-provider interfaces are untouched on the existing path.
- Periodic services, dashboard endpoints and EF mappings are conditionally
  registered; unconfigured deployments remain bit-for-bit identical.

## Notes

### Why `InternalTickerManagerWithPeriodic` instead of editing the base class?

`InternalTickerManager<TTime, TCron>` is a public surface across:
- the core scheduler,
- every persistence provider (`InMemory`, `EFCore`, `StackExchange.Redis`),
- the dashboard repository, the remote-executor SDK, downstream user code.

Adding a third type parameter or a constructor dependency to that class is a
**hard breaking change** for every caller of `IInternalTickerManager` and every
provider implementation. The chosen Path A — derive from the existing class,
add periodic capabilities by composition, and gate registration behind
`EnablePeriodic()` — keeps the existing generic surface intact while still
giving periodic-aware code a strong-typed entry point.

The duplicated scheduling glue is a deliberate trade-off; consolidation can
happen in a future major version once `Periodic` becomes the default.

