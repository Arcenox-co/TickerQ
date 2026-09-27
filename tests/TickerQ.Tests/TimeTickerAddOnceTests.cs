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
        _manager = new TickerManager<TestTimeTicker, TestCronTicker>(
            _provider,
            Substitute.For<ITickerQHostScheduler>(),
            clock,
            Substitute.For<ITickerQNotificationHubSender>(),
            new TickerExecutionContext(),
            Substitute.For<ITickerQDispatcher>());
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
}
