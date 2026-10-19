namespace TickerQ.Tests;

// These tests clear or count the process-wide cancellation registry. Fallback and
// function-provider tests in other collections can also register executions, so this
// collection must not overlap them. Concurrency within each test remains enabled.
[CollectionDefinition("TickerCancellationTokenState", DisableParallelization = true)]
public sealed class TickerCancellationTokenStateCollection;
