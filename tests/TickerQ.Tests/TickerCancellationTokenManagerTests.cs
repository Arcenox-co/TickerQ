using System;
using System.Threading;
using TickerQ.Utilities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Models;
using Xunit;

namespace TickerQ.Tests;

public class TickerCancellationTokenManagerTests : IDisposable
{
    public void Dispose()
    {
        TickerCancellationTokenManager.CleanUpTickerCancellationTokens();
    }

    [Fact]
    public void RequestCancellationById_Cancels_The_Token()
    {
        var cts = new CancellationTokenSource();
        var tickerId = Guid.NewGuid();
        var context = MakeContext(tickerId);

        TickerCancellationTokenManager.AddTickerCancellationToken(cts, context, isDue: false);

        var token = cts.Token;
        Assert.False(token.IsCancellationRequested);

        var result = TickerCancellationTokenManager.RequestTickerCancellationById(tickerId);

        Assert.True(result);
        Assert.True(token.IsCancellationRequested);
    }

    [Fact]
    public void RequestCancellationById_Returns_False_For_Unknown_Id()
    {
        var result = TickerCancellationTokenManager.RequestTickerCancellationById(Guid.NewGuid());

        Assert.False(result);
    }

    [Fact]
    public void IsParentRunning_Returns_True_When_Ticker_Registered()
    {
        var cts = new CancellationTokenSource();
        var parentId = Guid.NewGuid();
        var tickerId = Guid.NewGuid();
        var context = MakeContext(tickerId, parentId);

        TickerCancellationTokenManager.AddTickerCancellationToken(cts, context, isDue: false);

        Assert.True(TickerCancellationTokenManager.IsParentRunning(parentId));
    }

    [Fact]
    public void IsParentRunning_Returns_False_After_Removal()
    {
        var cts = new CancellationTokenSource();
        var parentId = Guid.NewGuid();
        var tickerId = Guid.NewGuid();
        var context = MakeContext(tickerId, parentId);

        TickerCancellationTokenManager.AddTickerCancellationToken(cts, context, isDue: false);
        TickerCancellationTokenManager.RemoveTickerCancellationToken(tickerId);

        Assert.False(TickerCancellationTokenManager.IsParentRunning(parentId));
    }

    [Fact]
    public void IsParentRunningExcludingSelf_Returns_False_When_Only_Self()
    {
        var cts = new CancellationTokenSource();
        var parentId = Guid.NewGuid();
        var tickerId = Guid.NewGuid();
        var context = MakeContext(tickerId, parentId);

        TickerCancellationTokenManager.AddTickerCancellationToken(cts, context, isDue: false);

        Assert.False(TickerCancellationTokenManager.IsParentRunningExcludingSelf(parentId, tickerId));
    }

    [Fact]
    public void IsParentRunningExcludingSelf_Returns_True_When_Sibling_Exists()
    {
        var parentId = Guid.NewGuid();
        var ticker1 = Guid.NewGuid();
        var ticker2 = Guid.NewGuid();

        TickerCancellationTokenManager.AddTickerCancellationToken(
            new CancellationTokenSource(), MakeContext(ticker1, parentId), isDue: false);
        TickerCancellationTokenManager.AddTickerCancellationToken(
            new CancellationTokenSource(), MakeContext(ticker2, parentId), isDue: false);

        Assert.True(TickerCancellationTokenManager.IsParentRunningExcludingSelf(parentId, ticker1));
    }

    [Fact]
    public void CancelById_Keeps_Entry_Tracked_During_Cancellation()
    {
        // Verifies cancel-before-remove: IsParentRunning should be true
        // at the moment Cancel fires on the CTS
        var parentId = Guid.NewGuid();
        var tickerId = Guid.NewGuid();
        var cts = new CancellationTokenSource();
        var context = MakeContext(tickerId, parentId);

        bool wasTrackedDuringCancel = false;
        cts.Token.Register(() =>
        {
            wasTrackedDuringCancel = TickerCancellationTokenManager.IsParentRunning(parentId);
        });

        TickerCancellationTokenManager.AddTickerCancellationToken(cts, context, isDue: false);

        TickerCancellationTokenManager.RequestTickerCancellationById(tickerId);

        Assert.True(wasTrackedDuringCancel, "the entry should still be in the dictionary when Cancel fires");
    }

    private static InternalFunctionContext MakeContext(Guid tickerId, Guid? parentId = null)
    {
        return new InternalFunctionContext
        {
            TickerId = tickerId,
            ParentId = parentId,
            FunctionName = "Test",
            Type = TickerType.CronTickerOccurrence
        };
    }
}
