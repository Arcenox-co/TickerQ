using System.Diagnostics;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using TickerQ.BackgroundServices;
using TickerQ.Dispatcher;
using TickerQ.Provider;
using TickerQ.TickerQThreadPool;
using TickerQ.Utilities;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Instrumentation;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Interfaces.Managers;
using TickerQ.Utilities.Licensing;
using TickerQ.Utilities.Managers;
using TickerQ.Utilities.Temps;

namespace TickerQ.Benchmarks;

/// <summary>
/// Measures persisted independent chains through the real manager, acquisition, scheduler,
/// execution handler and terminal-write paths. Process-wide allocation counters include workers.
/// </summary>
internal static class RuntimeExecutionBenchmark
{
    private sealed record Settings(int Roots = 1000, int ChainLength = 8, int Concurrency = 8,
        int Producers = 4, int Workflows = 32, int QueueCapacity = 128, int ChainParallelism = 4,
        string Shape = "serial", string Work = "none", int WorkUnits = 1000, int DelayMs = 1,
        int Warmup = 1, int Iterations = 5, int TimeoutSeconds = 120, string Label = "local", string? Json = null);

    private sealed record Sample(int Iteration, int Roots, int PersistedSteps, double ElapsedSeconds,
        double RootsPerSecond, double StepsPerSecond, long AllocatedBytes, double AllocatedBytesPerStep,
        int Gen0Collections, int Gen1Collections, int Gen2Collections, double CpuSeconds, long ManagedBytesBefore,
        long ManagedBytesAfter, long SampledPeakManagedBytes, long SampledPeakWorkingSetBytes);

    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Contains("--help"))
        {
            Console.WriteLine("runtime [--roots 1000] [--chain-length 8] [--concurrency 8] [--producers 4] " +
                "[--workflows 32] [--queue-capacity 128] [--chain-parallelism 4] [--shape serial|wide] " +
                "[--work none|cpu|yield|delay] [--work-units 1000] [--delay-ms 1] " +
                "[--warmup 1] [--iterations 5] [--timeout-seconds 120] [--label local] [--json results.json]");
            return 0;
        }

        try
        {
            var settings = Parse(args);
            var environment = new
            {
                Runtime = RuntimeInformation.FrameworkDescription, OS = RuntimeInformation.OSDescription,
                Architecture = RuntimeInformation.ProcessArchitecture.ToString(), Environment.ProcessorCount,
                ServerGC = GCSettings.IsServerGC, Provider = "TickerQ in-memory, one shared runtime partition",
                Measurement = "Concurrent public AddAsync + RunNowAsync through persisted terminal completion"
            };
            Console.WriteLine(JsonSerializer.Serialize(new { settings, environment }));
            var samples = new List<Sample>();
            for (var iteration = -settings.Warmup; iteration < settings.Iterations; iteration++)
            {
                await using var rig = new RuntimeRig(settings);
                var sample = await MeasureAsync(rig, settings, iteration + 1);
                if (iteration < 0)
                    Console.WriteLine($"warmup complete: {sample.PersistedSteps} persisted steps");
                else
                {
                    samples.Add(sample);
                    Console.WriteLine($"sample {sample.Iteration}: {sample.StepsPerSecond:N0} steps/s, " +
                        $"{sample.RootsPerSecond:N0} roots/s, {sample.AllocatedBytesPerStep:N0} B/step, " +
                        $"sampled managed peak {sample.SampledPeakManagedBytes / 1048576d:N1} MiB");
                }
            }
            var report = new { settings, environment, samples };
            if (settings.Json is { } path)
            {
                await File.WriteAllTextAsync(path, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
                Console.WriteLine($"Saved {Path.GetFullPath(path)}");
            }
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static async Task<Sample> MeasureAsync(RuntimeRig rig, Settings settings, int iteration)
    {
        // Construction, registry setup and forced collection are outside the measured window.
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var managedBefore = GC.GetTotalMemory(false);
        var collections = Enumerable.Range(0, 3).Select(GC.CollectionCount).ToArray();
        using var process = Process.GetCurrentProcess();
        var cpuBefore = process.TotalProcessorTime;
        using var sampling = new CancellationTokenSource();
        long managedPeak = managedBefore;
        long workingSetPeak = process.WorkingSet64;
        async Task SampleMemoryAsync()
        {
            try
            {
                while (true)
                {
                    await Task.Delay(10, sampling.Token).ConfigureAwait(false);
                    process.Refresh();
                    managedPeak = Math.Max(managedPeak, GC.GetTotalMemory(false));
                    workingSetPeak = Math.Max(workingSetPeak, process.WorkingSet64);
                }
            }
            catch (OperationCanceledException) when (sampling.IsCancellationRequested) { }
        }
        var sampler = SampleMemoryAsync();
        var allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
        var timer = Stopwatch.StartNew();
        try
        {
            await rig.ExecuteAsync().ConfigureAwait(false);
        }
        finally
        {
            timer.Stop();
            sampling.Cancel();
            await sampler.ConfigureAwait(false);
        }
        var allocated = GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore;
        var managedAfter = GC.GetTotalMemory(false);
        process.Refresh();
        managedPeak = Math.Max(managedPeak, managedAfter);
        workingSetPeak = Math.Max(workingSetPeak, process.WorkingSet64);
        var gcCounts = Enumerable.Range(0, 3).Select(g => GC.CollectionCount(g) - collections[g]).ToArray();
        var cpuSeconds = (process.TotalProcessorTime - cpuBefore).TotalSeconds;
        // Verify every persisted node after measuring; a fast enqueue without completion is invalid.
        var persistedSteps = await rig.VerifyAsync().ConfigureAwait(false);
        var seconds = timer.Elapsed.TotalSeconds;
        return new Sample(iteration, settings.Roots, persistedSteps, seconds, settings.Roots / seconds,
            persistedSteps / seconds, allocated, (double)allocated / persistedSteps,
            gcCounts[0], gcCounts[1], gcCounts[2], cpuSeconds,
            managedBefore, managedAfter, managedPeak, workingSetPeak);
    }

    private static Settings Parse(string[] args)
    {
        if (args.Length % 2 != 0) throw new ArgumentException("Arguments must be --name value pairs; use runtime --help.");
        var result = new Settings();
        for (var i = 0; i < args.Length; i += 2)
        {
            string value = args[i + 1];
            int Number(int minimum = 1) => int.TryParse(value, out var number) && number >= minimum
                ? number : throw new ArgumentException($"Invalid value for {args[i]}: {value}");
            result = args[i] switch
            {
                "--roots" => result with { Roots = Number() },
                "--chain-length" => result with { ChainLength = Number() },
                "--concurrency" => result with { Concurrency = Number() },
                "--producers" => result with { Producers = Number() },
                "--workflows" => result with { Workflows = Number() },
                "--queue-capacity" => result with { QueueCapacity = Number() },
                "--chain-parallelism" => result with { ChainParallelism = Number() },
                "--shape" => result with { Shape = value },
                "--work" => result with { Work = value },
                "--work-units" => result with { WorkUnits = Number() },
                "--delay-ms" => result with { DelayMs = Number() },
                "--warmup" => result with { Warmup = Number(0) },
                "--iterations" => result with { Iterations = Number() },
                "--timeout-seconds" => result with { TimeoutSeconds = Number() },
                "--label" => result with { Label = value },
                "--json" => result with { Json = value },
                _ => throw new ArgumentException($"Unknown option {args[i]}")
            };
        }
        if (result.Shape is not ("serial" or "wide") || result.Work is not ("none" or "cpu" or "yield" or "delay"))
            throw new ArgumentException("Shape must be serial or wide; work must be none, cpu, yield or delay.");
        _ = checked(result.Roots * result.ChainLength);
        return result;
    }

    private sealed class BenchmarkTicker : TimeTickerEntity<BenchmarkTicker>
    {
        public BenchmarkTicker() { }
    }

    private sealed class RuntimeRig : IAsyncDisposable
    {
        private const string FunctionName = "RuntimeBenchmark.Step";
        private readonly Settings _settings;
        private readonly TickerQTaskScheduler _scheduler;
        private readonly ServiceProvider _services;
        private readonly AcquiredExecutionQueue _queue;
        private readonly TickerInMemoryPersistenceProvider<BenchmarkTicker, CronTickerEntity> _persistence;
        private readonly ITimeTickerManager<BenchmarkTicker> _manager;
        private readonly Guid[] _rootIds;
        private int _executed;
        private long _cpuResult;

        internal RuntimeRig(Settings settings)
        {
            _settings = settings;
            _rootIds = new Guid[settings.Roots];
            var options = new SchedulerOptionsBuilder
            {
                MaxConcurrency = settings.Concurrency, MaxActiveWorkflows = settings.Workflows,
                MaxQueuedExecutions = settings.QueueCapacity, MaxConcurrentChainSteps = settings.ChainParallelism
            };
            options.BindRuntimeActivationScope("runtime-benchmark", 1, schedulerEnabled: false);
            options.Validate();
            var clock = new TickerSystemClock();
            _scheduler = new TickerQTaskScheduler(options);
            _services = new ServiceCollection().AddSingleton<ITickerClock>(clock).AddSingleton(options)
                .AddSingleton<ITickerQTaskScheduler>(_scheduler).BuildServiceProvider();
            _persistence = new TickerInMemoryPersistenceProvider<BenchmarkTicker, CronTickerEntity>(_services);
            var notifications = new NoOpTickerQNotificationHubSender();
            var internalManager = new InternalTickerManager<BenchmarkTicker, CronTickerEntity>(
                _persistence, clock, notifications, options);
            var license = new TickerQLicenseStateProvider(clock);
            // Isolated benchmark fixture: startup certificate verification is outside this workload.
            var now = DateTimeOffset.UtcNow;
            license.Publish(TickerQLicenseState.ForCertificate(TickerQLicenseStatus.Active, false,
                Guid.NewGuid(), Guid.NewGuid(), "Benchmark fixture", "Business", "Full", now.AddDays(-1),
                now.AddDays(1), 1, 4, "Ed25519", "benchmark-fixture", "5.x"));
            var handler = new TickerExecutionTaskHandler(_services, clock,
                new LoggerInstrumentation(NullLogger<LoggerInstrumentation>.Instance, options), internalManager,
                options, new NoOpTickerQFailureNotifier(), license, new TickerFunctionConcurrencyGate());
            _queue = new AcquiredExecutionQueue(internalManager, handler, _scheduler, NullLogger.Instance, license);
            var dispatcher = new TickerQDispatcher(_scheduler, handler, license, _queue);
            _manager = new TickerManager<BenchmarkTicker, CronTickerEntity>(_persistence,
                new NoOpTickerQHostScheduler(), clock, notifications,
                new TickerExecutionContext { RuntimePartition = options.RuntimePartition }, dispatcher,
                options, new TickerQActivationGate());
            TickerFunctionProvider.ReplaceFunctions(new Dictionary<string, (string, TickerTaskPriority, TickerFunctionDelegate, int)>
            {
                [FunctionName] = ("", TickerTaskPriority.Normal, ExecuteStepAsync, 0)
            });
            TickerFunctionProvider.Build();
        }

        private async Task ExecuteStepAsync(CancellationToken token, IServiceProvider _, Utilities.Base.TickerFunctionContext __)
        {
            switch (_settings.Work)
            {
                case "cpu":
                    long value = 17;
                    for (var i = 0; i < _settings.WorkUnits; i++)
                    {
                        if ((i & 1023) == 0) token.ThrowIfCancellationRequested();
                        value = unchecked(value * 31 + i);
                    }
                    Interlocked.Add(ref _cpuResult, value);
                    break;
                case "yield": await Task.Yield(); break;
                case "delay": await Task.Delay(_settings.DelayMs, token).ConfigureAwait(false); break;
            }
            Interlocked.Increment(ref _executed);
        }

        internal async Task ExecuteAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(_settings.TimeoutSeconds));
            var token = timeout.Token;
            var published = Channel.CreateBounded<Guid>(new BoundedChannelOptions(_settings.Workflows)
            {
                SingleReader = true, SingleWriter = false, FullMode = BoundedChannelFullMode.Wait
            });
            var nextRoot = -1;
            async Task ProduceAsync()
            {
                while (true)
                {
                    var index = Interlocked.Increment(ref nextRoot);
                    if (index >= _settings.Roots) return;
                    var root = new BenchmarkTicker { Function = FunctionName, ExecutionTime = DateTime.UtcNow.AddDays(1) };
                    var parent = root;
                    for (var i = 1; i < _settings.ChainLength; i++)
                    {
                        var child = new BenchmarkTicker { Function = FunctionName, RunCondition = RunCondition.OnSuccess };
                        parent.Children.Add(child);
                        if (_settings.Shape == "serial") parent = child;
                    }
                    var saved = await _manager.AddAsync(root, token).ConfigureAwait(false);
                    if (!saved.IsSucceeded || saved.AffectedRows != _settings.ChainLength)
                        throw new InvalidOperationException("Benchmark persistence did not accept the full chain.", saved.Exception);
                    _rootIds[index] = root.Id;
                    await published.Writer.WriteAsync(root.Id, token).ConfigureAwait(false);
                }
            }
            async Task ProduceAllAsync()
            {
                try
                {
                    await Task.WhenAll(Enumerable.Range(0, _settings.Producers)
                        .Select(_ => Task.Run(ProduceAsync, token))).ConfigureAwait(false);
                    published.Writer.TryComplete();
                }
                catch (Exception error) { published.Writer.TryComplete(error); timeout.Cancel(); throw; }
            }
            async Task DispatchAsync()
            {
                try
                {
                    await foreach (var rootId in published.Reader.ReadAllAsync(token).ConfigureAwait(false))
                    {
                        // A single dispatcher prevents preflight/admission races. Producers still
                        // compete with independent running chains on the same provider's graph locks.
                        await ((ITickerQExecutionScheduler)_scheduler).WaitForOrchestrationCapacityAsync(token).ConfigureAwait(false);
                        var started = await _manager.RunNowAsync(rootId, token).ConfigureAwait(false);
                        if (!started.IsSucceeded || started.AffectedRows != 1)
                            throw new InvalidOperationException("Benchmark root was not acquired.", started.Exception);
                    }
                }
                catch { timeout.Cancel(); throw; }
            }
            try
            {
                await Task.WhenAll(ProduceAllAsync(), DispatchAsync()).ConfigureAwait(false);
                if (!await _scheduler.WaitForRunningTasksAsync(null, token).ConfigureAwait(false))
                    throw new TimeoutException("Benchmark execution did not drain before its deadline.");
            }
            catch { timeout.Cancel(); throw; }
        }

        internal async Task<int> VerifyAsync()
        {
            var count = 0;
            foreach (var rootId in _rootIds)
            {
                var root = await _persistence.GetTimeTickerById(rootId).ConfigureAwait(false);
                if (root == null) throw new InvalidOperationException("A benchmark root disappeared.");
                var pending = new Stack<BenchmarkTicker>();
                pending.Push(root);
                while (pending.TryPop(out var row))
                {
                    if (row.Status != TickerStatus.Done) throw new InvalidOperationException($"Job {row.Id} ended as {row.Status}.");
                    count++;
                    foreach (var child in row.Children) pending.Push(child);
                }
            }
            if (count != checked(_settings.Roots * _settings.ChainLength) || count != _executed)
                throw new InvalidOperationException($"Expected {_settings.Roots * _settings.ChainLength} persisted executions; got {count} rows, {_executed} calls.");
            GC.KeepAlive(_cpuResult);
            return count;
        }

        public async ValueTask DisposeAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await _queue.StopAsync(timeout.Token).ConfigureAwait(false);
            _queue.Dispose();
            await _scheduler.DisposeAsync().ConfigureAwait(false);
            await _services.DisposeAsync().ConfigureAwait(false);
            TickerInMemoryPersistenceProvider<BenchmarkTicker, CronTickerEntity>.ResetAllStateForTests();
        }
    }
}
