using TickerQ.Tests.Shared;
using MongoDB.Driver;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Interfaces;

namespace TickerQ.MongoDB.Tests;

[Collection("Mongo")]
public sealed class ChainDeleteBehaviorTests(MongoTestFixture fixture) : ChainDeleteBehaviorContract<TimeTickerEntity, CronTickerEntity>, IAsyncLifetime
{
    protected override ITickerPersistenceProvider<TimeTickerEntity, CronTickerEntity> Provider => fixture.Provider;
    protected override DateTime Now => fixture.FixedNow;
    public Task InitializeAsync() => fixture.DropAllAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    protected override async Task SetDescendantStatusAsync(Guid rootId, Guid id, TickerStatus status)
    {
        // Simulate the worker's terminal transition without using a generic unfenced lifecycle update.
        var result = await fixture.TimeTickers.UpdateOneAsync(x => x.Id == id && x.ChainRootId == rootId,
            Builders<TimeTickerEntity>.Update.Set(x => x.Status, status));
        Assert.Equal(1, result.ModifiedCount);
    }

    [Fact]
    public async Task Legacy_document_without_policy_still_cascades_during_retention()
    {
        var root = Chain(ChainDeleteBehavior.Cascade);
        await Provider.AddTimeTickers([root]);
        await fixture.TimeTickers.UpdateOneAsync(x => x.Id == root.Id, Builders<TimeTickerEntity>.Update.Unset(x => x.DeleteBehavior));
        Assert.Equal(ChainDeleteBehavior.Cascade, (await Provider.GetTimeTickerById(root.Id)).DeleteBehavior);
        var batch = await Provider.DeleteEligibleTimeTickerChainsAsync(new(Now.AddDays(-7), null, null, null), 10, default);
        Assert.Equal(3, batch.Deleted);
    }

    [Fact]
    public async Task Restrict_blocks_replacement_until_descendants_finish_then_retention_resumes()
    {
        var root = Chain(ChainDeleteBehavior.Restrict);
        var replacement = Node();
        Set(Assert.Single(Assert.Single(root.Children).Children), nameof(root.Status), TickerStatus.Idle);
        await Provider.AddTimeTickers([root]);
        Assert.Equal(0, await Provider.ReplaceTimeTickerChainAsync(root.Id, replacement));
        Assert.Null(await Provider.GetTimeTickerById(replacement.Id));
        var finished = await Provider.GetTimeTickerById(root.Id);
        await SetDescendantStatusAsync(root.Id, Assert.Single(Assert.Single(finished.Children).Children).Id, TickerStatus.Done);
        Assert.Equal(ChainDeleteBehavior.Restrict, (await Provider.GetTimeTickerById(root.Id)).DeleteBehavior);
        Assert.Equal(3, (await Provider.DeleteEligibleTimeTickerChainsAsync(new(Now.AddDays(-7), null, null, null), 10, default)).Deleted);
    }

    [Fact]
    public async Task Restrict_allows_replacement_when_all_descendants_are_terminal()
    {
        var root = Chain(ChainDeleteBehavior.Restrict);
        var replacement = Node();
        await Provider.AddTimeTickers([root]);
        Assert.True(await Provider.ReplaceTimeTickerChainAsync(root.Id, replacement) > 0);
        Assert.Null(await Provider.GetTimeTickerById(root.Id));
        Assert.NotNull(await Provider.GetTimeTickerById(replacement.Id));
    }
}
