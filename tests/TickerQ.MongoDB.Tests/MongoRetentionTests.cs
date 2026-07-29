using MongoDB.Bson;
using MongoDB.Driver;
using MongoDB.Driver.Core.Clusters;
using MongoDB.Driver.Core.Servers;
using TickerQ.MongoDB.Infrastructure;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Models;

namespace TickerQ.MongoDB.Tests;

[Collection("Mongo")]
public sealed class MongoRetentionTests : IAsyncLifetime
{
    private readonly MongoTestFixture _fixture;

    public MongoRetentionTests(MongoTestFixture fixture) => _fixture = fixture;

    public Task InitializeAsync() => _fixture.DropAllAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private DateTime Ago(double days) => _fixture.FixedNow - TimeSpan.FromDays(days);

    private static RetentionCutoffs Cutoffs(
        DateTime? succeeded = null,
        DateTime? failed = null,
        DateTime? cancelled = null,
        DateTime? skipped = null)
        => new(succeeded, failed, cancelled, skipped);

    private TimeTickerEntity Node(
        TickerStatus status,
        DateTime? executedAt,
        Guid? parentId = null,
        string? lockHolder = null,
        Guid? acquisitionToken = null,
        DateTime? leaseUntil = null)
    {
        var entity = new TimeTickerEntity
        {
            Id = Guid.NewGuid(),
            Function = "retention-test",
            Request = Array.Empty<byte>(),
            ExecutionTime = _fixture.FixedNow.AddHours(-1),
            LeaseUntil = leaseUntil,
            AcquisitionToken = acquisitionToken
        };

        Set(entity, nameof(entity.Status), status);
        Set(entity, nameof(entity.ExecutedAt), executedAt);
        Set(entity, nameof(entity.ParentId), parentId);
        Set(entity, nameof(entity.LockHolder), lockHolder);
        Set(entity, nameof(entity.LockedAt), lockHolder is null ? null : _fixture.FixedNow.AddHours(-1));
        Set(entity, nameof(entity.CreatedAt), Ago(40));
        Set(entity, nameof(entity.UpdatedAt), Ago(40));
        return entity;
    }

    private static void Set(TimeTickerEntity entity, string property, object? value)
        => typeof(TimeTickerEntity).GetProperty(property)!.SetValue(entity, value);

    [Fact]
    public void SupportsRetention_IsTrue()
        => Assert.True(_fixture.Provider.SupportsRetention);

    [Fact]
    public async Task SharedFixture_SupportsTransactionsInDirectReplicaSetMode()
    {
        await _fixture.Database.RunCommandAsync<BsonDocument>(new BsonDocument("ping", 1));

        var description = _fixture.Client.Cluster.Description;
        var server = Assert.Single(description.Servers);
        Assert.True(_fixture.UsesDirectConnection);
        Assert.Equal(ClusterType.Standalone, description.Type);
        Assert.Equal(ServerType.ReplicaSetPrimary, server.Type);
        Assert.NotNull(server.LogicalSessionTimeout);
        Assert.False(string.IsNullOrWhiteSpace(server.ReplicaSetConfig?.Name));

        using var session = await _fixture.Client.StartSessionAsync();
        session.StartTransaction();
        await _fixture.Database.GetCollection<BsonDocument>("transaction_capability_probe")
            .InsertOneAsync(session, new BsonDocument("_id", ObjectId.GenerateNewId()));
        await session.AbortTransactionAsync();
    }

    [Fact]
    public async Task BlockedOldestRoot_DoesNotStarveLaterEligibleRoot()
    {
        var blocked = Node(TickerStatus.Done, Ago(20));
        var blockedChild = Node(TickerStatus.Done, Ago(1), blocked.Id);
        var eligible = Node(TickerStatus.Done, Ago(10));
        await _fixture.TimeTickers.InsertManyAsync([blocked, blockedChild, eligible]);

        var first = await _fixture.Provider.DeleteEligibleTimeTickerChainsAsync(
            Cutoffs(succeeded: Ago(7)), 1, RetentionCursor.Start, CancellationToken.None);

        Assert.Equal(0, first.Deleted);
        Assert.True(first.HasMore);
        Assert.True(first.NextCursor.HasValue);

        var second = await _fixture.Provider.DeleteEligibleTimeTickerChainsAsync(
            Cutoffs(succeeded: Ago(7)), 1, first.NextCursor, CancellationToken.None);

        Assert.Equal(1, second.Deleted);
        Assert.NotNull(await _fixture.TimeTickers.Find(x => x.Id == blocked.Id).FirstOrDefaultAsync());
        Assert.NotNull(await _fixture.TimeTickers.Find(x => x.Id == blockedChild.Id).FirstOrDefaultAsync());
        Assert.Null(await _fixture.TimeTickers.Find(x => x.Id == eligible.Id).FirstOrDefaultAsync());
    }

    [Fact]
    public async Task FourLevelChain_IsDeletedOnlyWhenEveryNodeIsEligible()
    {
        var root = Node(TickerStatus.Done, Ago(10));
        var child = Node(TickerStatus.DueDone, Ago(10), root.Id);
        var grandchild = Node(TickerStatus.Failed, Ago(10), child.Id);
        var greatGrandchild = Node(TickerStatus.Skipped, Ago(10), grandchild.Id);
        await _fixture.TimeTickers.InsertManyAsync([root, child, grandchild, greatGrandchild]);

        var retained = await _fixture.Provider.DeleteEligibleTimeTickerChainsAsync(
            Cutoffs(succeeded: Ago(7), failed: Ago(7)), 10, RetentionCursor.Start, CancellationToken.None);
        Assert.Equal(0, retained.Deleted);
        Assert.Equal(4, await _fixture.TimeTickers.CountDocumentsAsync(FilterDefinition<TimeTickerEntity>.Empty));

        var deleted = await _fixture.Provider.DeleteEligibleTimeTickerChainsAsync(
            Cutoffs(succeeded: Ago(7), failed: Ago(7), skipped: Ago(7)), 10, RetentionCursor.Start, CancellationToken.None);
        Assert.Equal(4, deleted.Deleted);
        Assert.Equal(0, await _fixture.TimeTickers.CountDocumentsAsync(FilterDefinition<TimeTickerEntity>.Empty));
    }

    [Fact]
    public async Task Chain_RetainedWhole_WhenDeepDescendantIneligibleByWindow()
    {
        var root = Node(TickerStatus.Done, Ago(10));
        var child = Node(TickerStatus.Done, Ago(10), root.Id);
        var grandchild = Node(TickerStatus.Done, Ago(1), child.Id); // too recent → ineligible
        await _fixture.TimeTickers.InsertManyAsync([root, child, grandchild]);

        var result = await _fixture.Provider.DeleteEligibleTimeTickerChainsAsync(
            Cutoffs(succeeded: Ago(7)), 10, RetentionCursor.Start, CancellationToken.None);

        Assert.Equal(0, result.Deleted);
        Assert.Equal(3, await _fixture.TimeTickers.CountDocumentsAsync(FilterDefinition<TimeTickerEntity>.Empty));
    }

    [Fact]
    public async Task Chain_RetainedWhole_WhenDescendantIsOwned()
    {
        // A live acquisition token on a descendant means the aggregate is actively owned; the whole chain
        // must be retained even though the root is terminal and old.
        var root = Node(TickerStatus.Done, Ago(10));
        var child = Node(TickerStatus.Done, Ago(10), root.Id, acquisitionToken: Guid.NewGuid());
        await _fixture.TimeTickers.InsertManyAsync([root, child]);

        var result = await _fixture.Provider.DeleteEligibleTimeTickerChainsAsync(
            Cutoffs(succeeded: Ago(7)), 10, RetentionCursor.Start, CancellationToken.None);

        Assert.Equal(0, result.Deleted);
        Assert.Equal(2, await _fixture.TimeTickers.CountDocumentsAsync(FilterDefinition<TimeTickerEntity>.Empty));
    }

    [Fact]
    public async Task OversizedChain_IsRetainedWhole()
    {
        var root = Node(TickerStatus.Done, Ago(10));
        var child = Node(TickerStatus.Done, Ago(10), root.Id);
        var grandchild = Node(TickerStatus.Done, Ago(10), child.Id);
        var greatGrandchild = Node(TickerStatus.Done, Ago(10), grandchild.Id);
        await _fixture.TimeTickers.InsertManyAsync([root, child, grandchild, greatGrandchild]);

        var result = await _fixture.Provider.DeleteEligibleTimeTickerChainsAsync(
            new RetentionCutoffs(Ago(7), null, null, null, maxNodesPerChain: 3),
            10, RetentionCursor.Start, CancellationToken.None);

        Assert.Equal(0, result.Deleted);
        Assert.Equal(4, await _fixture.TimeTickers.CountDocumentsAsync(FilterDefinition<TimeTickerEntity>.Empty));
    }

    [Fact]
    public async Task ChainExactlyAtCap_IsDeletedWhole()
    {
        var root = Node(TickerStatus.Done, Ago(10));
        var child = Node(TickerStatus.Done, Ago(10), root.Id);
        var grandchild = Node(TickerStatus.Done, Ago(10), child.Id);
        await _fixture.TimeTickers.InsertManyAsync([root, child, grandchild]);

        var result = await _fixture.Provider.DeleteEligibleTimeTickerChainsAsync(
            new RetentionCutoffs(Ago(7), null, null, null, maxNodesPerChain: 3),
            10, RetentionCursor.Start, CancellationToken.None);

        Assert.Equal(3, result.Deleted);
        Assert.Equal(0, await _fixture.TimeTickers.CountDocumentsAsync(FilterDefinition<TimeTickerEntity>.Empty));
    }

    [Fact]
    public async Task Cancellation_IsHonored_DuringChainTraversal()
    {
        var root = Node(TickerStatus.Done, Ago(10));
        var child = Node(TickerStatus.Done, Ago(10), root.Id);
        await _fixture.TimeTickers.InsertManyAsync([root, child]);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            _fixture.Provider.DeleteEligibleTimeTickerChainsAsync(
                Cutoffs(succeeded: Ago(7)), 10, RetentionCursor.Start, cts.Token));
    }

    [Fact]
    public async Task UnexpiredRetentionClaim_BlocksOnDemandAcquire_ExpiredClaimIsRecovered()
    {
        var claimed = Node(
            TickerStatus.Done,
            Ago(10),
            lockHolder: TickerMongoPersistenceProvider<TimeTickerEntity, CronTickerEntity>.RetentionLockHolder,
            acquisitionToken: Guid.NewGuid(),
            leaseUntil: _fixture.FixedNow.AddMinutes(1));
        await _fixture.TimeTickers.InsertOneAsync(claimed);

        var blocked = await _fixture.Provider.AcquireTimeTickerOnDemandAsync(
            claimed.Id, _fixture.FixedNow, CancellationToken.None);
        Assert.Null(blocked);

        await _fixture.TimeTickers.UpdateOneAsync(
            x => x.Id == claimed.Id,
            Builders<TimeTickerEntity>.Update.Set(x => x.LeaseUntil, _fixture.FixedNow.AddSeconds(-1)));

        var acquired = await _fixture.Provider.AcquireTimeTickerOnDemandAsync(
            claimed.Id, _fixture.FixedNow, CancellationToken.None);
        Assert.NotNull(acquired);
        Assert.Equal(TickerStatus.InProgress, acquired!.Status);
        Assert.NotEqual(TickerMongoPersistenceProvider<TimeTickerEntity, CronTickerEntity>.RetentionLockHolder, acquired.LockHolder);
    }

    [Fact]
    public async Task CronRetention_DeletesOccurrencesButPreservesDefinition()
    {
        var cron = new CronTickerEntity
        {
            Id = Guid.NewGuid(),
            Function = "retention-cron",
            Expression = "* * * * *",
            Request = Array.Empty<byte>(),
            IsEnabled = true
        };
        await _fixture.CronTickers.InsertOneAsync(cron);
        await _fixture.CronTickerOccurrences.InsertOneAsync(new CronTickerOccurrenceEntity<CronTickerEntity>
        {
            Id = Guid.NewGuid(),
            CronTickerId = cron.Id,
            Status = TickerStatus.Done,
            ExecutedAt = Ago(10),
            ExecutionTime = Ago(10),
            CreatedAt = Ago(10),
            UpdatedAt = Ago(10)
        });

        var result = await _fixture.Provider.DeleteEligibleCronTickerOccurrencesAsync(
            Cutoffs(succeeded: Ago(7)), 10, CancellationToken.None);

        Assert.Equal(1, result.Deleted);
        Assert.Equal(1, await _fixture.CronTickers.CountDocumentsAsync(FilterDefinition<CronTickerEntity>.Empty));
        Assert.Equal(0, await _fixture.CronTickerOccurrences.CountDocumentsAsync(
            FilterDefinition<CronTickerOccurrenceEntity<CronTickerEntity>>.Empty));
    }
}
