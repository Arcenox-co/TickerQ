using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using StackExchange.Redis;
using TickerQ.Caching.StackExchangeRedis.Helpers;
using TickerQ.Utilities.Entities;

namespace TickerQ.Caching.StackExchangeRedis.Tests.Infrastructure;

public sealed class LazyTimeTickerChildrenSerializationTests
{
    [Fact]
    public void Source_generated_redis_serialization_preserves_empty_and_explicit_null_children()
    {
        var serializer = CreateSerializer();
        var defaultTicker = new TimeTickerEntity();
        using var defaultJson = JsonDocument.Parse(serializer.Serialize(defaultTicker));
        Assert.Equal(JsonValueKind.Array, defaultJson.RootElement.GetProperty("Children").ValueKind);
        Assert.Equal(0, defaultJson.RootElement.GetProperty("Children").GetArrayLength());
        var empty = serializer.DeserializeOrNull<TimeTickerEntity>(defaultJson.RootElement.GetRawText());
        Assert.NotNull(empty);
        Assert.Empty(empty.Children);
        empty.Children.Add(new TimeTickerEntity());
        Assert.Single(empty.Children);

        var nullTicker = new TimeTickerEntity { Children = null! };
        using var nullJson = JsonDocument.Parse(serializer.Serialize(nullTicker));
        Assert.Equal(JsonValueKind.Null, nullJson.RootElement.GetProperty("Children").ValueKind);
        Assert.Null(serializer.DeserializeOrNull<TimeTickerEntity>(nullJson.RootElement.GetRawText()).Children);
        Assert.DoesNotContain("_children", defaultJson.RootElement.GetRawText(), StringComparison.Ordinal);
    }

    [Fact]
    public void Source_generated_redis_serialization_round_trips_independent_nested_collections()
    {
        var serializer = CreateSerializer();
        var root = new TimeTickerEntity();
        var child = new TimeTickerEntity();
        var grandchild = new TimeTickerEntity();
        child.Children.Add(grandchild);
        root.Children.Add(child);

        var restored = serializer.DeserializeOrNull<TimeTickerEntity>(serializer.Serialize(root));

        Assert.Equal(root.Id, restored.Id);
        var restoredChild = Assert.Single(restored.Children);
        var restoredGrandchild = Assert.Single(restoredChild.Children);
        Assert.Equal(child.Id, restoredChild.Id);
        Assert.Equal(grandchild.Id, restoredGrandchild.Id);
        Assert.Empty(restoredGrandchild.Children);
        Assert.NotSame(root.Children, restored.Children);
        Assert.NotSame(restored.Children, restoredChild.Children);
        restoredChild.Children.Clear();
        Assert.Single(child.Children);
        Assert.Single(restored.Children);
    }

    private static RedisSerializer CreateSerializer() => new(
        Substitute.For<IDatabase>(),
        new JsonSerializerOptions { TypeInfoResolver = RedisContextJsonSerializerContext.Default },
        NullLogger.Instance);
}
