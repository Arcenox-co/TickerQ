using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Infrastructure;
using TickerQ.Utilities.Models;

namespace TickerQ.Tests;

public class TimeTickerContextBuilderTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Build_LeafClearsFactoryChildren_AndKeepsListsIndependent(bool nullChildren)
    {
        var root = new TimeTickerEntity();
        if (nullChildren)
            root.Children = null!;
        var context = new InternalFunctionContext();
        var ownedChildren = context.TimeTickerChildren;
        ownedChildren.Add(new InternalFunctionContext());

        var built = TimeTickerContextBuilder.Build(root, _ => context);
        var second = TimeTickerContextBuilder.Build(root, _ => new InternalFunctionContext());

        Assert.Same(context, built);
        Assert.Same(ownedChildren, built.TimeTickerChildren);
        Assert.Empty(built.TimeTickerChildren);
        Assert.NotSame(built.TimeTickerChildren, second.TimeTickerChildren);

        built.TimeTickerChildren.Add(new InternalFunctionContext());

        Assert.Empty(second.TimeTickerChildren);
        if (!nullChildren)
            Assert.Empty(root.Children);
    }

    [Fact]
    public void Build_PreservesSiblingOrderAndFactoryMetadata_AcrossBranches()
    {
        var root = new TimeTickerEntity { AcquisitionToken = Guid.NewGuid() };
        root.ChainRootId = root.Id;
        root.ChainGeneration = root.AcquisitionToken;
        var first = Child(root, root);
        var second = Child(root, root);
        var grandchild = Child(second, root);
        first.Children = null!;
        root.Children = [first, second];
        second.Children.Add(grandchild);
        var created = new Dictionary<Guid, (InternalFunctionContext Context, List<InternalFunctionContext> Children)>();

        var built = TimeTickerContextBuilder.Build(root, entity =>
        {
            var context = new InternalFunctionContext
            {
                TickerId = entity.Id,
                ParentId = entity.ParentId,
                ChainRootId = entity.ChainRootId,
                ChainGeneration = entity.ChainGeneration,
                AcquisitionToken = entity.AcquisitionToken
            };
            created.Add(entity.Id, (context, context.TimeTickerChildren));
            return context;
        });

        Assert.Equal(4, created.Count);
        Assert.Equal(new[] { first.Id, second.Id }, built.TimeTickerChildren.Select(c => c.TickerId));
        Assert.Empty(built.TimeTickerChildren[0].TimeTickerChildren);
        Assert.Same(created[grandchild.Id].Context, Assert.Single(built.TimeTickerChildren[1].TimeTickerChildren));
        foreach (var entity in new[] { root, first, second, grandchild })
        {
            var (context, children) = created[entity.Id];
            Assert.Same(children, context.TimeTickerChildren);
            Assert.Equal(entity.ParentId, context.ParentId);
            Assert.Equal(root.Id, context.ChainRootId);
            Assert.Equal(root.ChainGeneration, context.ChainGeneration);
            Assert.Equal(entity.AcquisitionToken, context.AcquisitionToken);
        }
        Assert.Equal(new[] { first.Id, second.Id }, root.Children.Select(c => c.Id));
        Assert.Same(grandchild, Assert.Single(second.Children));
    }

    [Fact]
    public void Build_DeepChainRemainsIterative_AndAcceptsNullFactoryChildLists()
    {
        const int depth = 20_000;
        var root = new TimeTickerEntity();
        var leaf = root;
        for (var i = 1; i < depth; i++)
        {
            var child = new TimeTickerEntity { ParentId = leaf.Id };
            leaf.Children.Add(child);
            leaf = child;
        }

        var calls = 0;
        var context = TimeTickerContextBuilder.Build(root, entity =>
        {
            calls++;
            return new InternalFunctionContext
            {
                TickerId = entity.Id,
                ParentId = entity.ParentId,
                TimeTickerChildren = null!
            };
        });

        Assert.Equal(depth, calls);
        Assert.Equal(root.Id, context.TickerId);
        for (var i = 1; i < depth; i++)
        {
            var child = Assert.Single(context.TimeTickerChildren);
            Assert.Equal(context.TickerId, child.ParentId);
            context = child;
        }
        Assert.Equal(leaf.Id, context.TickerId);
        Assert.Empty(context.TimeTickerChildren);
    }

    private static TimeTickerEntity Child(TimeTickerEntity parent, TimeTickerEntity root) => new()
    {
        ParentId = parent.Id,
        ChainRootId = root.Id,
        ChainGeneration = root.ChainGeneration
    };
}
