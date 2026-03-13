using TickerQ.TickerQThreadPool;

namespace TickerQ.Tests;

public class WorkItemTests
{
    [Fact]
    public void Constructor_SetsWorkAndToken()
    {
        using var cts = new CancellationTokenSource();
        Func<CancellationToken, Task> work = _ => Task.CompletedTask;

        var item = new WorkItem(work, cts.Token);

        Assert.Same(work, item.Work);
        Assert.Equal(cts.Token, item.UserToken);
    }

    [Fact]
    public void Constructor_ThrowsArgumentNullException_WhenWorkIsNull()
    {
        var ex = Assert.Throws<ArgumentNullException>(() => { _ = new WorkItem(null!, CancellationToken.None); });
        Assert.Equal("work", ex.ParamName);
    }

    [Fact]
    public void Constructor_AcceptsCancellationTokenNone()
    {
        var item = new WorkItem(_ => Task.CompletedTask, CancellationToken.None);

        Assert.Equal(CancellationToken.None, item.UserToken);
    }

    [Fact]
    public void Constructor_AcceptsCancelledToken()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var item = new WorkItem(_ => Task.CompletedTask, cts.Token);

        Assert.True(item.UserToken.IsCancellationRequested);
    }

    [Fact]
    public async Task Work_CanBeInvoked()
    {
        var executed = false;
        var item = new WorkItem(_ =>
        {
            executed = true;
            return Task.CompletedTask;
        }, CancellationToken.None);

        await item.Work(CancellationToken.None);

        Assert.True(executed);
    }
}
