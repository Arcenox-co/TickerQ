using System.Text;
using MongoDB.Bson;
using MongoDB.Driver;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Models;

namespace TickerQ.MongoDB.Tests;

[Collection("Mongo")]
public sealed class MongoNodeFinalizationOutboxTests : IAsyncLifetime
{
    private readonly MongoTestFixture _fixture;
    public MongoNodeFinalizationOutboxTests(MongoTestFixture fixture) => _fixture = fixture;
    public Task InitializeAsync() => _fixture.DropAllAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public void DirectConnectionReplicaSetAdvertisesDurableSupportOnlyAfterStartupProbe()
    {
        Assert.True(_fixture.UsesDirectConnection);
        Assert.False(_fixture.ProviderBeforeReadinessProbe);
        Assert.True(_fixture.Provider.SupportsDurableNodeFinalizationOutbox);
    }

    [Fact]
    public async Task AcceptedCommitAtomicallyPersistsStatusResultAndExactSecretFreeIntent()
    {
        var (ticker, acquired) = await AddAcquiredTimeTickerAsync();
        var intent = Intent(ticker.Id, acquired.AcquisitionToken!.Value);
        var context = Terminal(ticker.Id, acquired.AcquisitionToken, new TickerResultEnvelope([7], 1, "application/json"));

        Assert.True(await _fixture.Provider.CommitTerminalTickerAndEnqueueNodeFinalizationAsync(context, intent));
        Assert.Equal(TickerStatus.Done, (await _fixture.Provider.GetTimeTickerById(ticker.Id))!.Status);
        Assert.Equal(7, (await _fixture.Provider.GetTimeTickerResultAsync(ticker.Id))!.ToPayloadArray()[0]);
        var stored = Assert.Single(await _fixture.NodeFinalizations.Find(FilterDefinition<BsonDocument>.Empty).ToListAsync());
        Assert.Equal(intent.ExactBody, stored["ExactBody"].AsBsonBinaryData.Bytes);
        Assert.DoesNotContain("secret", stored.ToJson(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task StaleCommitMutatesNothingAndExactRetryIsIdempotentButMismatchFailsClosed()
    {
        var (ticker, acquired) = await AddAcquiredTimeTickerAsync();
        var intent = Intent(ticker.Id, acquired.AcquisitionToken!.Value);
        var staleToken = Guid.NewGuid();
        Assert.False(await _fixture.Provider.CommitTerminalTickerAndEnqueueNodeFinalizationAsync(
            Terminal(ticker.Id, staleToken, null), Intent(ticker.Id, staleToken)));
        Assert.Equal(TickerStatus.InProgress, (await _fixture.Provider.GetTimeTickerById(ticker.Id))!.Status);
        Assert.Equal(0, await _fixture.NodeFinalizations.CountDocumentsAsync(FilterDefinition<BsonDocument>.Empty));

        Assert.True(await _fixture.Provider.CommitTerminalTickerAndEnqueueNodeFinalizationAsync(
            Terminal(ticker.Id, acquired.AcquisitionToken, null), intent));
        Assert.True(await _fixture.Provider.CommitTerminalTickerAndEnqueueNodeFinalizationAsync(
            Terminal(ticker.Id, acquired.AcquisitionToken, null), intent));
        Assert.Equal(1, await _fixture.NodeFinalizations.CountDocumentsAsync(FilterDefinition<BsonDocument>.Empty));

        var mismatch = Intent(ticker.Id, acquired.AcquisitionToken.Value, intent.OutboxId, Guid.NewGuid());
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _fixture.Provider.CommitTerminalTickerAndEnqueueNodeFinalizationAsync(
                Terminal(ticker.Id, acquired.AcquisitionToken, null), mismatch));
    }

    [Fact]
    public async Task SameDispatchWithDifferentTerminalStatusOrResultFailsClosed()
    {
        var (ticker, acquired) = await AddAcquiredTimeTickerAsync();
        var intent = Intent(ticker.Id, acquired.AcquisitionToken!.Value);
        Assert.True(await _fixture.Provider.CommitTerminalTickerAndEnqueueNodeFinalizationAsync(
            Terminal(ticker.Id, acquired.AcquisitionToken, new TickerResultEnvelope([1], 1, "application/json")), intent));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _fixture.Provider.CommitTerminalTickerAndEnqueueNodeFinalizationAsync(
                Terminal(ticker.Id, acquired.AcquisitionToken, new TickerResultEnvelope([2], 1, "application/json")), intent));
        var failed = Terminal(ticker.Id, acquired.AcquisitionToken, null);
        failed.SetProperty(x => x.Status, TickerStatus.Failed);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _fixture.Provider.CommitTerminalTickerAndEnqueueNodeFinalizationAsync(failed, intent));

        Assert.Equal(TickerStatus.Done, (await _fixture.Provider.GetTimeTickerById(ticker.Id))!.Status);
        Assert.Equal(1, (await _fixture.Provider.GetTimeTickerResultAsync(ticker.Id))!.ToPayloadArray()[0]);
        Assert.Equal(1, await _fixture.NodeFinalizations.CountDocumentsAsync(FilterDefinition<BsonDocument>.Empty));
    }

    [Fact]
    public async Task ChildFinalizationRequiresPersistedExecutionIdentityAndParentAndRejectsSecondDispatch()
    {
        var root = NewTimeTicker();
        var child = NewTimeTicker(root.Id);
        await _fixture.Provider.AddTimeTickers([root, child]);
        var acquiredRoot = Assert.Single(await _fixture.Provider.AcquireImmediateTimeTickersAsync([root.Id]));
        var generation = acquiredRoot.ChainGeneration!.Value;
        await StampChildExecutionAsync(child.Id, generation);

        var first = Intent(child.Id, generation);
        Assert.True(await _fixture.Provider.CommitTerminalTickerAndEnqueueNodeFinalizationAsync(
            Terminal(child.Id, generation, new TickerResultEnvelope([3], 1, "application/json"),
                parentId: root.Id, rootId: root.Id, generation: generation), first));

        var second = Intent(child.Id, generation);
        Assert.False(await _fixture.Provider.CommitTerminalTickerAndEnqueueNodeFinalizationAsync(
            Terminal(child.Id, generation, new TickerResultEnvelope([9], 1, "application/json"),
                parentId: root.Id, rootId: root.Id, generation: generation), second));
        Assert.Equal(1, await _fixture.NodeFinalizations.CountDocumentsAsync(FilterDefinition<BsonDocument>.Empty));
        Assert.Equal(3, (await _fixture.Provider.GetTimeTickerResultAsync(child.Id))!.ToPayloadArray()[0]);
    }

    [Fact]
    public async Task ReparentedChildRejectsStaleParentIntentWithoutMutationOrOutbox()
    {
        var root = NewTimeTicker();
        var oldParent = NewTimeTicker(root.Id);
        var newParent = NewTimeTicker(root.Id);
        var child = NewTimeTicker(oldParent.Id);
        await _fixture.Provider.AddTimeTickers([root, oldParent, newParent, child]);
        var acquiredRoot = Assert.Single(await _fixture.Provider.AcquireImmediateTimeTickersAsync([root.Id]));
        var generation = acquiredRoot.ChainGeneration!.Value;
        await StampChildExecutionAsync(child.Id, generation);
        await _fixture.TimeTickers.UpdateOneAsync(x => x.Id == child.Id,
            Builders<TimeTickerEntity>.Update.Set(x => x.ParentId, newParent.Id));

        var intent = Intent(child.Id, generation);
        Assert.False(await _fixture.Provider.CommitTerminalTickerAndEnqueueNodeFinalizationAsync(
            Terminal(child.Id, generation, null, parentId: oldParent.Id,
                rootId: root.Id, generation: generation), intent));
        Assert.Equal(TickerStatus.InProgress, (await _fixture.Provider.GetTimeTickerById(child.Id))!.Status);
        Assert.Equal(0, await _fixture.NodeFinalizations.CountDocumentsAsync(FilterDefinition<BsonDocument>.Empty));
    }

    [Fact]
    public async Task ConcurrentClaimsHaveOneWinnerAndExpiredLeaseIsFenced()
    {
        var (ticker, acquired) = await AddAcquiredTimeTickerAsync();
        var intent = Intent(ticker.Id, acquired.AcquisitionToken!.Value);
        Assert.True(await _fixture.Provider.CommitTerminalTickerAndEnqueueNodeFinalizationAsync(
            Terminal(ticker.Id, acquired.AcquisitionToken, null), intent));

        var now = _fixture.FixedNow.AddMinutes(1);
        var results = await Task.WhenAll(
            _fixture.Provider.ClaimDueNodeFinalizationsAsync("worker-a", 1, now, now.AddMinutes(1)),
            _fixture.Provider.ClaimDueNodeFinalizationsAsync("worker-b", 1, now, now.AddMinutes(1)));
        var first = Assert.Single(results.SelectMany(x => x));
        var reclaimed = Assert.Single(await _fixture.Provider.ClaimDueNodeFinalizationsAsync(
            "worker-c", 1, now.AddMinutes(2), now.AddMinutes(3)));
        Assert.Equal(2, reclaimed.AttemptCount);
        Assert.False(await _fixture.Provider.CompleteNodeFinalizationAsync(first));
        Assert.False(await _fixture.Provider.RescheduleNodeFinalizationAsync(first, now.AddMinutes(4), "old"));
        Assert.True(await _fixture.Provider.RescheduleNodeFinalizationAsync(reclaimed, now.AddMinutes(4), "retryable"));
        Assert.Empty(await _fixture.Provider.ClaimDueNodeFinalizationsAsync("worker-d", 1, now.AddMinutes(3), now.AddMinutes(4)));
        var finalClaim = Assert.Single(await _fixture.Provider.ClaimDueNodeFinalizationsAsync(
            "worker-d", 1, now.AddMinutes(5), now.AddMinutes(6)));
        Assert.Equal(intent.ExactBody, finalClaim.Intent.ExactBody);
        Assert.True(await _fixture.Provider.CompleteNodeFinalizationAsync(finalClaim));
        Assert.False(await _fixture.Provider.CompleteNodeFinalizationAsync(finalClaim));
    }

    [Fact]
    public async Task FailureAfterOutboxInsertRollsBackTickerResultAndIntent()
    {
        var (ticker, acquired) = await AddAcquiredTimeTickerAsync();
        var intent = Intent(ticker.Id, acquired.AcquisitionToken!.Value);
        _fixture.ConcreteProvider.AfterNodeFinalizationInsertForTestAsync =
            _ => throw new InvalidOperationException("abort-after-insert");
        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                _fixture.Provider.CommitTerminalTickerAndEnqueueNodeFinalizationAsync(
                    Terminal(ticker.Id, acquired.AcquisitionToken, new TickerResultEnvelope([9], 1, "application/json")), intent));
        }
        finally
        {
            _fixture.ConcreteProvider.AfterNodeFinalizationInsertForTestAsync = null;
        }

        Assert.Equal(TickerStatus.InProgress, (await _fixture.Provider.GetTimeTickerById(ticker.Id))!.Status);
        Assert.Null(await _fixture.Provider.GetTimeTickerResultAsync(ticker.Id));
        Assert.Equal(0, await _fixture.NodeFinalizations.CountDocumentsAsync(FilterDefinition<BsonDocument>.Empty));
    }

    [Fact]
    public async Task MajorityCommitThenResponseLossIsResolvedByExactIndependentReadback()
    {
        var (ticker, acquired) = await AddAcquiredTimeTickerAsync();
        var intent = Intent(ticker.Id, acquired.AcquisitionToken!.Value);
        _fixture.ConcreteProvider.AfterNodeFinalizationTransactionForTestAsync =
            _ => throw new TimeoutException("simulated lost commit response");
        try
        {
            Assert.True(await _fixture.Provider.CommitTerminalTickerAndEnqueueNodeFinalizationAsync(
                Terminal(ticker.Id, acquired.AcquisitionToken,
                    new TickerResultEnvelope([4], 1, "application/json")), intent));
        }
        finally
        {
            _fixture.ConcreteProvider.AfterNodeFinalizationTransactionForTestAsync = null;
        }

        Assert.Equal(TickerStatus.Done, (await _fixture.Provider.GetTimeTickerById(ticker.Id))!.Status);
        Assert.Equal(4, (await _fixture.Provider.GetTimeTickerResultAsync(ticker.Id))!.ToPayloadArray()[0]);
        Assert.Equal(1, await _fixture.NodeFinalizations.CountDocumentsAsync(FilterDefinition<BsonDocument>.Empty));
    }

    [Fact]
    public async Task OccurrenceCommitPreservesTypeAndOutboxSurvivesTickerRetention()
    {
        var cron = new CronTickerEntity
        {
            Id = Guid.NewGuid(), Function = "node-cron", Expression = "* * * * *", Request = [],
            IsEnabled = true, CreatedAt = _fixture.FixedNow, UpdatedAt = _fixture.FixedNow
        };
        await _fixture.Provider.InsertCronTickers([cron], CancellationToken.None);
        var occurrence = new CronTickerOccurrenceEntity<CronTickerEntity>
        {
            Id = Guid.NewGuid(), CronTickerId = cron.Id, ExecutionTime = _fixture.FixedNow,
            Status = TickerStatus.Idle, CreatedAt = _fixture.FixedNow, UpdatedAt = _fixture.FixedNow
        };
        await _fixture.Provider.InsertCronTickerOccurrences([occurrence], CancellationToken.None);
        var acquired = Assert.Single(await _fixture.Provider.AcquireImmediateCronOccurrencesAsync([occurrence.Id]));
        var token = acquired.AcquisitionToken!.Value;
        var intent = Intent(occurrence.Id, token, tickerType: TickerType.CronTickerOccurrence);
        var context = Terminal(occurrence.Id, token, null, TickerType.CronTickerOccurrence, cron.Id);

        Assert.True(await _fixture.Provider.CommitTerminalTickerAndEnqueueNodeFinalizationAsync(context, intent));
        Assert.Equal(TickerStatus.Done,
            Assert.Single(await _fixture.Provider.GetAllCronTickerOccurrences(x => x.Id == occurrence.Id)).Status);
        Assert.Equal(1, await _fixture.Provider.RemoveCronTickerOccurrences([occurrence.Id], CancellationToken.None));
        Assert.Equal(1, await _fixture.NodeFinalizations.CountDocumentsAsync(FilterDefinition<BsonDocument>.Empty));
        var claim = Assert.Single(await _fixture.Provider.ClaimDueNodeFinalizationsAsync(
            "retention-worker", 1, _fixture.FixedNow.AddMinutes(1), _fixture.FixedNow.AddMinutes(2)));
        Assert.Equal(TickerType.CronTickerOccurrence, claim.Intent.TickerType);
        Assert.Equal(occurrence.Id, claim.Intent.TickerId);
    }

    [Fact]
    public async Task IndexesIncludeUniqueIdentityAndDueOrder()
    {
        var indexes = await (await _fixture.NodeFinalizations.Indexes.ListAsync()).ToListAsync();
        Assert.Contains(indexes, x => x["name"] == "_id_");
        Assert.Contains(indexes, x => x["name"] == "UQ_NodeFinalization_FullIdentity" && x["unique"].ToBoolean());
        Assert.Contains(indexes, x => x["name"] == "IX_NodeFinalization_Due");
    }

    private async Task<(TimeTickerEntity Original, TimeTickerEntity Acquired)> AddAcquiredTimeTickerAsync()
    {
        var ticker = new TimeTickerEntity
        {
            Id = Guid.NewGuid(), Function = "node-outbox", Request = [], ExecutionTime = _fixture.FixedNow,
            Status = TickerStatus.Idle, CreatedAt = _fixture.FixedNow, UpdatedAt = _fixture.FixedNow
        };
        await _fixture.Provider.AddTimeTickers([ticker]);
        return (ticker, Assert.Single(await _fixture.Provider.AcquireImmediateTimeTickersAsync([ticker.Id])));
    }

    private static InternalFunctionContext Terminal(Guid id, Guid? token, TickerResultEnvelope? result,
        TickerType tickerType = TickerType.TimeTicker, Guid? parentId = null,
        Guid? rootId = null, Guid? generation = null)
    {
        var context = new InternalFunctionContext
        {
            TickerId = id, FunctionName = "node-outbox", Type = tickerType,
            AcquisitionToken = token, ParentId = parentId,
            ChainRootId = rootId, ChainGeneration = generation
        }.SetProperty(x => x.Status, TickerStatus.Done).SetProperty(x => x.ReleaseLock, true);
        context.SetProperty(x => x.ResultEnvelope, result);
        return context;
    }

    private TimeTickerEntity NewTimeTicker(Guid? parentId = null) => new()
    {
        Id = Guid.NewGuid(), Function = "node-outbox-child", Request = [], ParentId = parentId,
        ExecutionTime = parentId == null ? _fixture.FixedNow : null, Status = TickerStatus.Idle,
        CreatedAt = _fixture.FixedNow, UpdatedAt = _fixture.FixedNow
    };

    private Task StampChildExecutionAsync(Guid childId, Guid generation)
        => _fixture.TimeTickers.UpdateOneAsync(x => x.Id == childId,
            Builders<TimeTickerEntity>.Update
                .Set(x => x.Status, TickerStatus.InProgress)
                .Set(x => x.AcquisitionToken, generation));

    private static NodeFinalizationIntent Intent(Guid tickerId, Guid token, Guid? outboxId = null,
        Guid? controlNonce = null, TickerType tickerType = TickerType.TimeTicker)
    {
        var dispatch = outboxId ?? Guid.NewGuid();
        var epoch = Guid.NewGuid();
        var control = controlNonce ?? Guid.NewGuid();
        var body = Encoding.UTF8.GetBytes($"{{\"tickerType\":{(int)tickerType},\"tickerId\":\"{tickerId:D}\",\"acquisitionToken\":\"{token:D}\",\"dispatchId\":\"{dispatch:D}\",\"nodeEpoch\":\"{epoch:D}\",\"controlNonce\":\"{control:D}\"}}");
        return new NodeFinalizationIntent(1, dispatch, tickerType, tickerId, token, dispatch,
            epoch, "https://node.example/finalize", "/finalize", false, Guid.NewGuid(), control, body,
            new DateTime(2025, 6, 15, 12, 0, 0, DateTimeKind.Utc));
    }
}