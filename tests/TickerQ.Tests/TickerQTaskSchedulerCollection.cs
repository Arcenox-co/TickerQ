namespace TickerQ.Tests;

// These tests use real workers, bounded queues, and timing assertions. Other
// collections deliberately block threads to exercise persistence races, which can
// starve scheduler startup on small CI runners. Isolate the scheduler tests from
// that unrelated load; the producers and workers inside each test still race.
[CollectionDefinition("TickerQTaskScheduler", DisableParallelization = true)]
public sealed class TickerQTaskSchedulerCollection;
