using NSubstitute;
using TickerQ.Utilities;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Managers;
using TickerQ.Utilities.Infrastructure;
using TickerQ.Utilities.Models;

namespace TickerQ.Tests;

public class CronChainValidationTests
{
    [Fact]
    public async Task Manager_checks_launch_receipt_even_after_template_removal()
    {
        var provider = Substitute.For<ITickerPersistenceProvider<TimeTickerEntity, CronTickerEntity>>();
        provider.SupportsCronChains.Returns(true);
        var context = new InternalFunctionContext { TickerId = Guid.NewGuid(), ParentId = Guid.NewGuid() };
        provider.MaterializeCronChainAsync(context, false, default).Returns(CronChainDispatchResult.AlreadyCreated);
        var manager = new InternalTickerManager<TimeTickerEntity, CronTickerEntity>(
            provider, Substitute.For<ITickerClock>(), null, new SchedulerOptionsBuilder());
        Assert.True(await manager.TryDispatchCronChainAsync(context, false));
    }

    [Fact]
    public void Rejects_cycles_shared_nodes_and_excessive_depth()
    {
        var node = new CronChainNode { Function = "Test" };
        var template = new CronChainTemplate { Children = [node] };
        node.Children.Add(node);
        Assert.Throws<ArgumentException>(() => CronChainMaterializer.ValidateShape(template));
        node.Children.Clear(); template.Children.Add(node);
        Assert.Throws<ArgumentException>(() => CronChainMaterializer.ValidateShape(template));
        template.Children.RemoveAt(1);
        for (var i = 0; i < 20; i++) { var child = new CronChainNode { Function = "Test" }; node.Children.Add(child); node = child; }
        Assert.Throws<ArgumentException>(() => CronChainMaterializer.ValidateShape(template));
    }

    [Fact]
    public void Node_limit_matches_dashboard_total_including_root()
    {
        var template = new CronChainTemplate { Children = Enumerable.Range(0, 499).Select(_ => new CronChainNode { Function = "Test" }).ToList() };
        CronChainMaterializer.ValidateShape(template);
        template.Children.Add(new CronChainNode { Function = "Test" });
        Assert.Throws<ArgumentException>(() => CronChainMaterializer.ValidateShape(template));
    }
}
