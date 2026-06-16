using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using TickerQ.Utilities;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Instrumentation;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Interfaces.Managers;
using TickerQ.Utilities.Interfaces.Managers;
using TickerQ.Utilities.Models;
using Xunit;

namespace TickerQ.Tests;

[Collection("TickerCancellationTokenState")]
public class PeriodicChainHandlerTests : IDisposable
{
    public void Dispose() => TickerCancellationTokenManager.CleanUpTickerCancellationTokens();

    private readonly ITickerClock _clock;
    private readonly IInternalTickerManager _internalManager;
    private readonly ITickerQInstrumentation _instrumentation;
    private readonly TickerExecutionTaskHandler _handler;

    public PeriodicChainHandlerTests()
    {
        _clock = Substitute.For<ITickerClock>();
        _clock.UtcNow.Returns(DateTime.UtcNow);
        _internalManager = Substitute.For<IInternalTickerManager>();
        _instrumentation = Substitute.For<ITickerQInstrumentation>();

        var services = new ServiceCollection();
        services.AddSingleton(_internalManager);
        services.AddSingleton(_instrumentation);
        var sp = services.BuildServiceProvider();

        _handler = new TickerExecutionTaskHandler(sp, _clock, _instrumentation, _internalManager);
    }

    private static InternalFunctionContext PeriodicContext(bool withTemplate, Action onDelegate)
    {
        return new InternalFunctionContext
        {
            TickerId = Guid.NewGuid(),
            ParentId = Guid.NewGuid(),
            FunctionName = "RootFn",
            Type = TickerType.PeriodicTickerOccurrence,
            ExecutionTime = DateTime.UtcNow,
            RetryIntervals = [],
            Status = TickerStatus.Idle,
            CachedDelegate = (_, _, _) => { onDelegate(); return Task.CompletedTask; },
            TimeTickerChildren = [],
            PeriodicChainTemplate = withTemplate
                ? new[] { new PeriodicChainStep { Function = "B", RunCondition = RunCondition.OnSuccess } }
                : null
        };
    }

    [Fact]
    public async Task PeriodicOccurrence_WithTemplate_MaterializesChain_AndDoesNotRunOwnFunction()
    {
        _internalManager
            .MaterializePeriodicChainAsync(Arg.Any<InternalFunctionContext>(), Arg.Any<CancellationToken>())
            .Returns(true);

        var ran = false;
        var ctx = PeriodicContext(withTemplate: true, onDelegate: () => ran = true);

        await _handler.ExecuteTaskAsync(ctx, isDue: true);

        Assert.False(ran); // occurrence does NOT run its own function; the chain root does
        await _internalManager.Received(1).MaterializePeriodicChainAsync(
            Arg.Is<InternalFunctionContext>(c => c.TickerId == ctx.TickerId), Arg.Any<CancellationToken>());
        Assert.Equal(TickerStatus.DueDone, ctx.Status);
        await _internalManager.Received(1).UpdateTickerAsync(
            Arg.Is<InternalFunctionContext>(c => c.TickerId == ctx.TickerId), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PeriodicOccurrence_WithoutTemplate_RunsOwnFunction_Regression()
    {
        var ran = false;
        var ctx = PeriodicContext(withTemplate: false, onDelegate: () => ran = true);

        await _handler.ExecuteTaskAsync(ctx, isDue: false);

        Assert.True(ran); // legacy behavior preserved
        await _internalManager.DidNotReceive().MaterializePeriodicChainAsync(
            Arg.Any<InternalFunctionContext>(), Arg.Any<CancellationToken>());
        Assert.Equal(TickerStatus.Done, ctx.Status);
    }

    [Fact]
    public async Task PeriodicOccurrence_OverlapSkip_MarksSkipped_WhenMaterializationSkipped()
    {
        _internalManager
            .MaterializePeriodicChainAsync(Arg.Any<InternalFunctionContext>(), Arg.Any<CancellationToken>())
            .Returns(false); // overlap suppression

        var ran = false;
        var ctx = PeriodicContext(withTemplate: true, onDelegate: () => ran = true);

        await _handler.ExecuteTaskAsync(ctx, isDue: false);

        Assert.False(ran);
        Assert.Equal(TickerStatus.Skipped, ctx.Status);
        await _internalManager.Received(1).UpdateTickerAsync(
            Arg.Is<InternalFunctionContext>(c => c.TickerId == ctx.TickerId), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PeriodicOccurrence_MaterializationThrows_MarksFailed_ScheduleNotStalled()
    {
        _internalManager
            .MaterializePeriodicChainAsync(Arg.Any<InternalFunctionContext>(), Arg.Any<CancellationToken>())
            .Returns<bool>(_ => throw new InvalidOperationException("boom"));

        var ctx = PeriodicContext(withTemplate: true, onDelegate: () => { });

        await _handler.ExecuteTaskAsync(ctx, isDue: false);

        Assert.Equal(TickerStatus.Failed, ctx.Status);
        await _internalManager.Received(1).UpdateTickerAsync(
            Arg.Is<InternalFunctionContext>(c => c.TickerId == ctx.TickerId), Arg.Any<CancellationToken>());
    }
}

