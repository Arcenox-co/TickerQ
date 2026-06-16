using System;
using System.Linq;
using TickerQ.Utilities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Managers;
using Xunit;

namespace TickerQ.Tests;

public class PeriodicChainBuilderTests : IDisposable
{
    private readonly int _origMaxChildren = TickerChainConfig.MaxChildrenPerNode;
    private readonly int _origMaxDepth = TickerChainConfig.MaxDepth;

    public void Dispose()
    {
        // Restore global chain limits so static mutation does not leak between tests.
        TickerChainConfig.MaxChildrenPerNode = _origMaxChildren;
        TickerChainConfig.MaxDepth = _origMaxDepth;
    }

    [Fact]
    public void WithChild_SingleStep_ProducesSingleRootNoChildren()
    {
        var template = PeriodicChainBuilder.Create()
            .WithChild(c => c.SetFunction("A"))
            .Build();

        Assert.Single(template);
        Assert.Equal("A", template[0].Function);
        Assert.Empty(template[0].Children);
        Assert.Equal(RunCondition.OnSuccess, template[0].RunCondition);
    }

    [Fact]
    public void WithChild_ParallelSiblings_AtSameLevel()
    {
        var template = PeriodicChainBuilder.Create()
            .WithChild(c => c.SetFunction("A").SetRunCondition(RunCondition.OnSuccess))
            .WithChild(c => c.SetFunction("B").SetRunCondition(RunCondition.OnFailure))
            .Build();

        Assert.Equal(2, template.Length);
        Assert.Equal(new[] { "A", "B" }, template.Select(s => s.Function).ToArray());
        Assert.Equal(RunCondition.OnSuccess, template[0].RunCondition);
        Assert.Equal(RunCondition.OnFailure, template[1].RunCondition);
        Assert.All(template, s => Assert.Empty(s.Children));
    }

    [Fact]
    public void WithChild_SequentialSpine_ViaDescendants_WithinDefaultDepth()
    {
        // Default MaxDepth = 2 -> root child (level 1) + grandchild (level 2)
        var template = PeriodicChainBuilder.Create()
            .WithChild(c => c.SetFunction("ScheduledCalculations").SetRunCondition(RunCondition.OnSuccess),
                sub => sub.WithChild(g => g.SetFunction("PublishReport").SetRunCondition(RunCondition.OnSuccess)))
            .Build();

        Assert.Single(template);
        Assert.Equal("ScheduledCalculations", template[0].Function);
        Assert.Single(template[0].Children);
        Assert.Equal("PublishReport", template[0].Children[0].Function);
        Assert.Empty(template[0].Children[0].Children);
    }

    [Fact]
    public void WithChild_ExceedingDefaultDepth_Throws()
    {
        // Default MaxDepth = 2; nesting a third level (A->B->C) must throw.
        var builder = PeriodicChainBuilder.Create();

        Assert.Throws<InvalidOperationException>(() =>
            builder.WithChild(c => c.SetFunction("A"),
                a => a.WithChild(c => c.SetFunction("B"),
                    b => b.WithChild(c => c.SetFunction("C")))));
    }

    [Fact]
    public void WithChild_DeepSpine_AllowedAfterRaisingMaxDepth()
    {
        TickerChainConfig.MaxDepth = 6;

        var template = PeriodicChainBuilder.Create()
            .WithChild(c => c.SetFunction("A"),
                a => a.WithChild(c => c.SetFunction("B"),
                    b => b.WithChild(c => c.SetFunction("C"),
                        cc => cc.WithChild(d => d.SetFunction("D"),
                            dd => dd.WithChild(e => e.SetFunction("E"),
                                ee => ee.WithChild(f => f.SetFunction("F")))))))
            .Build();

        // Walk the spine and assert order/linkage to depth 6.
        var node = template[0];
        foreach (var fn in new[] { "A", "B", "C", "D", "E" })
        {
            Assert.Equal(fn, node.Function);
            Assert.Single(node.Children);
            node = node.Children[0];
        }
        Assert.Equal("F", node.Function);
        Assert.Empty(node.Children);
    }

    [Fact]
    public void WithChild_ExceedingMaxChildrenPerNode_Throws()
    {
        TickerChainConfig.MaxChildrenPerNode = 2;

        var builder = PeriodicChainBuilder.Create()
            .WithChild(c => c.SetFunction("A"))
            .WithChild(c => c.SetFunction("B"));

        Assert.Throws<InvalidOperationException>(() =>
            builder.WithChild(c => c.SetFunction("C")));
    }

    [Fact]
    public void WithChild_CarriesRetriesAndRequest()
    {
        var template = PeriodicChainBuilder.Create()
            .WithChild(c => c.SetFunction("A").SetRetries(3, 5, 10).SetRequest(new byte[] { 1, 2, 3 }))
            .Build();

        Assert.Equal(3, template[0].Retries);
        Assert.Equal(new[] { 5, 10 }, template[0].RetryIntervals);
        Assert.Equal(new byte[] { 1, 2, 3 }, template[0].Request);
    }
}

