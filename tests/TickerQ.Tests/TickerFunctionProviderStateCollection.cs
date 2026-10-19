namespace TickerQ.Tests;

// Function registration and the cron timezone are also touched by DI/startup tests
// outside this collection. Keep tests that inspect/reset these globals isolated.
[CollectionDefinition("TickerFunctionProviderState", DisableParallelization = true)]
public sealed class TickerFunctionProviderStateCollection;
