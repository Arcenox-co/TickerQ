using BenchmarkDotNet.Running;
using TickerQ.Benchmarks;

BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
