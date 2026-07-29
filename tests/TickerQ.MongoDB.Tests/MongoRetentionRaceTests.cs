using MongoDB.Driver;
using TickerQ.MongoDB.Infrastructure;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Models;

namespace TickerQ.MongoDB.Tests;

[Collection("Mongo")]
public sealed class MongoRetentionRaceTests : IAsyncLifetime
{
    private readonly MongoTestFixture _fixture;
    private TickerMongoPersistenceProvider<TimeTickerEntity, CronTickerEntity> Provider
        => _fixture.ConcreteProvider;

    public MongoRetentionRaceTests(MongoTestFixture fixture) => _fixture = fixture;

    public Task InitializeAsync() => _fixture.DropAllAsync();

    public Task DisposeAsync()
    {
        Provider.BeforeGraphMutationFenceForTestAsync = null;
        Provider.AfterGraphMutationFenceForTestAsync = null;
        Provider.BeforeRetentionFenceForTestAsync = null;
        Provider.AfterRetentionDiscoveryForTestAsync = null;
        return Task.CompletedTask;
    }

    private DateTime Ago(double days) => _fixture.FixedNow - TimeSpan.FromDays(days);

    private TimeTickerEntity Node(TickerStatus status, DateTime? executedAt, Guid? parentId = null)
    {
        var entity = new TimeTickerEntity
        {
            Id = Guid.NewGuid(),
            Function = "retention-race",
            Request = Array.Empty<byte>(),
            ExecutionTime = _fixture.FixedNow.AddHours(-1),
            Status = status,
            ExecutedAt = executedAt,
            ParentId = parentId,
            CreatedAt = Ago(40),
            UpdatedAt = Ago(40)
        };
        return entity;
    }

    private static RetentionCutoffs Cutoffs(DateTime succeeded)
        => new(succeeded, null, null, null);

    [Fact]
    public async Task RemoveTimeTickers_UsesGraphFenceAndPreservesRecursiveDelete()
    {
        var root = Node(TickerStatus.Done, Ago(10));
        var child = Node(TickerStatus.Done, Ago(10), root.Id);
        await _fixture.TimeTickers.InsertManyAsync([root, child]);

        var fenceAttempts = 0;
        Provider.BeforeGraphMutationFenceForTestAsync = _ =>
        {
            Interlocked.Increment(ref fenceAttempts);
            return Task.CompletedTask;
        };

        var deleted = await Provider.RemoveTimeTickers([root.Id], CancellationToken.None);

        Assert.Equal(2, deleted);
        Assert.True(fenceAttempts >= 1);
        Assert.Equal(0, await _fixture.TimeTickers.CountDocumentsAsync(
            FilterDefinition<TimeTickerEntity>.Empty));
    }

    [Fact]
    public async Task RetentionWins_PhantomInsertCannotAttachToDeletedAggregate()
    {
        var root = Node(TickerStatus.Done, Ago(10));
        var child = Node(TickerStatus.Done, Ago(10), root.Id);
        await _fixture.TimeTickers.InsertManyAsync([root, child]);

        var mutationReachedFence = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        Task<int>? mutation = null;
        Provider.BeforeGraphMutationFenceForTestAsync = _ =>
        {
            mutationReachedFence.TrySetResult();
            return Task.CompletedTask;
        };
        Provider.AfterRetentionDiscoveryForTestAsync = async (_, _) =>
        {
            var phantom = Node(TickerStatus.Done, Ago(1), child.Id);
            mutation = Provider.AddTimeTickers([phantom], CancellationToken.None);
            await mutationReachedFence.Task;
        };

        var retained = await Provider.DeleteEligibleTimeTickerChainsAsync(
            Cutoffs(Ago(7)), 10, RetentionCursor.Start, CancellationToken.None);
        var inserted = await mutation!;

        Assert.Equal(2, retained.Deleted);
        Assert.Equal(0, inserted);
        Assert.Equal(0, await _fixture.TimeTickers.CountDocumentsAsync(
            FilterDefinition<TimeTickerEntity>.Empty));
    }

    [Fact]
    public async Task ReparentWins_RetentionKeepsNewlyAdoptedChildAndOldAggregate()
    {
        var doomedRoot = Node(TickerStatus.Done, Ago(10));
        var child = Node(TickerStatus.Done, Ago(10), doomedRoot.Id);
        var liveRoot = Node(TickerStatus.Done, Ago(1));
        await _fixture.TimeTickers.InsertManyAsync([doomedRoot, child, liveRoot]);

        var mutationHasFence = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseMutation = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var retentionReachedFence = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        Provider.AfterGraphMutationFenceForTestAsync = async _ =>
        {
            mutationHasFence.TrySetResult();
            await releaseMutation.Task;
        };
        Provider.BeforeRetentionFenceForTestAsync = _ =>
        {
            retentionReachedFence.TrySetResult();
            return Task.CompletedTask;
        };

        child.ParentId = liveRoot.Id;
        var mutation = Provider.UpdateTimeTickers([child], CancellationToken.None);
        await mutationHasFence.Task;

        var retention = Provider.DeleteEligibleTimeTickerChainsAsync(
            Cutoffs(Ago(7)), 10, RetentionCursor.Start, CancellationToken.None);
        await retentionReachedFence.Task;
        releaseMutation.TrySetResult();

        Assert.Equal(1, await mutation);
        var retained = await retention;
        Assert.Equal(0, retained.Deleted);

        var rows = await _fixture.TimeTickers.Find(FilterDefinition<TimeTickerEntity>.Empty)
            .ToListAsync();
        Assert.Contains(rows, x => x.Id == doomedRoot.Id);
        Assert.Contains(rows, x => x.Id == child.Id && x.ParentId == liveRoot.Id);
        Assert.Contains(rows, x => x.Id == liveRoot.Id);
        Assert.DoesNotContain(rows, x => x.ParentId.HasValue && rows.All(p => p.Id != x.ParentId.Value));
    }
}
