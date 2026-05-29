using System;
using System.Linq;
using TickerQ.Utilities;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Managers;
using Xunit;

namespace TickerQ.Tests;

public class FluentChainTickerBuilderTests : IDisposable
{
    private readonly int _origMaxChildren = TickerChainConfig.MaxChildrenPerNode;
    private readonly int _origMaxDepth = TickerChainConfig.MaxDepth;

    public void Dispose()
    {
        TickerChainConfig.MaxChildrenPerNode = _origMaxChildren;
        TickerChainConfig.MaxDepth = _origMaxDepth;
    }

    // Backward compatibility: the direct-chaining pattern (Children.Add) keeps working unchanged.
    [Fact]
    public void DirectChaining_ViaChildren_BuildsParentWithConditionalChildren()
    {
        var parent = new TimeTickerEntity
        {
            Function = "process-order",
            ExecutionTime = DateTime.UtcNow
        };
        parent.Children.Add(new TimeTickerEntity
        {
            Function = "send-confirmation",
            ParentId = parent.Id,
            RunCondition = RunCondition.OnSuccess
        });
        parent.Children.Add(new TimeTickerEntity
        {
            Function = "alert-ops",
            ParentId = parent.Id,
            RunCondition = RunCondition.OnFailure
        });

        Assert.Equal(2, parent.Children.Count);
        Assert.Equal("send-confirmation", parent.Children.ElementAt(0).Function);
        Assert.Equal(RunCondition.OnSuccess, parent.Children.ElementAt(0).RunCondition);
        Assert.Equal(RunCondition.OnFailure, parent.Children.ElementAt(1).RunCondition);
        Assert.All(parent.Children, c => Assert.Equal(parent.Id, c.ParentId));
    }

    // Backward compatibility: the legacy ordinal fluent API is unchanged.
    [Fact]
    public void LegacyFluent_BeginWith_WithFirstAndSecondChild_StillWorks()
    {
        var chain = FluentChainTickerBuilder<TimeTickerEntity>
            .BeginWith(p => p.SetFunction("process-order").SetExecutionTime(DateTime.UtcNow))
            .WithFirstChild(c => c.SetFunction("send-confirmation").SetRunCondition(RunCondition.OnSuccess))
            .WithSecondChild(c => c.SetFunction("alert-ops").SetRunCondition(RunCondition.OnFailure))
            .Build();

        Assert.Equal("process-order", chain.Function);
        Assert.Equal(2, chain.Children.Count);
        Assert.Contains(chain.Children, c => c.Function == "send-confirmation" && c.RunCondition == RunCondition.OnSuccess);
        Assert.Contains(chain.Children, c => c.Function == "alert-ops" && c.RunCondition == RunCondition.OnFailure);
    }

    // Backward compatibility: child + grandchild (2 levels) via legacy ordinal API.
    [Fact]
    public void LegacyFluent_ChildAndGrandChild_StillWorks()
    {
        var chain = FluentChainTickerBuilder<TimeTickerEntity>
            .BeginWith(p => p.SetFunction("process-order"))
            .WithFirstChild(c => c.SetFunction("charge").SetRunCondition(RunCondition.OnSuccess))
            .WithFirstGrandChild(gc => gc.SetFunction("ship").SetRunCondition(RunCondition.OnSuccess))
            .Build();

        var charge = chain.Children.Single();
        Assert.Equal("charge", charge.Function);
        var ship = charge.Children.Single();
        Assert.Equal("ship", ship.Function);
    }

    // New recursive API: arbitrary depth bounded by config, consistent SetFunction/SetRunCondition vocabulary.
    [Fact]
    public void RecursiveWithChild_BuildsSpine_AfterRaisingMaxDepth()
    {
        TickerChainConfig.MaxDepth = 4;

        var chain = FluentChainTickerBuilder<TimeTickerEntity>
            .BeginWith(p => p.SetFunction("root"))
            .WithChild(c => c.SetFunction("A"),
                a => a.WithChild(c => c.SetFunction("B"),
                    b => b.WithChild(c => c.SetFunction("C"),
                        cc => cc.WithChild(d => d.SetFunction("D")))))
            .Build();

        var a = chain.Children.Single();
        Assert.Equal("A", a.Function);
        Assert.Equal(chain.Id, a.ParentId);

        var b = a.Children.Single();
        Assert.Equal("B", b.Function);
        Assert.Equal(a.Id, b.ParentId);

        var c = b.Children.Single();
        Assert.Equal("C", c.Function);
        Assert.Equal(b.Id, c.ParentId);

        var d = c.Children.Single();
        Assert.Equal("D", d.Function);
        Assert.Equal(c.Id, d.ParentId);
        Assert.Empty(d.Children);
    }

    [Fact]
    public void RecursiveWithChild_ParallelSiblings_AtRoot()
    {
        var chain = FluentChainTickerBuilder<TimeTickerEntity>
            .BeginWith(p => p.SetFunction("root"))
            .WithChild(c => c.SetFunction("A").SetRunCondition(RunCondition.OnSuccess))
            .WithChild(c => c.SetFunction("B").SetRunCondition(RunCondition.OnFailure))
            .Build();

        Assert.Equal(2, chain.Children.Count);
        Assert.All(chain.Children, c => Assert.Equal(chain.Id, c.ParentId));
    }

    [Fact]
    public void RecursiveWithChild_ExceedingDefaultDepth_Throws()
    {
        // Default MaxDepth = 2; third level must throw.
        var builder = FluentChainTickerBuilder<TimeTickerEntity>.BeginWith(p => p.SetFunction("root"));

        Assert.Throws<InvalidOperationException>(() =>
            builder.WithChild(c => c.SetFunction("A"),
                a => a.WithChild(c => c.SetFunction("B"),
                    b => b.WithChild(c => c.SetFunction("C")))));
    }
}

