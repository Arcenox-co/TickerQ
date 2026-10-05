using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using TickerQ.Provider;
using TickerQ.Utilities;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Interfaces.Managers;
using TickerQ.Utilities.Managers;
using TickerQ.Utilities.Models;

namespace TickerQ.Tests;

[Collection("TickerFunctionProviderState")]
public sealed class TimeTickerAddOnceTests : IDisposable
{
    public sealed class TestTimeTicker : TimeTickerEntity<TestTimeTicker> { }
    public sealed class TestCronTicker : CronTickerEntity { }

    private readonly TickerInMemoryPersistenceProvider<TestTimeTicker, TestCronTicker> _provider;
    private readonly ITimeTickerManager<TestTimeTicker> _manager;

    public TimeTickerAddOnceTests()
    {
        TickerFunctionProvider.RegisterFunctions(
            new Dictionary<string, (string, TickerTaskPriority, TickerFunctionDelegate, int)>
            {
                ["Welcome"] = ("", TickerTaskPriority.Normal, (_, _, _) => Task.CompletedTask, 0),
                ["Other"] = ("", TickerTaskPriority.Normal, (_, _, _) => Task.CompletedTask, 0)
            });
        TickerFunctionProvider.Build();

        var clock = Substitute.For<ITickerClock>();
        clock.UtcNow.Returns(new DateTime(2026, 7, 30, 12, 0, 0, DateTimeKind.Utc));
        var services = new ServiceCollection().AddSingleton(clock).BuildServiceProvider();
        _provider = new TickerInMemoryPersistenceProvider<TestTimeTicker, TestCronTicker>(services);
        var schedulerOptions = new SchedulerOptionsBuilder();
        schedulerOptions.BindRuntimeActivationScope(null, 1, schedulerEnabled: false);
        _manager = new TickerManager<TestTimeTicker, TestCronTicker>(
            _provider,
            Substitute.For<ITickerQHostScheduler>(),
            clock,
            Substitute.For<ITickerQNotificationHubSender>(),
            new TickerExecutionContext(),
            Substitute.For<ITickerQDispatcher>(),
            schedulerOptions,
            new TickerQActivationGate());
    }

    public void Dispose() => TickerFunctionProvider.Build();

    [Fact]
    public async Task AddOnce_ConcurrentCalls_ConvergeOnDeterministicId_AndPreserveWinner()
    {
        const string key = "startup:send-welcome-email";
        var calls = Enumerable.Range(0, 20).Select(i =>
            _manager.AddOnceAsync(key, new TestTimeTicker
            {
                Function = "Welcome",
                Description = $"candidate-{i}",
                ExecutionTime = new DateTime(2026, 7, 31, 12, 0, 0, DateTimeKind.Utc),
                Request = []
            }));

        var results = await Task.WhenAll(calls);

        Assert.All(results, result => Assert.True(result.IsSucceeded));
        var deterministicId = TimeTickerInitIdentity.DeterministicId(key);
        Assert.All(results, result => Assert.Equal(deterministicId, result.Result.Id));
        var persisted = await _provider.GetTimeTickers(x => x.InitIdentifier == key, CancellationToken.None);
        var winner = Assert.Single(persisted);
        var winnerDescription = winner.Description;

        var repeated = await _manager.AddOnceAsync(key, new TestTimeTicker
        {
            Function = "Other",
            Description = "must-not-overwrite",
            ExecutionTime = new DateTime(2026, 8, 1, 12, 0, 0, DateTimeKind.Utc)
        });

        Assert.True(repeated.IsSucceeded);
        Assert.Equal(winnerDescription, repeated.Result.Description);
        Assert.Equal(deterministicId, repeated.Result.Id);
    }

    [Fact]
    public async Task AddOnce_OrdinaryPreStartCallerCannotHoldGateAgainstAuthorizedSeeder()
    {
        var scope = new ReconciliationActivationScope("add-once-startup");
        const long epoch = 7;
        var gate = new TickerQActivationGate();
        var options = new SchedulerOptionsBuilder();
        options.BindRuntimeActivationScope("add-once-startup", epoch, schedulerEnabled: true);
        var clock = Substitute.For<ITickerClock>();
        clock.UtcNow.Returns(new DateTime(2026, 7, 30, 12, 0, 0, DateTimeKind.Utc));
        var manager = (ITimeTickerManager<TestTimeTicker>)new TickerManager<TestTimeTicker, TestCronTicker>(
            _provider,
            Substitute.For<ITickerQHostScheduler>(),
            clock,
            Substitute.For<ITickerQNotificationHubSender>(),
            new TickerExecutionContext(),
            Substitute.For<ITickerQDispatcher>(),
            options,
            gate);

        var ordinary = manager.AddOnceAsync("ordinary", new TestTimeTicker
        {
            Function = "Welcome", ExecutionTime = clock.UtcNow.AddMinutes(1), Request = []
        });
        await Task.Yield();
        Assert.False(ordinary.IsCompleted);

        TickerResult<TestTimeTicker> seeded;
        using (StartupSeederAdmissionContext.Enter(scope.ScopeKey, epoch))
            seeded = await manager.AddOnceAsync("seeder", new TestTimeTicker
            {
                Function = "Welcome", ExecutionTime = clock.UtcNow.AddMinutes(1), Request = []
            }).WaitAsync(TimeSpan.FromSeconds(2));

        Assert.True(seeded.IsSucceeded);
        Assert.False(ordinary.IsCompleted);
        gate.SignalActivated();
        Assert.True((await ordinary.WaitAsync(TimeSpan.FromSeconds(2))).IsSucceeded);
    }
}
