using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using TickerQ.MongoDB.Infrastructure;
using TickerQ.MongoDB.Serialization;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Models;

namespace TickerQ.MongoDB.Tests;

// Container-free coverage of the chain-lease terminal write: a root that retains its chain lease
// keeps LeaseUntil on its terminal write; every other terminal write still clears it.
public sealed class MongoChainLeaseUpdateBuilderTests
{
    private static readonly DateTime Now = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime LeaseUntil = Now.AddMinutes(5);

    [Fact]
    public void Root_terminal_write_that_retains_the_chain_lease_keeps_the_next_lease()
    {
        var set = RenderSet(Terminal(parentId: null, retainsChainLease: true));

        Assert.Equal(LeaseUntil, set["LeaseUntil"].ToUniversalTime());
        Assert.True(set["AcquisitionToken"].IsBsonNull);
    }

    [Fact]
    public void Root_terminal_write_without_a_chain_lease_clears_the_lease()
        => Assert.True(RenderSet(Terminal(parentId: null, retainsChainLease: false))["LeaseUntil"].IsBsonNull);

    [Fact]
    public void Lock_release_clears_the_lease_even_when_the_chain_lease_is_retained()
    {
        var context = Terminal(parentId: null, retainsChainLease: true)
            .SetProperty(x => x.ReleaseLock, true);

        var set = RenderSet(context);

        Assert.True(set["LeaseUntil"].IsBsonNull);
        Assert.True(set["ChainGeneration"].IsBsonNull);
    }

    [Fact]
    public void Child_terminal_write_never_keeps_a_lease()
        => Assert.True(RenderSet(Terminal(parentId: Guid.NewGuid(), retainsChainLease: true))["LeaseUntil"].IsBsonNull);

    private static InternalFunctionContext Terminal(Guid? parentId, bool retainsChainLease)
    {
        var context = new InternalFunctionContext
        {
            TickerId = Guid.NewGuid(),
            ParentId = parentId,
            RetainsChainLease = retainsChainLease
        };
        return context.SetProperty(x => x.Status, TickerStatus.Done);
    }

    private static BsonDocument RenderSet(InternalFunctionContext context)
    {
        TickerClassMaps.RegisterOnce<TestTimeTicker, TestCronTicker>();
        var update = MongoUpdateBuilders.BuildTimeTickerUpdate<TestTimeTicker>(context, Now, LeaseUntil);
        var rendered = update.Render(new RenderArgs<TestTimeTicker>(
            BsonSerializer.LookupSerializer<TestTimeTicker>(), BsonSerializer.SerializerRegistry));
        return rendered.AsBsonDocument["$set"].AsBsonDocument;
    }

    private sealed class TestTimeTicker : TimeTickerEntity<TestTimeTicker>;
    private sealed class TestCronTicker : CronTickerEntity;
}
