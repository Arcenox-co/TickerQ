using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using TickerQ.Provider;
using TickerQ.Tests.Shared;
using TickerQ.Utilities;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Enums;

namespace TickerQ.Tests;

public sealed class CronChainTests : CronChainContract<CronChainTests.Job, CronChainTests.Cron>
{
    public sealed class Job : TimeTickerEntity<Job> { }
    public sealed class Cron : CronTickerEntity { }
    protected override ITickerPersistenceProvider<Job, Cron> Provider { get; }
    protected override DateTime Now => new(2026, 10, 17, 12, 0, 0, DateTimeKind.Utc);
    public CronChainTests()
    {
        var clock = Substitute.For<ITickerClock>();
        clock.UtcNow.Returns(Now);
        TickerInMemoryPersistenceProvider<Job, Cron>.ResetAllStateForTests();
        Provider = new TickerInMemoryPersistenceProvider<Job, Cron>(new ServiceCollection()
            .AddSingleton(clock).AddSingleton(new SchedulerOptionsBuilder { FallbackBatchSize = 100 }).BuildServiceProvider());
    }

}
