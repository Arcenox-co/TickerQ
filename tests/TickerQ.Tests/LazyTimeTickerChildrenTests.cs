using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using TickerQ.Provider;
using TickerQ.Utilities;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Models;

namespace TickerQ.Tests;

public sealed class LazyTimeTickerChildrenTests
{
    private static readonly FieldInfo ChildrenField = typeof(TimeTickerEntity<TimeTickerEntity>)
        .GetField("_children", BindingFlags.NonPublic | BindingFlags.Instance)!;

    [Fact]
    public void Default_children_are_lazy_but_public_collection_is_owned_mutable_and_stable()
    {
        var first = new TimeTickerEntity();
        var second = new TimeTickerEntity();
        Assert.Null(ChildrenField.GetValue(first));
        Assert.Empty(first.GetChildrenForRead());
        Assert.Null(ChildrenField.GetValue(first));

        var child = new TimeTickerEntity();
        var collection = first.Children;
        collection.Add(child);

        Assert.Same(collection, first.Children);
        Assert.Same(child, Assert.Single(first.Children));
        Assert.NotSame(collection, second.Children);
        Assert.Empty(second.Children);
    }

    [Fact]
    public void Explicit_collection_and_null_assignments_preserve_the_original_contract()
    {
        var ticker = new TimeTickerEntity();
        ICollection<TimeTickerEntity> assigned = new HashSet<TimeTickerEntity>();
        ticker.Children = assigned;
        Assert.Same(assigned, ticker.Children);
        Assert.Same(assigned, ticker.GetChildrenForRead());

        ticker.Children = Array.Empty<TimeTickerEntity>();
        Assert.Same(Array.Empty<TimeTickerEntity>(), ticker.Children);
        Assert.True(ticker.Children.IsReadOnly);

        ticker.Children = null!;
        Assert.Null(ticker.Children);
        Assert.Null(ticker.GetChildrenForRead());
        ticker.Children = assigned;
        Assert.Same(assigned, ticker.Children);
    }

    [Fact]
    public void Concurrent_first_reads_publish_one_collection()
    {
        var ticker = new TimeTickerEntity();
        var references = new ICollection<TimeTickerEntity>[32];
        Parallel.For(0, references.Length, i => references[i] = ticker.Children);
        Assert.All(references, collection => Assert.Same(ticker.Children, collection));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Concurrent_initialization_does_not_overwrite_an_explicit_assignment(bool assignNull)
    {
        for (var iteration = 0; iteration < 500; iteration++)
        {
            var ticker = new TimeTickerEntity();
            ICollection<TimeTickerEntity>? assigned = assignNull ? null : new HashSet<TimeTickerEntity>();
            Parallel.Invoke(() => { _ = ticker.Children; }, () => ticker.Children = assigned!);
            Assert.Same(assigned, ticker.Children);
            Assert.Same(assigned, ticker.GetChildrenForRead());
        }
    }

    [Fact]
    public void Query_snapshot_owns_a_new_lazy_navigation_without_mutating_the_original()
    {
        var child = new TimeTickerEntity();
        var original = new TimeTickerEntity { Children = [child] };
        var snapshot = original.CreateQuerySnapshot();

        Assert.Null(ChildrenField.GetValue(snapshot));
        Assert.Empty(snapshot.GetChildrenForRead());
        snapshot.Children.Add(new TimeTickerEntity());
        Assert.NotSame(original.Children, snapshot.Children);
        Assert.Same(child, Assert.Single(original.Children));
    }

    [Fact]
    public void Custom_navigation_overrides_are_used_for_traversal_and_snapshot_reset()
    {
        var original = new CustomJob { Label = "custom", Children = [new CustomJob()] };
        Assert.Same(original.Children, original.GetChildrenForRead());
        var assignments = original.Assignments;
        var snapshot = original.CreateQuerySnapshot();

        Assert.Equal("custom", snapshot.Label);
        Assert.Equal(assignments + 1, snapshot.Assignments);
        Assert.Equal(assignments, original.Assignments);
        Assert.Empty(snapshot.Children);
        Assert.Single(original.Children);
        snapshot.Children.Add(new CustomJob());
        Assert.NotSame(original.Children, snapshot.Children);
    }

    [Fact]
    public void Json_preserves_empty_null_and_nested_mutable_collections()
    {
        var standaloneJson = JsonSerializer.Serialize(new TimeTickerEntity());
        using var standalone = JsonDocument.Parse(standaloneJson);
        Assert.Equal(JsonValueKind.Array, standalone.RootElement.GetProperty("Children").ValueKind);
        Assert.Equal(0, standalone.RootElement.GetProperty("Children").GetArrayLength());
        var nullJson = JsonSerializer.Serialize(new TimeTickerEntity { Children = null! });
        Assert.Null(JsonSerializer.Deserialize<TimeTickerEntity>(nullJson)!.Children);

        var parent = new TimeTickerEntity { Children = [new TimeTickerEntity()] };
        var copy = JsonSerializer.Deserialize<TimeTickerEntity>(JsonSerializer.Serialize(parent))!;
        var child = Assert.Single(copy.Children);
        child.Children.Add(new TimeTickerEntity());
        Assert.Empty(parent.Children.Single().Children);
        Assert.Single(child.Children);
        Assert.NotSame(copy.Children, child.Children);
    }

    [Fact]
    public async Task Standalone_store_rows_and_execution_updates_do_not_allocate_leaf_collections()
    {
        // Unique cron type isolates the generic provider's static partition state.
        using var services = new ServiceCollection().AddSingleton(new SchedulerOptionsBuilder()).BuildServiceProvider();
        var provider = new TickerInMemoryPersistenceProvider<TimeTickerEntity, TestCron>(services);
        TickerInMemoryPersistenceProvider<TimeTickerEntity, TestCron>.ResetAllStateForTests();
        try
        {
            var rows = Enumerable.Range(0, 100).Select(_ => new TimeTickerEntity
            {
                Function = "lazy-leaf", ExecutionTime = DateTime.UtcNow.AddMinutes(1)
            }).ToArray();
            Assert.Equal(rows.Length, await provider.AddTimeTickers(rows));
            Assert.All(provider.TimeTickersForTests.Values, row => Assert.Null(ChildrenField.GetValue(row)));

            var snapshot = await provider.GetTimeTickerById(rows[0].Id);
            Assert.Null(ChildrenField.GetValue(snapshot));
            var projected = await provider.GetEarliestTimeTickers();
            Assert.All(projected, row => Assert.Null(ChildrenField.GetValue(row)));

            var acquired = await provider.AcquireTimeTickerOnDemandAsync(rows[0].Id, DateTime.UtcNow);
            Assert.NotNull(acquired);
            Assert.Null(ChildrenField.GetValue(provider.TimeTickersForTests[rows[0].Id]));
            var completed = new InternalFunctionContext()
                .SetProperty(x => x.TickerId, rows[0].Id)
                .SetProperty(x => x.Type, TickerType.TimeTicker)
                .SetProperty(x => x.ChainRootId, rows[0].Id)
                .SetProperty(x => x.ChainGeneration, acquired.ChainGeneration)
                .SetProperty(x => x.AcquisitionToken, acquired.AcquisitionToken)
                .SetProperty(x => x.Status, TickerStatus.Done)
                .SetProperty(x => x.ExecutedAt, DateTime.UtcNow)
                .SetProperty(x => x.ReleaseLock, true);
            Assert.Equal(1, await provider.UpdateTimeTicker(completed));
            var stored = provider.TimeTickersForTests[rows[0].Id];
            Assert.Equal(TickerStatus.Done, stored.Status);
            Assert.Null(ChildrenField.GetValue(stored));
            Assert.Empty(stored.Children);
            stored.Children.Add(new TimeTickerEntity());
            Assert.Single(stored.Children);
        }
        finally
        {
            TickerInMemoryPersistenceProvider<TimeTickerEntity, TestCron>.ResetAllStateForTests();
        }
    }

    public sealed class TestCron : CronTickerEntity { }

    private sealed class CustomJob : TimeTickerEntity<CustomJob>
    {
        private ICollection<CustomJob> _customChildren = new List<CustomJob>();
        public string Label { get; set; } = "";
        public int Assignments { get; private set; }
        public override ICollection<CustomJob> Children
        {
            get => _customChildren;
            set { Assignments++; _customChildren = value; }
        }
    }
}
