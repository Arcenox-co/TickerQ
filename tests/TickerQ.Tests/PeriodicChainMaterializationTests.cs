using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Interfaces.Managers;
using TickerQ.Utilities.Managers;
using TickerQ.Utilities.Models;
using Xunit;

namespace TickerQ.Tests;

public class PeriodicChainMaterializationTests
{
    private static readonly DateTime Now = new(2026, 5, 29, 10, 0, 0, DateTimeKind.Utc);

    private readonly ITickerPersistenceProvider<TimeTickerEntity, CronTickerEntity> _persistence;
    private readonly IPeriodicTickerPersistenceProvider<PeriodicTickerEntity> _periodic;
    private readonly ITickerClock _clock;
    private readonly ITimeTickerManager<TimeTickerEntity> _timeManager;
    private readonly InternalTickerManagerWithPeriodic<TimeTickerEntity, CronTickerEntity, PeriodicTickerEntity> _manager;

    private TimeTickerEntity _capturedRoot;

    public PeriodicChainMaterializationTests()
    {
        _persistence = Substitute.For<ITickerPersistenceProvider<TimeTickerEntity, CronTickerEntity>>();
        _periodic = Substitute.For<IPeriodicTickerPersistenceProvider<PeriodicTickerEntity>>();
        _clock = Substitute.For<ITickerClock>();
        _clock.UtcNow.Returns(Now);

        _timeManager = Substitute.For<ITimeTickerManager<TimeTickerEntity>>();
        _timeManager
            .AddAsync(Arg.Do<TimeTickerEntity>(t => _capturedRoot = t), Arg.Any<CancellationToken>())
            .Returns(ci => new TickerResult<TimeTickerEntity>(ci.Arg<TimeTickerEntity>()));

        var services = new ServiceCollection();
        services.AddSingleton(_timeManager);
        var sp = services.BuildServiceProvider();

        _manager = new InternalTickerManagerWithPeriodic<TimeTickerEntity, CronTickerEntity, PeriodicTickerEntity>(
            _persistence, _periodic, _clock, null, sp);
    }

    private static InternalFunctionContext PeriodicContext(PeriodicChainStep[] template, byte[] rootRequest = null,
        ChainOverlapBehavior overlap = ChainOverlapBehavior.Allow)
    {
        return new InternalFunctionContext
        {
            ParentId = Guid.NewGuid(),
            TickerId = Guid.NewGuid(),
            FunctionName = "RootFn",
            Type = TickerType.PeriodicTickerOccurrence,
            Retries = 2,
            RetryIntervals = new[] { 5 },
            RootRequest = rootRequest,
            PeriodicChainTemplate = template,
            ChainOverlapBehavior = overlap
        };
    }

    [Fact]
    public async Task Materialize_RootCarriesPeriodicWork_AndExecutesImmediately()
    {
        var request = new byte[] { 9 };
        var template = PeriodicChainBuilder.Create().WithChild(c => c.SetFunction("B")).Build();

        var created = await _manager.MaterializePeriodicChainAsync(PeriodicContext(template, request));

        Assert.True(created);
        Assert.NotNull(_capturedRoot);
        Assert.Equal("RootFn", _capturedRoot.Function);
        Assert.Equal(request, _capturedRoot.Request);
        Assert.Equal(2, _capturedRoot.Retries);
        Assert.Equal(Now, _capturedRoot.ExecutionTime);
        Assert.NotEqual(Guid.Empty, _capturedRoot.Id);
    }

    [Fact]
    public async Task Materialize_BuildsSpine_WithParentLinkage_AndNoChildExecutionTime()
    {
        var template = PeriodicChainBuilder.Create()
            .WithChild(c => c.SetFunction("B").SetRunCondition(RunCondition.OnSuccess),
                b => b.WithChild(c => c.SetFunction("C").SetRunCondition(RunCondition.OnSuccess)))
            .Build();

        await _manager.MaterializePeriodicChainAsync(PeriodicContext(template));

        // Root -> B -> C
        var root = _capturedRoot;
        Assert.Single(root.Children);

        var b = root.Children.Single();
        Assert.Equal("B", b.Function);
        Assert.Equal(root.Id, b.ParentId);
        Assert.Null(b.ExecutionTime); // children fire by RunCondition, not by time
        Assert.Equal(RunCondition.OnSuccess, b.RunCondition);

        var c = b.Children.Single();
        Assert.Equal("C", c.Function);
        Assert.Equal(b.Id, c.ParentId);
        Assert.Null(c.ExecutionTime);
        Assert.Empty(c.Children);
    }

    [Fact]
    public async Task Materialize_AssignsUniqueIds_AcrossChain()
    {
        var template = PeriodicChainBuilder.Create()
            .WithChild(c => c.SetFunction("B"), b => b.WithChild(c => c.SetFunction("C")))
            .Build();

        await _manager.MaterializePeriodicChainAsync(PeriodicContext(template));

        var root = _capturedRoot;
        var b = root.Children.Single();
        var c = b.Children.Single();

        var ids = new[] { root.Id, b.Id, c.Id };
        Assert.Equal(3, ids.Distinct().Count());
    }

    [Fact]
    public async Task Materialize_NullTemplate_ReturnsFalse_AndDoesNotEnqueue()
    {
        var created = await _manager.MaterializePeriodicChainAsync(PeriodicContext(template: null));

        Assert.False(created);
        await _timeManager.DidNotReceive().AddAsync(Arg.Any<TimeTickerEntity>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Materialize_OverlapSkip_SkipsWhenPriorChainStillRunning()
    {
        var template = PeriodicChainBuilder.Create().WithChild(c => c.SetFunction("B")).Build();
        var ctx = PeriodicContext(template, overlap: ChainOverlapBehavior.Skip);

        // First fire materializes and records the chain ids.
        var first = await _manager.MaterializePeriodicChainAsync(ctx);
        Assert.True(first);

        // Simulate the prior chain still running (any non-terminal status).
        _persistence
            .GetTimeTickers(Arg.Any<System.Linq.Expressions.Expression<Func<TimeTickerEntity, bool>>>(), Arg.Any<CancellationToken>())
            .Returns(new[] { new TimeTickerEntity { Id = _capturedRoot.Id } }); // default Status == Idle (non-terminal)

        var second = await _manager.MaterializePeriodicChainAsync(ctx);

        Assert.False(second);
    }

    [Fact]
    public async Task Materialize_OverlapSkip_ProceedsWhenPriorChainCompleted()
    {
        var template = PeriodicChainBuilder.Create().WithChild(c => c.SetFunction("B")).Build();
        var ctx = PeriodicContext(template, overlap: ChainOverlapBehavior.Skip);

        await _manager.MaterializePeriodicChainAsync(ctx);

        // Prior chain fully terminal -> not running.
        var done = new TimeTickerEntity { Id = _capturedRoot.Id };
        done.Status = TickerStatus.Done;
        _persistence
            .GetTimeTickers(Arg.Any<System.Linq.Expressions.Expression<Func<TimeTickerEntity, bool>>>(), Arg.Any<CancellationToken>())
            .Returns(new[] { done });

        var second = await _manager.MaterializePeriodicChainAsync(ctx);

        Assert.True(second);
    }
}

