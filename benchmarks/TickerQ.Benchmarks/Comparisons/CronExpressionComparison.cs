using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using NCrontab;
using QuartzCron = Quartz.CronExpression;

namespace TickerQ.Benchmarks.Comparisons;

/// <summary>
/// Head-to-head cron expression parsing and next-occurrence calculation.
/// TickerQ uses NCrontab (6-part, second-level). Quartz uses its own CronExpression (7-part with year).
/// Hangfire delegates to NCrontab internally, so it's excluded here.
///
/// Results Overview (Apple M4 Pro, .NET 10.0):
/// ┌─────────────────────────────┬────────────┬─────────────┬─────────┬─────────────────────┐
/// │ Operation                   │ TickerQ    │ Quartz      │ Speedup │ Memory              │
/// ├─────────────────────────────┼────────────┼─────────────┼─────────┼─────────────────────┤
/// │ Parse simple                │ 229 ns     │ 3,835 ns    │ 16.7x   │ 1.4 KB vs 10.8 KB   │
/// │ Parse complex               │ 317 ns     │ 3,017 ns    │ 9.5x    │ 1.5 KB vs 8.7 KB    │
/// │ Parse second-level          │ 276 ns     │ 4,598 ns    │ 16.6x   │ 1.7 KB vs 12.9 KB   │
/// │ Next occurrence (simple)    │ 14.6 ns    │ 1,292 ns    │ 88x     │ 0 B vs 3.2 KB       │
/// │ Next occurrence (complex)   │ 12.9 ns    │ 1,119 ns    │ 87x     │ 0 B vs 3.1 KB       │
/// │ Next occurrence (second)    │ 26.7 ns    │ 1,318 ns    │ 49x     │ 0 B vs 2.7 KB       │
/// │ 100 next occurrences        │ 1,556 ns   │ 128,580 ns  │ 82x     │ 0 B vs 314 KB       │
/// └─────────────────────────────┴────────────┴─────────────┴─────────┴─────────────────────┘
/// Winner: TickerQ (NCrontab) — 10-88x faster, zero allocations on next-occurrence.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.HostProcess)]
public class CronExpressionComparison
{
    private CrontabSchedule _ncrontabSimple = null!;
    private CrontabSchedule _ncrontabComplex = null!;
    private CrontabSchedule _ncrontabSecondLevel = null!;
    private QuartzCron _quartzSimple = null!;
    private QuartzCron _quartzComplex = null!;
    private QuartzCron _quartzSecondLevel = null!;

    private DateTime _baseTime;
    private DateTimeOffset _baseTimeOffset;

    private static readonly CrontabSchedule.ParseOptions SecondOptions = new() { IncludingSeconds = true };

    // NCrontab format:       min hour dom month dow  (5-part) or sec min hour dom month dow (6-part)
    // Quartz format:         sec min hour dom month dow [year]
    private const string SimpleNcrontab = "*/5 * * * *";           // every 5 min
    private const string SimpleQuartz   = "0 0/5 * * * ?";        // every 5 min
    private const string ComplexNcrontab = "0 9-17 * * 1-5";      // weekday business hours
    private const string ComplexQuartz   = "0 0 9-17 ? * MON-FRI"; // weekday business hours
    private const string SecondNcrontab  = "*/30 * * * * *";      // every 30s (6-part)
    private const string SecondQuartz    = "0/30 * * * * ?";      // every 30s

    [GlobalSetup]
    public void Setup()
    {
        _baseTime = new DateTime(2026, 3, 16, 12, 0, 0, DateTimeKind.Utc);
        _baseTimeOffset = new DateTimeOffset(_baseTime, TimeSpan.Zero);

        _ncrontabSimple      = CrontabSchedule.Parse(SimpleNcrontab);
        _ncrontabComplex     = CrontabSchedule.Parse(ComplexNcrontab);
        _ncrontabSecondLevel = CrontabSchedule.Parse(SecondNcrontab, SecondOptions);

        _quartzSimple      = new QuartzCron(SimpleQuartz);
        _quartzComplex     = new QuartzCron(ComplexQuartz);
        _quartzSecondLevel = new QuartzCron(SecondQuartz);
    }

    // ── Parse: Simple ──

    [Benchmark(Description = "TickerQ (NCrontab): Parse simple")]
    public CrontabSchedule TickerQ_Parse_Simple() =>
        CrontabSchedule.Parse(SimpleNcrontab);

    [Benchmark(Description = "Quartz: Parse simple")]
    public QuartzCron Quartz_Parse_Simple() =>
        new QuartzCron(SimpleQuartz);

    // ── Parse: Complex ──

    [Benchmark(Description = "TickerQ (NCrontab): Parse complex")]
    public CrontabSchedule TickerQ_Parse_Complex() =>
        CrontabSchedule.Parse(ComplexNcrontab);

    [Benchmark(Description = "Quartz: Parse complex")]
    public QuartzCron Quartz_Parse_Complex() =>
        new QuartzCron(ComplexQuartz);

    // ── Parse: Second-level ──

    [Benchmark(Description = "TickerQ (NCrontab): Parse second-level")]
    public CrontabSchedule TickerQ_Parse_SecondLevel() =>
        CrontabSchedule.Parse(SecondNcrontab, SecondOptions);

    [Benchmark(Description = "Quartz: Parse second-level")]
    public QuartzCron Quartz_Parse_SecondLevel() =>
        new QuartzCron(SecondQuartz);

    // ── NextOccurrence: Simple ──

    [Benchmark(Description = "TickerQ (NCrontab): Next simple")]
    public DateTime TickerQ_Next_Simple() =>
        _ncrontabSimple.GetNextOccurrence(_baseTime);

    [Benchmark(Description = "Quartz: Next simple")]
    public DateTimeOffset? Quartz_Next_Simple() =>
        _quartzSimple.GetNextValidTimeAfter(_baseTimeOffset);

    // ── NextOccurrence: Complex ──

    [Benchmark(Description = "TickerQ (NCrontab): Next complex")]
    public DateTime TickerQ_Next_Complex() =>
        _ncrontabComplex.GetNextOccurrence(_baseTime);

    [Benchmark(Description = "Quartz: Next complex")]
    public DateTimeOffset? Quartz_Next_Complex() =>
        _quartzComplex.GetNextValidTimeAfter(_baseTimeOffset);

    // ── NextOccurrence: Second-level ──

    [Benchmark(Description = "TickerQ (NCrontab): Next second-level")]
    public DateTime TickerQ_Next_SecondLevel() =>
        _ncrontabSecondLevel.GetNextOccurrence(_baseTime);

    [Benchmark(Description = "Quartz: Next second-level")]
    public DateTimeOffset? Quartz_Next_SecondLevel() =>
        _quartzSecondLevel.GetNextValidTimeAfter(_baseTimeOffset);

    // ── Batch: 100 next occurrences ──

    [Benchmark(Description = "TickerQ (NCrontab): 100 next occurrences")]
    public int TickerQ_Next100()
    {
        var current = _baseTime;
        for (int i = 0; i < 100; i++)
            current = _ncrontabSimple.GetNextOccurrence(current);
        return 100;
    }

    [Benchmark(Description = "Quartz: 100 next occurrences")]
    public int Quartz_Next100()
    {
        var current = _baseTimeOffset;
        for (int i = 0; i < 100; i++)
        {
            var next = _quartzSimple.GetNextValidTimeAfter(current);
            if (next == null) break;
            current = next.Value;
        }
        return 100;
    }
}
