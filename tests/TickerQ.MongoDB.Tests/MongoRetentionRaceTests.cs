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
        Provider.AfterExplicitTimeTickerDeleteDiscoveryForTestAsync = null;
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
    public async Task RemoveTimeTickers_RetainsAggregateWhenRootIsInProgress()
    {
        var root = Node(TickerStatus.InProgress, null);
        root.LeaseUntil = _fixture.FixedNow.AddMinutes(-1);
        await _fixture.TimeTickers.InsertOneAsync(root);

        Assert.Equal(0, await Provider.RemoveTimeTickers([root.Id], CancellationToken.None));
        Assert.Equal(1, await _fixture.TimeTickers.CountDocumentsAsync(x => x.Id == root.Id));
    }

    [Fact]
    public async Task RemoveTimeTickers_RetainsWholeAggregateWhenDescendantHasAcquisitionToken()
    {
        var root = Node(TickerStatus.Done, Ago(10));
        var child = Node(TickerStatus.Done, Ago(10), root.Id);
        child.AcquisitionToken = Guid.NewGuid();
        child.LeaseUntil = _fixture.FixedNow.AddMinutes(-1);
        await _fixture.TimeTickers.InsertManyAsync([root, child]);

        Assert.Equal(0, await Provider.RemoveTimeTickers([root.Id], CancellationToken.None));
        Assert.Equal(2, await _fixture.TimeTickers.CountDocumentsAsync(
            Builders<TimeTickerEntity>.Filter.In(x => x.Id, new[] { root.Id, child.Id })));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RemoveTimeTickers_RejectsDescendantRequestWhenAggregateRootIsAcquired(bool childFirst)
    {
        var root = Node(TickerStatus.InProgress, null);
        root.AcquisitionToken = Guid.NewGuid();
        root.LeaseUntil = _fixture.FixedNow.AddMinutes(5);
        var child = Node(TickerStatus.Idle, null, root.Id);
        await _fixture.TimeTickers.InsertManyAsync([root, child]);

        var requested = childFirst ? new[] { child.Id, root.Id } : new[] { root.Id, child.Id };
        Assert.Equal(0, await Provider.RemoveTimeTickers(requested, CancellationToken.None));
        Assert.Equal(2, await _fixture.TimeTickers.CountDocumentsAsync(
            Builders<TimeTickerEntity>.Filter.In(x => x.Id, new[] { root.Id, child.Id })));
    }

    [Fact]
    public async Task ReplaceTimeTickerChainAsync_RejectsActiveOriginal()
    {
        var root = Node(TickerStatus.InProgress, null);
        root.AcquisitionToken = Guid.NewGuid();
        root.LeaseUntil = _fixture.FixedNow.AddMinutes(5);
        await _fixture.TimeTickers.InsertOneAsync(root);
        var replacement = Node(TickerStatus.Idle, null);

        Assert.Equal(0, await Provider.ReplaceTimeTickerChainAsync(
            root.Id, replacement, CancellationToken.None));
        Assert.Equal(1, await _fixture.TimeTickers.CountDocumentsAsync(x => x.Id == root.Id));
        Assert.Equal(0, await _fixture.TimeTickers.CountDocumentsAsync(x => x.Id == replacement.Id));
    }

    [Fact]
    public async Task ReplaceTimeTickerChainAsync_NormalizesStaleReplacementOwnershipAndIdentity()
    {
        var oldRoot = Node(TickerStatus.Idle, null);
        await _fixture.TimeTickers.InsertOneAsync(oldRoot);
        var replacement = Node(TickerStatus.InProgress, Ago(1), Guid.NewGuid());
        replacement.LockHolder = "stale-owner";
        replacement.LockedAt = _fixture.FixedNow;
        replacement.LeaseUntil = _fixture.FixedNow.AddMinutes(5);
        replacement.AcquisitionToken = Guid.NewGuid();
        replacement.ChainGeneration = Guid.NewGuid();
        var child = Node(TickerStatus.InProgress, Ago(1), Guid.NewGuid());
        child.LeaseUntil = _fixture.FixedNow.AddMinutes(5);
        child.AcquisitionToken = Guid.NewGuid();
        replacement.Children = [child];

        Assert.Equal(2, await Provider.ReplaceTimeTickerChainAsync(
            oldRoot.Id, replacement, CancellationToken.None));

        var rows = await _fixture.TimeTickers.Find(
            Builders<TimeTickerEntity>.Filter.In(x => x.Id, new[] { replacement.Id, child.Id }))
            .ToListAsync();
        var root = Assert.Single(rows, x => x.Id == replacement.Id);
        var persistedChild = Assert.Single(rows, x => x.Id == child.Id);
        Assert.Null(root.ParentId);
        Assert.Equal(replacement.Id, root.ChainRootId);
        Assert.Equal(replacement.Id, persistedChild.ParentId);
        Assert.Equal(replacement.Id, persistedChild.ChainRootId);
        Assert.All(rows, row =>
        {
            Assert.Equal(TickerStatus.Idle, row.Status);
            Assert.Null(row.LockHolder);
            Assert.Null(row.LockedAt);
            Assert.Null(row.LeaseUntil);
            Assert.Null(row.AcquisitionToken);
            Assert.Null(row.ChainGeneration);
            Assert.Null(row.ExecutedAt);
        });
    }

    [Fact]
    public async Task RemoveTimeTickers_RetriesSnapshotAndLosesToAcquisitionAfterDiscovery()
    {
        var root = Node(TickerStatus.Idle, null);
        await _fixture.TimeTickers.InsertOneAsync(root);
        var fired = 0;
        Provider.AfterExplicitTimeTickerDeleteDiscoveryForTestAsync = async ct =>
        {
            if (Interlocked.Exchange(ref fired, 1) != 0) return;
            await _fixture.TimeTickers.UpdateOneAsync(
                x => x.Id == root.Id,
                Builders<TimeTickerEntity>.Update
                    .Set(x => x.Status, TickerStatus.InProgress)
                    .Set(x => x.AcquisitionToken, Guid.NewGuid())
                    .Set(x => x.LeaseUntil, _fixture.FixedNow.AddMinutes(5))
                    .Set(x => x.UpdatedAt, _fixture.FixedNow.AddSeconds(1)),
                cancellationToken: ct);
        };

        Assert.Equal(0, await Provider.RemoveTimeTickers([root.Id], CancellationToken.None));
        Assert.Equal(TickerStatus.InProgress,
            (await _fixture.TimeTickers.Find(x => x.Id == root.Id).SingleAsync()).Status);
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
