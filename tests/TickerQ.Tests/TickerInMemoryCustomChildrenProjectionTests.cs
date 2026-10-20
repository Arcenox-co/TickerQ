using Microsoft.Extensions.DependencyInjection;
using TickerQ.Provider;
using TickerQ.Utilities;
using TickerQ.Utilities.Entities;

namespace TickerQ.Tests;

public sealed class TickerInMemoryCustomChildrenProjectionTests
{
    [Fact]
    public async Task Root_projection_assigns_completed_children_through_custom_virtual_setter()
    {
        using var services = new ServiceCollection()
            .AddSingleton(new SchedulerOptionsBuilder()).BuildServiceProvider();
        var provider = new TickerInMemoryPersistenceProvider<WrappedChildrenJob, TestCron>(services);
        TickerInMemoryPersistenceProvider<WrappedChildrenJob, TestCron>.ResetAllStateForTests();
        try
        {
            var child = new WrappedChildrenJob { Function = "custom-child" };
            var root = new WrappedChildrenJob
            {
                Function = "custom-root",
                ExecutionTime = DateTime.UtcNow.AddMinutes(1),
                Children = [child]
            };
            Assert.Equal(2, await provider.AddTimeTickers([root]));

            var projected = await provider.GetTimeTickerById(root.Id);

            Assert.NotNull(projected);
            var projectedChild = Assert.Single(projected.Children);
            Assert.Equal(child.Id, projectedChild.Id);
            Assert.Equal(root.Id, projectedChild.ParentId);
            Assert.True(projected.Children.IsReadOnly);
            Assert.Equal(new[] { 0, 1 }, projected.AssignedChildCounts);
            Assert.NotSame(root, projected);
            Assert.NotSame(child, projectedChild);
            Assert.Same(child, Assert.Single(root.Children));
            Assert.Equal(new[] { 1 }, root.AssignedChildCounts);

            var projectedLeaf = await provider.GetTimeTickerById(child.Id);
            Assert.NotNull(projectedLeaf);
            Assert.Empty(projectedLeaf.Children);
            Assert.Equal(new[] { 0, 0 }, projectedLeaf.AssignedChildCounts);
        }
        finally
        {
            TickerInMemoryPersistenceProvider<WrappedChildrenJob, TestCron>.ResetAllStateForTests();
        }
    }

    public sealed class TestCron : CronTickerEntity { }

    public sealed class WrappedChildrenJob : TimeTickerEntity<WrappedChildrenJob>
    {
        private List<WrappedChildrenJob> _ownedChildren = [];
        public List<int> AssignedChildCounts { get; } = [];

        public override ICollection<WrappedChildrenJob> Children
        {
            get => _ownedChildren.AsReadOnly();
            set
            {
                _ownedChildren = value.ToList();
                AssignedChildCounts.Add(_ownedChildren.Count);
            }
        }
    }
}
