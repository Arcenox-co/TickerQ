using Microsoft.Extensions.DependencyInjection;
using TickerQ.Provider;
using TickerQ.Utilities;
using TickerQ.Utilities.Entities;

namespace TickerQ.Tests;

public sealed class InMemoryBatchInsertionTests : IDisposable
{
    // Dedicated generic types isolate the provider's static state from other test classes.
    public sealed class Job : TimeTickerEntity<Job> { }
    public sealed class Cron : CronTickerEntity { }

    private readonly ServiceProvider _services;
    private readonly TickerInMemoryPersistenceProvider<Job, Cron> _provider;

    public InMemoryBatchInsertionTests()
    {
        _services = new ServiceCollection()
            .AddSingleton(new SchedulerOptionsBuilder())
            .BuildServiceProvider();
        _provider = new TickerInMemoryPersistenceProvider<Job, Cron>(_services);
        TickerInMemoryPersistenceProvider<Job, Cron>.ResetAllStateForTests();
    }

    [Fact]
    public async Task Independent_roots_keep_their_own_chain_identity_in_a_large_batch()
    {
        var roots = Enumerable.Range(0, 2_000)
            .Select(_ => new Job { Function = "batch", ChainGeneration = Guid.NewGuid() })
            .ToArray();
        var generations = roots.ToDictionary(root => root.Id, root => root.ChainGeneration);

        Assert.Equal(roots.Length, await _provider.AddTimeTickers(roots));
        Assert.Equal(roots.Length, _provider.TimeTickersForTests.Count);
        foreach (var root in roots)
        {
            var stored = await _provider.GetTimeTickerById(root.Id);
            Assert.NotNull(stored);
            Assert.Equal(root.Id, stored.ChainRootId);
            Assert.Equal(generations[root.Id], stored.ChainGeneration);
            Assert.Null(stored.ParentId);
        }
    }

    [Fact]
    public async Task Mixed_flat_and_nested_children_resolve_a_supplied_parent_regardless_of_order()
    {
        var generation = Guid.NewGuid();
        var root = new Job { Function = "root", ChainGeneration = generation };
        var nested = new Job { Function = "nested" };
        var child = new Job { Function = "child", ParentId = root.Id, Children = [nested] };
        var grandchild = new Job { Function = "grandchild", ParentId = child.Id };
        var unrelated = new Job { Function = "independent", ChainGeneration = Guid.NewGuid() };

        Assert.Equal(5, await _provider.AddTimeTickers([grandchild, unrelated, child, root]));

        var storedRoot = await _provider.GetTimeTickerById(root.Id);
        Assert.NotNull(storedRoot);
        var storedChild = Assert.Single(storedRoot.Children);
        Assert.Equal(child.Id, storedChild.Id);
        Assert.Equal(2, storedChild.Children.Count);
        Assert.Contains(storedChild.Children, node => node.Id == grandchild.Id);
        Assert.Contains(storedChild.Children, node => node.Id == nested.Id);
        foreach (var id in new[] { root.Id, child.Id, grandchild.Id, nested.Id })
        {
            var stored = await _provider.GetTimeTickerById(id);
            Assert.Equal(root.Id, stored.ChainRootId);
            Assert.Equal(generation, stored.ChainGeneration);
        }

        var storedUnrelated = await _provider.GetTimeTickerById(unrelated.Id);
        Assert.Equal(unrelated.Id, storedUnrelated.ChainRootId);
        Assert.Equal(unrelated.ChainGeneration, storedUnrelated.ChainGeneration);
    }

    [Fact]
    public async Task Children_appended_to_a_persisted_descendant_inherit_its_root_and_generation()
    {
        var generation = Guid.NewGuid();
        var persistedChild = new Job { Function = "persisted-child" };
        var root = new Job
        {
            Function = "persisted-root",
            ChainGeneration = generation,
            Children = [persistedChild]
        };
        Assert.Equal(2, await _provider.AddTimeTickers([root]));
        var appended = new Job { Function = "appended", ParentId = persistedChild.Id };
        var leaf = new Job { Function = "leaf", ParentId = appended.Id };
        var independent = new Job { Function = "independent" };

        Assert.Equal(3, await _provider.AddTimeTickers([leaf, independent, appended]));

        foreach (var id in new[] { appended.Id, leaf.Id })
        {
            var stored = await _provider.GetTimeTickerById(id);
            Assert.Equal(root.Id, stored.ChainRootId);
            Assert.Equal(generation, stored.ChainGeneration);
        }

        var storedRoot = await _provider.GetTimeTickerById(root.Id);
        var storedChild = Assert.Single(storedRoot.Children);
        var storedAppended = Assert.Single(storedChild.Children);
        Assert.Equal(appended.Id, storedAppended.Id);
        Assert.Equal(leaf.Id, Assert.Single(storedAppended.Children).Id);
        Assert.Equal(independent.Id,
            (await _provider.GetTimeTickerById(independent.Id)).ChainRootId);
    }

    [Fact]
    public void Duplicate_supplied_ids_are_rejected_before_any_root_is_inserted()
    {
        var first = new Job { Function = "first" };
        var duplicate = new Job { Id = first.Id, Function = "duplicate" };
        var unrelated = new Job { Function = "independent" };

        Assert.Throws<ArgumentException>(() =>
        {
            _ = _provider.AddTimeTickers([unrelated, first, duplicate]);
        });

        Assert.Empty(_provider.TimeTickersForTests);
        Assert.Null(unrelated.ChainRootId);
    }

    [Fact]
    public async Task Empty_batch_does_not_change_the_store()
    {
        Assert.Equal(0, await _provider.AddTimeTickers([]));
        Assert.Empty(_provider.TimeTickersForTests);
    }

    public void Dispose()
    {
        TickerInMemoryPersistenceProvider<Job, Cron>.ResetAllStateForTests();
        _services.Dispose();
    }
}
