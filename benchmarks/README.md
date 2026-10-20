# Runtime throughput and memory

The `runtime` harness measures complete TickerQ work against the real in-memory provider.
Concurrent producers build independent chains and call the public time manager's `AddAsync`.
One admission coordinator calls `RunNowAsync`; the actual acquired-job queue, scheduler,
function handler, chain coordinator, and fenced terminal writes run each chain to completion.
Producers, acquisitions, and independent executing chains share one provider partition and
therefore compete for its graph locks. Every node's persisted `Done` state and the exact
handler invocation count are checked after each sample. A failed or incomplete run exits nonzero.

Build once, then run the compiled Release binary without a debugger or other benchmarks:

```sh
dotnet build benchmarks/TickerQ.Benchmarks/TickerQ.Benchmarks.csproj -c Release -p:GeneratePackageOnBuild=false
dotnet benchmarks/TickerQ.Benchmarks/bin/Release/net10.0/TickerQ.Benchmarks.dll runtime --roots 1000 --chain-length 8 --concurrency 8 --producers 4 --workflows 32 --queue-capacity 128 --warmup 1 --iterations 5 --label baseline --json /tmp/tickerq-runtime-baseline.json
```

Use the same command, machine, .NET version, power settings, and GC configuration for each
revision. Record `git rev-parse HEAD` alongside the JSON output; use `--label` to identify the
revision. Compare the distribution of the five samples, rather than a single best run.
Each sample starts with an empty provider and fresh runtime. Setup, forced pre-sample GC,
post-run verification, and disposal are outside the elapsed-time window.

Useful workloads (append `--warmup 1 --iterations 5 --json /tmp/result.json` to capture samples):

| Workload | Arguments after `runtime` |
| --- | --- |
| Standalone scheduling/persistence overhead | `--roots 10000 --chain-length 1 --concurrency 8 --producers 4 --work none` |
| Independent serial chains contending on persistence | `--roots 1000 --chain-length 32 --concurrency 8 --producers 8 --workflows 64 --work none` |
| Concurrent sibling steps | `--roots 500 --chain-length 32 --shape wide --chain-parallelism 4 --concurrency 8 --producers 4 --work yield` |
| Simulated asynchronous I/O | `--roots 500 --chain-length 8 --concurrency 8 --producers 4 --work delay --delay-ms 1` |
| CPU work plus runtime overhead | `--roots 1000 --chain-length 8 --concurrency 8 --producers 4 --work cpu --work-units 10000` |

Vary `--concurrency` over 1, 2, 4, 8 and `--producers` over 1, 4, 8 with other arguments fixed
to inspect execution scaling and persistence contention. `--chain-length` includes the root.
`serial` links one child per parent; `wide` attaches all other steps directly to the root.
All children run on success. `--workflows` bounds admitted roots, `--queue-capacity` bounds
waiting attempts, and `--chain-parallelism` bounds live step tasks per chain. A bounded channel
backs up producers while admission is full. `runtime --help` lists every option, including
the per-sample `--timeout-seconds` deadline (120 seconds by default).

The console reports completed roots/s, steps/s, allocated bytes/step, and sampled managed
memory peak. JSON also records elapsed/CPU seconds, total allocated bytes, GC generation
counts, managed bytes before/after, and sampled process working-set peak. Allocation uses
`GC.GetTotalAllocatedBytes(true)` across all threads, including scheduler workers. The 10 ms
memory sampler and fixture counters contribute a small amount of measured overhead. Peaks
are sampled absolute process values, may miss brief spikes, and can include memory committed
during warmup. Completed in-memory history remains retained until the sample ends; allocation
and retained memory are different metrics.

This harness covers local in-memory execution. It excludes database/network latency, cron
scans, retries, startup/certificate validation, dashboard transport, and external telemetry.
Notification/logging sinks are disabled, and an isolated license-state fixture enables the
runtime. Use provider-specific load tests for EF, MongoDB, Redis, and multi-node claims.

Existing BenchmarkDotNet microbenchmarks remain available, for example:

```sh
dotnet benchmarks/TickerQ.Benchmarks/bin/Release/net10.0/TickerQ.Benchmarks.dll --filter '*DelegateInvocationComparison*'
```

The old `ConcurrentThroughputComparison` measures a TickerQ dictionary lookup/delegate call
against different enqueue/storage operations. Its ratios are not framework throughput or
end-to-end runtime comparisons. Use the complete runtime harness above for TickerQ changes.
