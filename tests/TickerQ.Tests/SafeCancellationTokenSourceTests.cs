using System;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace TickerQ.Tests;

public class SafeCancellationTokenSourceTests
{
    [Fact]
    public void Dispose_Is_Idempotent()
    {
        var cts = new SafeCancellationTokenSource();

        cts.Dispose();
        cts.Dispose(); // second dispose should not throw

        Assert.True(cts.IsDisposed);
    }

    [Fact]
    public void Cancel_After_Dispose_Does_Not_Throw()
    {
        var cts = new SafeCancellationTokenSource();
        cts.Dispose();

        cts.Cancel(); // should not throw
    }

    [Fact]
    public void CancelAfter_TimeSpan_After_Dispose_Does_Not_Throw()
    {
        var cts = new SafeCancellationTokenSource();
        cts.Dispose();

        cts.CancelAfter(TimeSpan.FromSeconds(1)); // should not throw
    }

    [Fact]
    public void CancelAfter_Int_After_Dispose_Does_Not_Throw()
    {
        var cts = new SafeCancellationTokenSource();
        cts.Dispose();

        cts.CancelAfter(1000); // should not throw
    }

    [Fact]
    public void Cancel_Sets_Token_To_Cancelled()
    {
        var cts = new SafeCancellationTokenSource();
        var token = cts.Token;

        cts.Cancel();

        Assert.True(cts.IsCancellationRequested);
        Assert.True(token.IsCancellationRequested);
    }

    [Fact]
    public void CreateLinked_Cancels_When_Parent_Cancels()
    {
        using var parent = new CancellationTokenSource();
        var linked = SafeCancellationTokenSource.CreateLinked(parent.Token);

        parent.Cancel();

        Assert.True(linked.IsCancellationRequested);
        linked.Dispose();
    }

    [Fact]
    public async Task Concurrent_Cancel_And_Dispose_Does_Not_Throw()
    {
        // Run many iterations to exercise the race window
        for (int i = 0; i < 1000; i++)
        {
            var cts = new SafeCancellationTokenSource();

            var t1 = Task.Run(() => cts.Cancel());
            var t2 = Task.Run(() => cts.Dispose());

            await Task.WhenAll(t1, t2);

            Assert.True(cts.IsDisposed);
        }
    }
}
