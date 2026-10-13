using Xunit;

// These tests exercise TickerFunctionProvider's process-global startup registry.
// Serial execution prevents unrelated fixtures from registering different delegates
// under the same test function name at the same time.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
