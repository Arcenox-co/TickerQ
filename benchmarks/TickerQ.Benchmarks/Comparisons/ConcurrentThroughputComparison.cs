using System.Collections.Frozen;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using Hangfire;
using Hangfire.InMemory;
using Hangfire.States;
using Quartz;
using Quartz.Impl;
using HangfireJob = Hangfire.Common.Job;
using TickerQ.Utilities;

namespace TickerQ.Benchmarks.Comparisons;

/// <summary>
/// Microbenchmarks of different operations, not comparable end-to-end job throughput.
/// TickerQ measures only dictionary lookup and delegate invocation; Hangfire and Quartz
/// also write scheduler storage. None of these methods waits for persisted job completion.
/// Use the `runtime` harness for TickerQ scheduler/persistence throughput and memory.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.HostProcess)]
public class ConcurrentThroughputComparison
{
    private FrozenDictionary<string, TickerFunctionDelegate> _tickerqFunctions = null!;
    private string[] _tickerqKeys = null!;
    private BackgroundJobClient _hangfireClient = null!;
    private InMemoryStorage _hangfireStorage = null!;
    private IScheduler _quartzScheduler = null!;
    private int _quartzCounter;

    [Params(100, 1000)]
    public int JobCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        // TickerQ: pre-built FrozenDictionary with 10 functions
        var dict = new Dictionary<string, TickerFunctionDelegate>();
        for (int i = 0; i < 10; i++)
            dict[$"MyApp.Jobs.Function_{i}"] = (_, _, _) => Task.CompletedTask;
        _tickerqFunctions = dict.ToFrozenDictionary();
        _tickerqKeys = dict.Keys.ToArray();

        // Hangfire
        _hangfireStorage = new InMemoryStorage();
        _hangfireClient = new BackgroundJobClient(_hangfireStorage);

        // Quartz
        _quartzScheduler = new StdSchedulerFactory().GetScheduler().GetAwaiter().GetResult();
        _quartzScheduler.Start().GetAwaiter().GetResult();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _hangfireStorage?.Dispose();
        _quartzScheduler?.Shutdown(false).GetAwaiter().GetResult();
    }

    // ── TickerQ: parallel lookup + invoke ──

    [Benchmark(Baseline = true, Description = "TickerQ: Parallel lookup + invoke only")]
    public void TickerQ_ParallelDispatch()
    {
        Parallel.For(0, JobCount, i =>
        {
            var key = _tickerqKeys[i % _tickerqKeys.Length];
            if (_tickerqFunctions.TryGetValue(key, out var del))
                del(CancellationToken.None, null!, null!).GetAwaiter().GetResult();
        });
    }

    // ── Hangfire: parallel enqueue ──

    [Benchmark(Description = "Hangfire: Parallel enqueue")]
    public void Hangfire_ParallelEnqueue()
    {
        Parallel.For(0, JobCount, i =>
        {
            _hangfireClient.Create(
                HangfireJob.FromExpression(() => NoopMethod()),
                new EnqueuedState());
        });
    }

    // ── Quartz: parallel schedule ──

    [Benchmark(Description = "Quartz: Parallel schedule")]
    public void Quartz_ParallelSchedule()
    {
        Parallel.For(0, JobCount, i =>
        {
            var id = Interlocked.Increment(ref _quartzCounter);
            var job = JobBuilder.Create<NoopQuartzJob>()
                .WithIdentity($"job-{id}", "throughput")
                .Build();

            var trigger = TriggerBuilder.Create()
                .WithIdentity($"trigger-{id}", "throughput")
                .StartAt(DateTimeOffset.UtcNow.AddHours(1))
                .Build();

            _quartzScheduler.ScheduleJob(job, trigger).GetAwaiter().GetResult();
        });
    }

    // ── Sequential variants for comparison ──

    [Benchmark(Description = "TickerQ: Sequential lookup + invoke only")]
    public void TickerQ_SequentialDispatch()
    {
        for (int i = 0; i < JobCount; i++)
        {
            var key = _tickerqKeys[i % _tickerqKeys.Length];
            if (_tickerqFunctions.TryGetValue(key, out var del))
                del(CancellationToken.None, null!, null!).GetAwaiter().GetResult();
        }
    }

    [Benchmark(Description = "Hangfire: Sequential enqueue")]
    public void Hangfire_SequentialEnqueue()
    {
        for (int i = 0; i < JobCount; i++)
        {
            _hangfireClient.Create(
                HangfireJob.FromExpression(() => NoopMethod()),
                new EnqueuedState());
        }
    }

    [Benchmark(Description = "Quartz: Sequential schedule")]
    public void Quartz_SequentialSchedule()
    {
        for (int i = 0; i < JobCount; i++)
        {
            var id = Interlocked.Increment(ref _quartzCounter);
            var job = JobBuilder.Create<NoopQuartzJob>()
                .WithIdentity($"job-{id}", "seq")
                .Build();

            var trigger = TriggerBuilder.Create()
                .WithIdentity($"trigger-{id}", "seq")
                .StartAt(DateTimeOffset.UtcNow.AddHours(1))
                .Build();

            _quartzScheduler.ScheduleJob(job, trigger).GetAwaiter().GetResult();
        }
    }

    public static void NoopMethod() { }

    public class NoopQuartzJob : IJob
    {
        public Task Execute(IJobExecutionContext context) => Task.CompletedTask;
    }
}
