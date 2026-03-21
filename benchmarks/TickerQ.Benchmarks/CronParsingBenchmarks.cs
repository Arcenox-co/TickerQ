using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using NCrontab;

namespace TickerQ.Benchmarks;

/// <summary>
/// Benchmarks for cron expression parsing and next-occurrence calculation.
/// TickerQ uses NCrontab with 6-part (second-level) cron support.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.HostProcess)]
public class CronParsingBenchmarks
{
    private CrontabSchedule _simpleSchedule = null!;
    private CrontabSchedule _complexSchedule = null!;
    private CrontabSchedule _secondLevelSchedule = null!;
    private DateTime _baseTime;

    private static readonly CrontabSchedule.ParseOptions SecondLevelOptions = new() { IncludingSeconds = true };

    [GlobalSetup]
    public void Setup()
    {
        _baseTime = new DateTime(2026, 3, 16, 12, 0, 0, DateTimeKind.Utc);
        _simpleSchedule = CrontabSchedule.Parse("*/5 * * * *");
        _complexSchedule = CrontabSchedule.Parse("0 9-17 * * 1-5");
        _secondLevelSchedule = CrontabSchedule.Parse("*/30 * * * * *", SecondLevelOptions);
    }

    // ── Parsing ──

    [Benchmark(Description = "Parse: Simple (*/5 * * * *)")]
    public CrontabSchedule Parse_Simple() =>
        CrontabSchedule.Parse("*/5 * * * *");

    [Benchmark(Description = "Parse: Complex (0 9-17 * * 1-5)")]
    public CrontabSchedule Parse_Complex() =>
        CrontabSchedule.Parse("0 9-17 * * 1-5");

    [Benchmark(Description = "Parse: 6-part second-level (*/30 * * * * *)")]
    public CrontabSchedule Parse_SecondLevel() =>
        CrontabSchedule.Parse("*/30 * * * * *", SecondLevelOptions);

    // ── Next occurrence ──

    [Benchmark(Description = "NextOccurrence: Simple")]
    public DateTime Next_Simple() =>
        _simpleSchedule.GetNextOccurrence(_baseTime);

    [Benchmark(Description = "NextOccurrence: Complex (weekday business hours)")]
    public DateTime Next_Complex() =>
        _complexSchedule.GetNextOccurrence(_baseTime);

    [Benchmark(Description = "NextOccurrence: 6-part second-level")]
    public DateTime Next_SecondLevel() =>
        _secondLevelSchedule.GetNextOccurrence(_baseTime);

    // ── Batch: next N occurrences ──

    [Benchmark(Description = "Next 100 occurrences: Simple")]
    public List<DateTime> Next100_Simple()
    {
        var results = new List<DateTime>(100);
        var current = _baseTime;
        for (int i = 0; i < 100; i++)
        {
            current = _simpleSchedule.GetNextOccurrence(current);
            results.Add(current);
        }
        return results;
    }

    [Benchmark(Description = "Next 100 occurrences: 6-part")]
    public List<DateTime> Next100_SecondLevel()
    {
        var results = new List<DateTime>(100);
        var current = _baseTime;
        for (int i = 0; i < 100; i++)
        {
            current = _secondLevelSchedule.GetNextOccurrence(current);
            results.Add(current);
        }
        return results;
    }
}
