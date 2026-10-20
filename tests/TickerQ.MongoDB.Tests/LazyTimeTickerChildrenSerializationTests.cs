using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using TickerQ.MongoDB.Serialization;
using TickerQ.Utilities.Entities;

namespace TickerQ.MongoDB.Tests;

// Provider class maps are process-wide. Register outside concurrent fixture startup.
[CollectionDefinition("Lazy children BSON serialization", DisableParallelization = true)]
public sealed class LazyChildrenBsonCollection;

[Collection("Lazy children BSON serialization")]
public sealed class LazyTimeTickerChildrenSerializationTests
{
    [Fact]
    public void Provider_BSON_mapping_keeps_children_and_lazy_backing_state_unmapped()
    {
        TickerClassMaps.RegisterOnce<TimeTickerEntity, CronTickerEntity>();
        var root = new TimeTickerEntity { Function = "lazy-children" };
        root.Children.Add(new TimeTickerEntity { Function = "nested" });

        var document = root.ToBsonDocument();

        Assert.False(document.Contains(nameof(TimeTickerEntity.Children)));
        Assert.False(document.Contains(nameof(TimeTickerEntity.Parent)));
        Assert.DoesNotContain(document.Names, name => name.Contains("_children", StringComparison.Ordinal));
        var restored = BsonSerializer.Deserialize<TimeTickerEntity>(document);
        Assert.Equal(root.Id, restored.Id);
        Assert.Equal(root.Function, restored.Function);
        Assert.Empty(restored.Children);
        Assert.NotSame(root.Children, restored.Children);
        restored.Children.Add(new TimeTickerEntity { Function = "new-child" });
        Assert.Single(root.Children);
        Assert.Single(restored.Children);
        Assert.NotEqual(root.Children.Single().Id, restored.Children.Single().Id);
    }
}
