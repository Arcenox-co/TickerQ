using BenchmarkDotNet.Running;
using TickerQ.Benchmarks;

if (args.Length > 0 && args[0] == "runtime")
    Environment.ExitCode = await RuntimeExecutionBenchmark.RunAsync(args[1..]);
else
    BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
