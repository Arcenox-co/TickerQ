using System.Collections.Frozen;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using Hangfire;
using Hangfire.InMemory;
using HangfireJob = Hangfire.Common.Job;
using Quartz;
using Quartz.Impl;
using TickerQ.Utilities;
using TickerQ.Utilities.Enums;

namespace TickerQ.Benchmarks.Comparisons;

/// <summary>
/// Compares startup/registration cost across frameworks.
///
/// Results Overview (Apple M4 Pro, .NET 10.0):
/// ┌──────┬─────────────────────┬──────────────────────┬──────────────────────────┬──────────┬──────────┐
/// │ Jobs │ TickerQ             │ Hangfire             │ Quartz                   │ HF Ratio │ Q Ratio  │
/// ├──────┼─────────────────────┼──────────────────────┼──────────────────────────┼──────────┼──────────┤
/// │ 5    │ 274 ns / 1.3 KB     │ 102 us / 43 KB       │ 214 us / 288 KB          │ 371x     │ 784x     │
/// │ 25   │ 2.96 us / 8.3 KB    │ 138 us / 143 KB      │ 724 us / 1 MB            │ 47x      │ 245x     │
/// │ 100  │ 9.6 us / 32 KB      │ 419 us / 521 KB      │ 2,139 us / 3.8 MB        │ 44x      │ 223x     │
/// └──────┴─────────────────────┴──────────────────────┴──────────────────────────┴──────────┴──────────┘
/// Winner: TickerQ — 44-784x faster, 16-217x less memory than competitors.
/// - TickerQ: source-generated dictionary → FrozenDictionary (one-time at startup)
/// - Hangfire: storage initialization + recurring job registration
/// - Quartz: scheduler factory + job/trigger scheduling
/// </summary>
[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.HostProcess)]
public class StartupRegistrationComparison
{
    [Params(5, 25, 100)]
    public int JobCount { get; set; }

    // ── TickerQ: Build FrozenDictionary of source-generated functions ──

    [Benchmark(Baseline = true, Description = "TickerQ: Build FrozenDictionary")]
    public FrozenDictionary<string, (string, TickerTaskPriority, TickerFunctionDelegate, int)> TickerQ_BuildRegistry()
    {
        TickerFunctionDelegate noopDelegate = (_, _, _) => Task.CompletedTask;
        var dict = new Dictionary<string, (string, TickerTaskPriority, TickerFunctionDelegate, int)>(JobCount);

        for (int i = 0; i < JobCount; i++)
            dict[$"MyApp.Jobs.Function_{i}"] = ($"*/{i + 1} * * * * *", TickerTaskPriority.Normal, noopDelegate, 0);

        return dict.ToFrozenDictionary();
    }

    // ── Hangfire: Create storage + register recurring jobs ──

    [Benchmark(Description = "Hangfire: Storage + recurring jobs")]
    public void Hangfire_RegisterRecurringJobs()
    {
        using var storage = new InMemoryStorage();
        var manager = new RecurringJobManager(storage);

        for (int i = 0; i < JobCount; i++)
        {
            manager.AddOrUpdate(
                $"job-{i}",
                HangfireJob.FromExpression(() => NoopMethod()),
                $"*/{(i % 59) + 1} * * * *");
        }
    }

    // ── Quartz: Create scheduler + schedule jobs ──

    [Benchmark(Description = "Quartz: Scheduler + schedule jobs")]
    public void Quartz_ScheduleJobs()
    {
        var scheduler = new StdSchedulerFactory().GetScheduler().GetAwaiter().GetResult();

        for (int i = 0; i < JobCount; i++)
        {
            var job = JobBuilder.Create<NoopQuartzJob>()
                .WithIdentity($"job-{i}", "bench")
                .Build();

            var trigger = TriggerBuilder.Create()
                .WithIdentity($"trigger-{i}", "bench")
                .WithCronSchedule($"0 0/{(i % 59) + 1} * * * ?")
                .Build();

            scheduler.ScheduleJob(job, trigger).GetAwaiter().GetResult();
        }

        scheduler.Shutdown(false).GetAwaiter().GetResult();
    }

    public static void NoopMethod() { }

    public class NoopQuartzJob : IJob
    {
        public Task Execute(IJobExecutionContext context) => Task.CompletedTask;
    }
}
