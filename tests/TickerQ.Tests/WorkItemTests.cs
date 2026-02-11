using FluentAssertions;
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

        item.Work.Should().BeSameAs(work);
        item.UserToken.Should().Be(cts.Token);
    }

    [Fact]
    public void Constructor_ThrowsArgumentNullException_WhenWorkIsNull()
    {
        var act = () => new WorkItem(null!, CancellationToken.None);

        act.Should().Throw<ArgumentNullException>()
            .And.ParamName.Should().Be("work");
    }

    [Fact]
    public void Constructor_AcceptsCancellationTokenNone()
    {
        var item = new WorkItem(_ => Task.CompletedTask, CancellationToken.None);

        item.UserToken.Should().Be(CancellationToken.None);
    }

    [Fact]
    public void Constructor_AcceptsCancelledToken()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var item = new WorkItem(_ => Task.CompletedTask, cts.Token);

        item.UserToken.IsCancellationRequested.Should().BeTrue();
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

        executed.Should().BeTrue();
    }
}
