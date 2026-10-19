namespace TickerQ.Tests;

// The cron timezone is process-wide, including for manager calls and AddTickerQ.
// Tests that change it must not overlap tests in any other collection.
[CollectionDefinition("CronScheduleCacheState", DisableParallelization = true)]
public sealed class CronScheduleCacheStateCollection;
