using System.Diagnostics;
using System.Linq.Expressions;
using NSubstitute;
using TickerQ.Utilities;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Infrastructure;
using TickerQ.Utilities.Interfaces.Managers;
using TickerQ.Utilities.Managers;

namespace TickerQ.Tests;

public partial class TickerManagerTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AddBatchAsync_LogsCreatedTrees_ReadingBackOnlyWhenNotEveryNodeWasConfirmed(bool allInserted)
    {
        var store = new InMemoryTickerExecutionLogStore();
        var manager = BatchManagerWithLifecycle(store);
        var child = new TimeTickerEntity { Id = Guid.NewGuid(), Function = ValidFunctionName };
        var first = new TimeTickerEntity
        {
            Id = Guid.NewGuid(), Function = ValidFunctionName, ExecutionTime = FixedUtcNow.AddMinutes(5), Children = [child]
        };
        var second = new TimeTickerEntity
        {
            Id = Guid.NewGuid(), Function = ValidFunctionName, ExecutionTime = FixedUtcNow.AddMinutes(5)
        };
        // Three nodes confirmed is the whole batch; one is a partial insert the provider has to be asked about.
        _persistenceProvider.AddTimeTickers(Arg.Any<TimeTickerEntity[]>(), Arg.Any<CancellationToken>())
            .Returns(allInserted ? 3 : 1);
        _persistenceProvider.GetTimeTickers(Arg.Any<Expression<Func<TimeTickerEntity, bool>>>(), Arg.Any<CancellationToken>())
            .Returns([second]);

        var result = await manager.AddBatchAsync([first, second]);

        Assert.True(result.IsSucceeded);
        await _persistenceProvider.Received(allInserted ? 0 : 1)
            .GetTimeTickers(Arg.Any<Expression<Func<TimeTickerEntity, bool>>>(), Arg.Any<CancellationToken>());
        Assert.Equal(TickerLifecycleEvent.Created, Assert.Single(store.GetTail(second.Id)).LifecycleEvent);
        Assert.Equal(allInserted ? 1 : 0, store.GetTail(first.Id).Count);
        Assert.Equal(allInserted ? 1 : 0, store.GetTail(child.Id).Count);
    }

    [Fact]
    public async Task AddBatchAsync_LargeConfirmedBatch_DoesNotReadEveryRowBack()
    {
        // The read-back predicate is one equality per id; evaluated per stored row it was quadratic,
        // and 20,000 jobs took seconds inside AddBatchAsync before the scheduler was woken.
        var manager = BatchManagerWithLifecycle(new InMemoryTickerExecutionLogStore());
        var tickers = Enumerable.Range(0, 20_000)
            .Select(_ => new TimeTickerEntity { Function = ValidFunctionName, ExecutionTime = FixedUtcNow.AddMinutes(5) })
            .ToList();
        _persistenceProvider.AddTimeTickers(Arg.Any<TimeTickerEntity[]>(), Arg.Any<CancellationToken>())
            .Returns(call => call.ArgAt<TimeTickerEntity[]>(0).Length);

        var elapsed = Stopwatch.StartNew();
        var result = await manager.AddBatchAsync(tickers);
        elapsed.Stop();

        Assert.True(result.IsSucceeded);
        await _persistenceProvider.DidNotReceive()
            .GetTimeTickers(Arg.Any<Expression<Func<TimeTickerEntity, bool>>>(), Arg.Any<CancellationToken>());
        Assert.True(elapsed.Elapsed < TimeSpan.FromSeconds(5), $"AddBatchAsync took {elapsed.Elapsed}");
    }

    private ITimeTickerManager<TimeTickerEntity> BatchManagerWithLifecycle(InMemoryTickerExecutionLogStore store)
    {
        var options = new SchedulerOptionsBuilder();
        options.BindRuntimeActivationScope(null, 1, schedulerEnabled: false);
        return new TickerManager<TimeTickerEntity, CronTickerEntity>(
            _persistenceProvider, _hostScheduler, _clock, _notificationHubSender, _executionContext, _dispatcher,
            options, new TickerQActivationGate(), lifecycle: new TickerLifecycleLogger(store, new(), _clock));
    }
}
