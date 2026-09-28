using MongoDB.Bson;
using MongoDB.Driver;
using TickerQ.MongoDB.Infrastructure;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Models;

namespace TickerQ.MongoDB.Tests;

[Collection("Mongo")]
public sealed class MongoResultPersistenceTests : IAsyncLifetime
{
    private const int MaxPayloadBytes = 1024 * 1024;
    private readonly MongoTestFixture _fixture;
    private IMongoCollection<BsonDocument> Results
        => _fixture.Database.GetCollection<BsonDocument>("ticker_TickerResults");

    public MongoResultPersistenceTests(MongoTestFixture fixture) => _fixture = fixture;
    public Task InitializeAsync() => _fixture.DropAllAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public void AdvertisesResultPublicationSupport()
        => Assert.True(_fixture.Provider.SupportsResultPublication);

    [Fact]
    public async Task DueDoneIsAcceptedByAtomicCommit()
    {
        var (ticker, token) = await AddAcquiredTimeTickerAsync();
        var context = Success(ticker.Id, token, null, Envelope(8))
            .SetProperty(x => x.Status, TickerStatus.DueDone);

        Assert.True(await _fixture.Provider.CommitSuccessfulTickerAsync(context));
        Assert.Equal(TickerStatus.DueDone, (await _fixture.Provider.GetTimeTickerById(ticker.Id))!.Status);
        Assert.Equal(8, (await _fixture.Provider.GetTimeTickerResultAsync(ticker.Id))!.ToPayloadArray()[0]);
    }

    [Fact]
    public async Task ExplicitNullEnvelopeClearsStaleResultInSuccessfulCommit()
    {
        var (ticker, token) = await AddAcquiredTimeTickerAsync();
        await Results.InsertOneAsync(ResultDocument(ticker.Id, "time", 9));

        Assert.True(await _fixture.Provider.CommitSuccessfulTickerAsync(
            Success(ticker.Id, token, null, null)));

        Assert.Null(await _fixture.Provider.GetTimeTickerResultAsync(ticker.Id));
        Assert.Equal(TickerStatus.Done, (await _fixture.Provider.GetTimeTickerById(ticker.Id))!.Status);
    }

    [Fact]
    public async Task SuccessfulRootWritePersistsMetadataAndReturnsImmutableSnapshots()
    {
        var (ticker, token) = await AddAcquiredTimeTickerAsync();
        var source = new byte[] { 1, 2, 3 };
        var envelope = new TickerResultEnvelope(source, 1, "application/json", "sha256:abc", "Example.Result");
        var context = Success(ticker.Id, token, null, envelope);

        Assert.True(await _fixture.Provider.CommitSuccessfulTickerAsync(context));
        source[0] = 99;

        var first = Assert.IsType<TickerResultEnvelope>(await _fixture.Provider.GetTimeTickerResultAsync(ticker.Id));
        Assert.Equal(new byte[] { 1, 2, 3 }, first.ToPayloadArray());
        Assert.Equal(1, first.Version);
        Assert.Equal("application/json", first.MediaType);
        Assert.Equal("sha256:abc", first.ContractId);
        Assert.Equal("Example.Result", first.ContractType);
        var mutated = first.ToPayloadArray();
        mutated[0] = 88;
        var second = Assert.IsType<TickerResultEnvelope>(await _fixture.Provider.GetTimeTickerResultAsync(ticker.Id));
        Assert.Equal(new byte[] { 1, 2, 3 }, second.ToPayloadArray());
        Assert.NotSame(first, second);
    }

    [Fact]
    public async Task FreshProviderInstanceReadsPreviouslyCommittedResult()
    {
        var (ticker, token) = await AddAcquiredTimeTickerAsync();
        Assert.True(await _fixture.Provider.CommitSuccessfulTickerAsync(
            Success(ticker.Id, token, null, Envelope(42))));

        var restartedProvider = _fixture.NewProvider();
        var stored = Assert.IsType<TickerResultEnvelope>(
            await restartedProvider.GetTimeTickerResultAsync(ticker.Id));
        Assert.Equal(42, stored.ToPayloadArray()[0]);
    }

    [Fact]
    public async Task ChainChildrenAndGrandchildrenPersistOnlyTheirOwnDirectResults()
    {
        var root = NewTimeTicker();
        var child = NewTimeTicker(root.Id);
        var grandchild = NewTimeTicker(child.Id);
        await _fixture.Provider.AddTimeTickers([root, child, grandchild]);
        var acquiredRoot = Assert.Single(await _fixture.Provider.AcquireImmediateTimeTickersAsync([root.Id]));

        Assert.Equal(1, await StartChildAsync(child.Id, root.Id, root.Id, acquiredRoot.ChainGeneration));
        Assert.Equal(1, await StartChildAsync(grandchild.Id, child.Id, root.Id, acquiredRoot.ChainGeneration));
        Assert.True(await _fixture.Provider.CommitSuccessfulTickerAsync(
            Success(child.Id, null, root.Id, Envelope(2), rootId: root.Id,
                generation: acquiredRoot.ChainGeneration)));
        Assert.True(await _fixture.Provider.CommitSuccessfulTickerAsync(
            Success(grandchild.Id, null, child.Id, Envelope(3), rootId: root.Id,
                generation: acquiredRoot.ChainGeneration)));
        Assert.True(await _fixture.Provider.CommitSuccessfulTickerAsync(
            Success(root.Id, acquiredRoot.AcquisitionToken, null, Envelope(1))));

        Assert.Equal(1, (await _fixture.Provider.GetTimeTickerResultAsync(root.Id))!.ToPayloadArray()[0]);
        Assert.Equal(2, (await _fixture.Provider.GetTimeTickerResultAsync(child.Id))!.ToPayloadArray()[0]);
        Assert.Equal(3, (await _fixture.Provider.GetTimeTickerResultAsync(grandchild.Id))!.ToPayloadArray()[0]);
    }

    [Fact]
    public async Task CronOccurrencePersistsButCronDefinitionHasNoResult()
    {
        var cron = NewCron();
        await _fixture.Provider.InsertCronTickers([cron], CancellationToken.None);
        var occurrence = NewOccurrence(cron.Id);
        await _fixture.Provider.InsertCronTickerOccurrences([occurrence], CancellationToken.None);
        var acquired = Assert.Single(await _fixture.Provider.AcquireImmediateCronOccurrencesAsync([occurrence.Id]));

        Assert.True(await _fixture.Provider.CommitSuccessfulTickerAsync(
            Success(occurrence.Id, acquired.AcquisitionToken, cron.Id, Envelope(4), TickerType.CronTickerOccurrence)));

        Assert.Equal(4, (await _fixture.Provider.GetCronTickerOccurrenceResultAsync(occurrence.Id))!.ToPayloadArray()[0]);
        Assert.Null(await _fixture.Provider.GetTimeTickerResultAsync(cron.Id));
    }

    [Fact]
    public async Task SameGuidForTimeAndCronOccurrenceKeepsTypedResultsAndExactRetriesIndependent()
    {
        var sharedId = Guid.NewGuid();
        var time = NewTimeTicker();
        time.Id = sharedId;
        var cron = NewCron();
        var occurrence = NewOccurrence(cron.Id);
        occurrence.Id = sharedId;
        await _fixture.Provider.AddTimeTickers([time]);
        await _fixture.Provider.InsertCronTickers([cron], CancellationToken.None);
        await _fixture.Provider.InsertCronTickerOccurrences([occurrence], CancellationToken.None);

        var acquiredTime = Assert.Single(await _fixture.Provider.AcquireImmediateTimeTickersAsync([sharedId]));
        var acquiredOccurrence = Assert.Single(
            await _fixture.Provider.AcquireImmediateCronOccurrencesAsync([sharedId]));
        var timeCommit = Success(sharedId, acquiredTime.AcquisitionToken, null, Envelope(1));
        var cronCommit = Success(sharedId, acquiredOccurrence.AcquisitionToken, cron.Id, Envelope(2),
            TickerType.CronTickerOccurrence);

        Assert.True(await _fixture.Provider.CommitSuccessfulTickerAsync(timeCommit));
        Assert.True(await _fixture.Provider.CommitSuccessfulTickerAsync(cronCommit));
        Assert.True(await _fixture.Provider.CommitSuccessfulTickerAsync(timeCommit));
        Assert.True(await _fixture.Provider.CommitSuccessfulTickerAsync(cronCommit));
        Assert.Equal(1, (await _fixture.Provider.GetTimeTickerResultAsync(sharedId))!.ToPayloadArray()[0]);
        Assert.Equal(2, (await _fixture.Provider.GetCronTickerOccurrenceResultAsync(sharedId))!.ToPayloadArray()[0]);
        Assert.Equal(2, await Results.CountDocumentsAsync(FilterDefinition<BsonDocument>.Empty));
    }

    [Fact]
    public async Task LegacyScalarResultIsAdoptedIntoTypedIdentityWithoutCrossKindExposure()
    {
        var id = Guid.NewGuid();
        await Results.InsertOneAsync(ResultDocument(id, "time", 7));

        Assert.Equal(7, (await _fixture.Provider.GetTimeTickerResultAsync(id))!.ToPayloadArray()[0]);
        Assert.Null(await _fixture.Provider.GetCronTickerOccurrenceResultAsync(id));

        var documents = await Results.Find(FilterDefinition<BsonDocument>.Empty).ToListAsync();
        var adopted = Assert.Single(documents);
        Assert.True(adopted["_id"].IsBsonDocument);
        Assert.Equal("time", adopted["_id"].AsBsonDocument["Kind"].AsString);
        Assert.Equal(id, adopted["_id"].AsBsonDocument["TickerId"].AsGuid);
    }

    [Fact]
    public async Task StaleFenceDoesNotPublishOrChangeTerminalStatus()
    {
        var (ticker, _) = await AddAcquiredTimeTickerAsync();
        var affected = await _fixture.Provider.CommitSuccessfulTickerAsync(
            Success(ticker.Id, Guid.NewGuid(), null, Envelope(5)));

        Assert.False(affected);
        Assert.Null(await _fixture.Provider.GetTimeTickerResultAsync(ticker.Id));
        Assert.Equal(TickerStatus.InProgress, (await _fixture.Provider.GetTimeTickerById(ticker.Id))!.Status);
    }

    [Fact]
    public async Task Exact_terminal_replay_loses_after_new_queued_generation()
    {
        var ticker = NewTimeTicker();
        await _fixture.Provider.AddTimeTickers([ticker]);
        var runA = Assert.Single(await _fixture.Provider.AcquireImmediateTimeTickersAsync([ticker.Id]));
        var terminalA = Success(ticker.Id, runA.AcquisitionToken, null, null);

        Assert.True(await _fixture.Provider.CommitTerminalTickerAsync(terminalA));
        Assert.True(await _fixture.Provider.CommitTerminalTickerAsync(terminalA));
        Assert.Equal(1, await _fixture.Provider.UpdateTimeTicker(
            new InternalFunctionContext { TickerId = ticker.Id, Type = TickerType.TimeTicker }
                .SetProperty(x => x.Status, TickerStatus.Idle), CancellationToken.None));
        var idle = await _fixture.Provider.GetTimeTickerById(ticker.Id, CancellationToken.None);
        var candidate = new TimeTickerEntity
        {
            Id = ticker.Id, UpdatedAt = idle!.UpdatedAt, AcquisitionToken = idle.AcquisitionToken
        };
        var queued = new List<TimeTickerEntity>();
        await foreach (var item in _fixture.Provider.QueueTimeTickers([candidate])) queued.Add(item);
        var runB = Assert.Single(queued);
        Assert.NotEqual(runA.AcquisitionToken, runB.AcquisitionToken);
        Assert.Equal(TickerStatus.Queued, runB.Status);

        Assert.False(await _fixture.Provider.CommitTerminalTickerAsync(terminalA));
        var persisted = await _fixture.Provider.GetTimeTickerById(ticker.Id, CancellationToken.None);
        Assert.Equal(runB.AcquisitionToken, persisted!.AcquisitionToken);
        Assert.Equal(TickerStatus.Queued, persisted.Status);
    }

    [Fact]
    public async Task Exact_terminal_replay_and_new_generation_are_serialized_by_entity_authority()
    {
        var ticker = NewTimeTicker();
        await _fixture.Provider.AddTimeTickers([ticker]);
        var runA = Assert.Single(await _fixture.Provider.AcquireImmediateTimeTickersAsync([ticker.Id]));
        var terminalA = Success(ticker.Id, runA.AcquisitionToken, null, null);
        Assert.True(await _fixture.Provider.CommitTerminalTickerAsync(terminalA));
        Assert.Equal(1, await _fixture.Provider.UpdateTimeTicker(
            new InternalFunctionContext { TickerId = ticker.Id, Type = TickerType.TimeTicker }
                .SetProperty(x => x.Status, TickerStatus.Idle)));
        var idle = await _fixture.Provider.GetTimeTickerById(ticker.Id);
        var authorityReached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseAuthority = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _fixture.ConcreteProvider.AfterTerminalReplayAuthorityForTestAsync = async _ =>
        {
            authorityReached.TrySetResult();
            await releaseAuthority.Task;
        };

        var replay = Task.Run(() => _fixture.Provider.CommitTerminalTickerAsync(terminalA));
        await authorityReached.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var acquire = Task.Run(async () =>
        {
            var queued = new List<TimeTickerEntity>();
            await foreach (var row in _fixture.Provider.QueueTimeTickers([
                new TimeTickerEntity
                {
                    Id = ticker.Id, UpdatedAt = idle!.UpdatedAt,
                    AcquisitionToken = idle.AcquisitionToken
                }])) queued.Add(row);
            return queued;
        });
        await Task.Delay(150);
        Assert.False(acquire.IsCompleted);

        releaseAuthority.TrySetResult();
        Assert.True(await replay);
        var runB = Assert.Single(await acquire);
        Assert.NotEqual(runA.AcquisitionToken, runB.AcquisitionToken);
        _fixture.ConcreteProvider.AfterTerminalReplayAuthorityForTestAsync = null;
    }

    [Fact]
    public async Task Legacy_result_adoption_serializes_with_terminal_reacquisition()
    {
        var ticker = NewTimeTicker();
        ticker.Status = TickerStatus.Done;
        ticker.ExecutedAt = _fixture.FixedNow;
        await _fixture.Provider.AddTimeTickers([ticker]);
        await Results.InsertOneAsync(ResultDocument(ticker.Id, "time", 17));
        var authorityReached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseAuthority = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _fixture.ConcreteProvider.AfterLegacyResultAuthorityForTestAsync = async _ =>
        {
            authorityReached.TrySetResult();
            await releaseAuthority.Task;
        };

        var read = Task.Run(() => _fixture.Provider.GetTimeTickerResultAsync(ticker.Id));
        await authorityReached.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var acquire = Task.Run(() => _fixture.Provider.AcquireTimeTickerOnDemandAsync(
            ticker.Id, _fixture.FixedNow, CancellationToken.None));
        await Task.Delay(150);
        Assert.False(acquire.IsCompleted);

        releaseAuthority.TrySetResult();
        Assert.Equal(17, (await read)!.ToPayloadArray()[0]);
        Assert.NotNull(await acquire);
        _fixture.ConcreteProvider.AfterLegacyResultAuthorityForTestAsync = null;
    }

    [Fact]
    public async Task StaleChildGenerationCannotOverwriteRerunStatusOrResult()
    {
        var root = NewTimeTicker();
        var child = NewTimeTicker(root.Id);
        await _fixture.Provider.AddTimeTickers([root, child]);

        var runA = Assert.Single(await _fixture.Provider.AcquireImmediateTimeTickersAsync([root.Id]));
        var generationA = Assert.IsType<Guid>(runA.ChainGeneration);
        var rootDone = new InternalFunctionContext
        {
            TickerId = root.Id,
            ChainRootId = root.Id,
            ChainGeneration = generationA,
            AcquisitionToken = runA.AcquisitionToken,
            Type = TickerType.TimeTicker
        }.SetProperty(x => x.Status, TickerStatus.Done)
         .SetProperty(x => x.ReleaseLock, true);
        Assert.Equal(1, await _fixture.Provider.UpdateTimeTicker(rootDone));

        var runB = Assert.IsType<TimeTickerEntity>(
            await _fixture.Provider.AcquireTimeTickerOnDemandAsync(root.Id, _fixture.FixedNow));
        var generationB = Assert.IsType<Guid>(runB.ChainGeneration);
        Assert.NotEqual(generationA, generationB);

        Assert.True(await _fixture.Provider.CommitSuccessfulTickerAsync(
            Success(child.Id, null, root.Id, Envelope(2), rootId: root.Id,
                generation: generationB)));
        Assert.False(await _fixture.Provider.CommitSuccessfulTickerAsync(
            Success(child.Id, null, root.Id, Envelope(1), rootId: root.Id,
                generation: generationA)));

        var staleStatus = new InternalFunctionContext
        {
            TickerId = child.Id,
            ParentId = root.Id,
            ChainRootId = root.Id,
            ChainGeneration = generationA,
            Type = TickerType.TimeTicker
        }.SetProperty(x => x.Status, TickerStatus.Failed)
         .SetProperty(x => x.ExceptionDetails, "late A");
        Assert.Equal(0, await _fixture.Provider.UpdateTimeTicker(staleStatus));

        Assert.Equal(TickerStatus.Done,
            (await _fixture.Provider.GetTimeTickerById(child.Id))!.Status);
        Assert.Equal(2,
            (await _fixture.Provider.GetTimeTickerResultAsync(child.Id))!.ToPayloadArray()[0]);
    }

    [Fact]
    public async Task StaleRootTokenEmbeddedChildTerminalWriteIsNotAcknowledged()
    {
        var root = NewTimeTicker();
        var child = NewTimeTicker(root.Id);
        await _fixture.Provider.AddTimeTickers([root, child]);
        var acquired = Assert.Single(await _fixture.Provider.AcquireImmediateTimeTickersAsync([root.Id]));
        Assert.Equal(1, await StartChildAsync(child.Id, root.Id, root.Id, acquired.ChainGeneration));
        var stale = Success(child.Id, Guid.NewGuid(), root.Id, Envelope(5),
            rootId: root.Id, generation: acquired.ChainGeneration);

        Assert.False(await _fixture.Provider.CommitTerminalTickerAsync(stale));
        Assert.Null(await _fixture.Provider.GetTimeTickerResultAsync(child.Id));
        Assert.Equal(TickerStatus.InProgress,
            (await _fixture.Provider.GetTimeTickerById(child.Id))!.Status);
    }

    [Fact]
    public async Task SuccessfulRerunWithoutResultClearsPriorResultAtomically()
    {
        var (ticker, token) = await AddAcquiredTimeTickerAsync();
        Assert.True(await _fixture.Provider.CommitSuccessfulTickerAsync(Success(ticker.Id, token, null, Envelope(6))));
        var rerun = await _fixture.Provider.AcquireTimeTickerOnDemandAsync(ticker.Id, _fixture.FixedNow);
        Assert.NotNull(rerun);
        Assert.Null(await _fixture.Provider.GetTimeTickerResultAsync(ticker.Id));

        Assert.True(await _fixture.Provider.CommitSuccessfulTickerAsync(Success(ticker.Id, rerun!.AcquisitionToken, null, null)));
        Assert.Null(await _fixture.Provider.GetTimeTickerResultAsync(ticker.Id));
    }

    [Fact]
    public async Task FailedAttemptResultNeverLeaksIntoRetryThatPublishesNothing()
    {
        var (ticker, token) = await AddAcquiredTimeTickerAsync();
        var failed = new InternalFunctionContext
        {
            TickerId = ticker.Id, FunctionName = "result-test", Type = TickerType.TimeTicker,
            AcquisitionToken = token
        }.SetProperty(x => x.Status, TickerStatus.Failed)
         .SetProperty(x => x.ResultEnvelope, Envelope(99))
         .SetProperty(x => x.ReleaseLock, true);

        Assert.Equal(1, await _fixture.Provider.UpdateTimeTicker(failed));
        Assert.Null(await _fixture.Provider.GetTimeTickerResultAsync(ticker.Id));

        var retry = Assert.IsType<TimeTickerEntity>(
            await _fixture.Provider.AcquireTimeTickerOnDemandAsync(ticker.Id, _fixture.FixedNow));
        Assert.True(await _fixture.Provider.CommitSuccessfulTickerAsync(
            Success(ticker.Id, retry.AcquisitionToken, null, null)));
        Assert.Null(await _fixture.Provider.GetTimeTickerResultAsync(ticker.Id));
    }

    [Fact]
    public async Task ForcedAbortRollsBackStatusAndResultTogether()
    {
        var (ticker, token) = await AddAcquiredTimeTickerAsync();
        _fixture.ConcreteProvider.AfterResultMutationForTestAsync = _ => throw new InvalidOperationException("abort");
        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                _fixture.Provider.CommitSuccessfulTickerAsync(Success(ticker.Id, token, null, Envelope(7))));
        }
        finally
        {
            _fixture.ConcreteProvider.AfterResultMutationForTestAsync = null;
        }

        Assert.Null(await _fixture.Provider.GetTimeTickerResultAsync(ticker.Id));
        Assert.Equal(TickerStatus.InProgress, (await _fixture.Provider.GetTimeTickerById(ticker.Id))!.Status);
    }

    [Fact]
    public async Task MajorityCommitThenResponseLossReturnsTruthAndExactRetryIsIdempotent()
    {
        var (ticker, token) = await AddAcquiredTimeTickerAsync();
        var committed = Success(ticker.Id, token, null, Envelope(7));
        _fixture.ConcreteProvider.AfterTerminalTransactionForTestAsync =
            _ => throw new TimeoutException("simulated lost terminal commit response");
        try
        {
            Assert.True(await _fixture.Provider.CommitSuccessfulTickerAsync(committed));
        }
        finally
        {
            _fixture.ConcreteProvider.AfterTerminalTransactionForTestAsync = null;
        }

        Assert.True(await _fixture.Provider.CommitSuccessfulTickerAsync(committed));
        Assert.False(await _fixture.Provider.CommitSuccessfulTickerAsync(
            Success(ticker.Id, token, null, Envelope(8))));
        Assert.Equal(TickerStatus.Done, (await _fixture.Provider.GetTimeTickerById(ticker.Id))!.Status);
        Assert.Equal(7, (await _fixture.Provider.GetTimeTickerResultAsync(ticker.Id))!.ToPayloadArray()[0]);
    }

    [Fact]
    public async Task CronOccurrenceCancellationAfterCommitReturnsTruthAndExactRetryCannotAcknowledgeDifferentResult()
    {
        var cron = NewCron();
        await _fixture.Provider.InsertCronTickers([cron], CancellationToken.None);
        var occurrence = NewOccurrence(cron.Id);
        await _fixture.Provider.InsertCronTickerOccurrences([occurrence], CancellationToken.None);
        var acquired = Assert.Single(await _fixture.Provider.AcquireImmediateCronOccurrencesAsync([occurrence.Id]));
        var committed = Success(occurrence.Id, acquired.AcquisitionToken, cron.Id, Envelope(4),
            TickerType.CronTickerOccurrence);
        using var cancellation = new CancellationTokenSource();
        _fixture.ConcreteProvider.AfterTerminalTransactionForTestAsync = _ =>
        {
            cancellation.Cancel();
            throw new OperationCanceledException(cancellation.Token);
        };
        try
        {
            Assert.True(await _fixture.Provider.CommitSuccessfulTickerAsync(committed, cancellation.Token));
        }
        finally
        {
            _fixture.ConcreteProvider.AfterTerminalTransactionForTestAsync = null;
        }

        Assert.True(await _fixture.Provider.CommitSuccessfulTickerAsync(committed));
        Assert.False(await _fixture.Provider.CommitSuccessfulTickerAsync(
            Success(occurrence.Id, acquired.AcquisitionToken, cron.Id, Envelope(5),
                TickerType.CronTickerOccurrence)));
        Assert.Equal(TickerStatus.Done,
            Assert.Single(await _fixture.Provider.GetAllCronTickerOccurrences(x => x.Id == occurrence.Id)).Status);
        Assert.Equal(4,
            (await _fixture.Provider.GetCronTickerOccurrenceResultAsync(occurrence.Id))!.ToPayloadArray()[0]);
    }

    [Fact]
    public async Task FailedTerminalCommitAmbiguityReturnsTruthWithoutPublishingAResult()
    {
        var (ticker, token) = await AddAcquiredTimeTickerAsync();
        var failed = Terminal(ticker.Id, token, TickerStatus.Failed, "ordinary failure");
        _fixture.ConcreteProvider.AfterTerminalTransactionForTestAsync =
            _ => throw new TimeoutException("simulated lost failed-terminal response");
        try
        {
            Assert.True(await _fixture.Provider.CommitTerminalTickerAsync(failed));
        }
        finally
        {
            _fixture.ConcreteProvider.AfterTerminalTransactionForTestAsync = null;
        }

        Assert.True(await _fixture.Provider.CommitTerminalTickerAsync(failed));
        Assert.False(await _fixture.Provider.CommitTerminalTickerAsync(
            Terminal(ticker.Id, token, TickerStatus.Cancelled, "different terminal mutation")));
        var stored = await _fixture.Provider.GetTimeTickerById(ticker.Id);
        Assert.Equal(TickerStatus.Failed, stored!.Status);
        Assert.Equal("ordinary failure", stored.ExceptionMessage);
        Assert.Null(await _fixture.Provider.GetTimeTickerResultAsync(ticker.Id));
    }

    [Fact]
    public async Task RetryNeverAcknowledgesAnotherExecutionsIdenticalTerminalWrite()
    {
        var (ticker, tokenA) = await AddAcquiredTimeTickerAsync();
        var attemptA = Success(ticker.Id, tokenA, null, Envelope(6));
        Assert.True(await _fixture.Provider.CommitSuccessfulTickerAsync(attemptA));

        var attemptB = Assert.IsType<TimeTickerEntity>(
            await _fixture.Provider.AcquireTimeTickerOnDemandAsync(ticker.Id, _fixture.FixedNow));
        Assert.NotEqual(tokenA, attemptB.AcquisitionToken);
        Assert.True(await _fixture.Provider.CommitSuccessfulTickerAsync(
            Success(ticker.Id, attemptB.AcquisitionToken, null, Envelope(6))));

        Assert.False(await _fixture.Provider.CommitSuccessfulTickerAsync(attemptA));
        Assert.Equal(6, (await _fixture.Provider.GetTimeTickerResultAsync(ticker.Id))!.ToPayloadArray()[0]);
    }

    [Fact]
    public async Task MissingLegacyCorruptOversizeAndUnknownDocumentsFailClosed()
    {
        var missing = Guid.NewGuid();
        Assert.Null(await _fixture.Provider.GetTimeTickerResultAsync(missing));

        foreach (var document in new[]
        {
            new BsonDocument { ["_id"] = GuidValue(Guid.NewGuid()), ["Kind"] = "time", ["Payload"] = "not-binary", ["Version"] = 1, ["MediaType"] = "application/json" },
            new BsonDocument { ["_id"] = GuidValue(Guid.NewGuid()), ["Kind"] = "time", ["Payload"] = new BsonBinaryData(new byte[MaxPayloadBytes + 1]), ["Version"] = 1, ["MediaType"] = "application/json" },
            new BsonDocument { ["_id"] = GuidValue(Guid.NewGuid()), ["Kind"] = "time", ["Payload"] = new BsonBinaryData(Array.Empty<byte>()), ["Version"] = 999, ["MediaType"] = "application/json" }
        })
        {
            await Results.InsertOneAsync(document);
            Assert.Null(await _fixture.Provider.GetTimeTickerResultAsync(document["_id"].AsGuid));
        }
    }

    [Fact]
    public async Task OversizeAndUnknownPublicationAreRejectedBeforeStatusWrite()
    {
        foreach (var envelope in new[]
        {
            new TickerResultEnvelope(new byte[MaxPayloadBytes + 1], 1, "application/json"),
            new TickerResultEnvelope(Array.Empty<byte>(), 999, "application/json")
        })
        {
            var (ticker, token) = await AddAcquiredTimeTickerAsync();
            await Assert.ThrowsAnyAsync<Exception>(() =>
                _fixture.Provider.CommitSuccessfulTickerAsync(Success(ticker.Id, token, null, envelope)));
            Assert.Equal(TickerStatus.InProgress, (await _fixture.Provider.GetTimeTickerById(ticker.Id))!.Status);
            Assert.Null(await _fixture.Provider.GetTimeTickerResultAsync(ticker.Id));
        }
    }

    [Fact]
    public async Task RecursiveDeleteAndRetentionDeleteResultSideDocuments()
    {
        var root = NewTimeTicker();
        var child = NewTimeTicker(root.Id);
        await _fixture.Provider.AddTimeTickers([root, child]);
        var acquiredRoot = Assert.Single(await _fixture.Provider.AcquireImmediateTimeTickersAsync([root.Id]));
        Assert.Equal(1, await StartChildAsync(child.Id, root.Id, root.Id, acquiredRoot.ChainGeneration));
        Assert.True(await _fixture.Provider.CommitSuccessfulTickerAsync(
            Success(child.Id, null, root.Id, Envelope(2), rootId: root.Id,
                generation: acquiredRoot.ChainGeneration)));
        Assert.True(await _fixture.Provider.CommitSuccessfulTickerAsync(
            Success(root.Id, acquiredRoot.AcquisitionToken, null, Envelope(1))));

        Assert.Equal(2, await _fixture.Provider.RemoveTimeTickers([root.Id]));
        Assert.Equal(0, await Results.CountDocumentsAsync(FilterDefinition<BsonDocument>.Empty));

        var old = NewTimeTicker();
        old.Status = TickerStatus.Done;
        old.ExecutedAt = _fixture.FixedNow.AddDays(-10);
        await _fixture.Provider.AddTimeTickers([old]);
        await Results.InsertOneAsync(ResultDocument(old.Id, "time", 3));
        var deleted = await _fixture.Provider.DeleteEligibleTimeTickerChainsAsync(
            new RetentionCutoffs(_fixture.FixedNow.AddDays(-7), null, null, null), 10, RetentionCursor.Start);
        Assert.Equal(1, deleted.Deleted);
        Assert.Null(await _fixture.Provider.GetTimeTickerResultAsync(old.Id));
    }

    [Fact]
    public async Task CronOccurrenceDeleteAndRetentionLeaveDefinitionAndNoResultOrphans()
    {
        var cron = NewCron();
        await _fixture.Provider.InsertCronTickers([cron], CancellationToken.None);
        var first = NewOccurrence(cron.Id);
        await _fixture.Provider.InsertCronTickerOccurrences([first], CancellationToken.None);
        await Results.InsertOneAsync(ResultDocument(first.Id, "cron-occurrence", 1));
        Assert.Equal(1, await _fixture.Provider.RemoveCronTickerOccurrences([first.Id], CancellationToken.None));
        Assert.Null(await _fixture.Provider.GetCronTickerOccurrenceResultAsync(first.Id));

        var old = NewOccurrence(cron.Id);
        old.Status = TickerStatus.Done;
        old.ExecutedAt = _fixture.FixedNow.AddDays(-10);
        await _fixture.Provider.InsertCronTickerOccurrences([old], CancellationToken.None);
        await Results.InsertOneAsync(ResultDocument(old.Id, "cron-occurrence", 2));
        var deleted = await _fixture.Provider.DeleteEligibleCronTickerOccurrencesAsync(
            new RetentionCutoffs(_fixture.FixedNow.AddDays(-7), null, null, null), 10);
        Assert.Equal(1, deleted.Deleted);
        Assert.Null(await _fixture.Provider.GetCronTickerOccurrenceResultAsync(old.Id));
        Assert.NotNull(await _fixture.Provider.GetCronTickerById(cron.Id, CancellationToken.None));
    }

    [Fact]
    public async Task StaleRestartClearsPriorTimeAndOccurrenceResults()
    {
        var time = NewTimeTicker();
        time.Status = TickerStatus.InProgress;
        time.OnStale = StaleAction.Restart;
        time.LockHolder = "dead";
        time.LeaseUntil = _fixture.FixedNow.AddSeconds(-1);
        await _fixture.Provider.AddTimeTickers([time]);

        var cron = NewCron();
        cron.OnStale = StaleAction.Restart;
        await _fixture.Provider.InsertCronTickers([cron], CancellationToken.None);
        var occurrence = NewOccurrence(cron.Id);
        occurrence.Status = TickerStatus.InProgress;
        occurrence.LockHolder = "dead";
        occurrence.LeaseUntil = _fixture.FixedNow.AddSeconds(-1);
        await _fixture.Provider.InsertCronTickerOccurrences([occurrence], CancellationToken.None);

        await Results.InsertManyAsync([
            ResultDocument(time.Id, "time", 1),
            ResultDocument(occurrence.Id, "cron-occurrence", 2)]);

        var recovered = await _fixture.Provider.RecoverStaleTickers(2);

        Assert.Equal(1, recovered.RestartedTimeTickers);
        Assert.Equal(1, recovered.RestartedCronOccurrences);
        Assert.Null(await _fixture.Provider.GetTimeTickerResultAsync(time.Id));
        Assert.Null(await _fixture.Provider.GetCronTickerOccurrenceResultAsync(occurrence.Id));
    }

    private async Task<(TimeTickerEntity Ticker, Guid? Token)> AddAcquiredTimeTickerAsync()
    {
        var ticker = NewTimeTicker();
        await _fixture.Provider.AddTimeTickers([ticker]);
        var acquired = Assert.Single(await _fixture.Provider.AcquireImmediateTimeTickersAsync([ticker.Id]));
        return (ticker, acquired.AcquisitionToken);
    }

    private TimeTickerEntity NewTimeTicker(Guid? parentId = null) => new()
    {
        Id = Guid.NewGuid(), Function = "result-test", Request = Array.Empty<byte>(),
        ExecutionTime = parentId.HasValue ? null : _fixture.FixedNow,
        ParentId = parentId, Status = TickerStatus.Idle,
        CreatedAt = _fixture.FixedNow, UpdatedAt = _fixture.FixedNow
    };

    private CronTickerEntity NewCron() => new()
    {
        Id = Guid.NewGuid(), Function = "result-cron", Expression = "* * * * *",
        Request = Array.Empty<byte>(), IsEnabled = true,
        CreatedAt = _fixture.FixedNow, UpdatedAt = _fixture.FixedNow
    };

    private CronTickerOccurrenceEntity<CronTickerEntity> NewOccurrence(Guid cronId) => new()
    {
        Id = Guid.NewGuid(), CronTickerId = cronId, ExecutionTime = _fixture.FixedNow,
        Status = TickerStatus.Idle, CreatedAt = _fixture.FixedNow, UpdatedAt = _fixture.FixedNow
    };

    private static TickerResultEnvelope Envelope(byte value)
        => new([value], 1, "application/json", "contract", "Example.Result");

    private static InternalFunctionContext Terminal(
        Guid id, Guid? token, TickerStatus status, string exception)
        => new InternalFunctionContext
        {
            TickerId = id, FunctionName = "result-test", Type = TickerType.TimeTicker,
            AcquisitionToken = token
        }.SetProperty(x => x.Status, status)
         .SetProperty(x => x.ExceptionDetails, exception)
         .SetProperty(x => x.ReleaseLock, true);

    private static InternalFunctionContext Success(
        Guid id, Guid? token, Guid? parentId, TickerResultEnvelope? envelope,
        TickerType type = TickerType.TimeTicker, Guid? rootId = null,
        Guid? generation = null)
    {
        var context = new InternalFunctionContext
        {
            TickerId = id, FunctionName = "result-test", Type = type,
            ParentId = parentId, AcquisitionToken = token,
            ChainRootId = rootId, ChainGeneration = generation
        }.SetProperty(x => x.Status, TickerStatus.Done)
         .SetProperty(x => x.ReleaseLock, true);
        context.SetProperty(x => x.ResultEnvelope, envelope);
        return context;
    }

    private Task<int> StartChildAsync(Guid id, Guid parentId, Guid rootId, Guid? generation)
        => _fixture.Provider.UpdateTimeTicker(new InternalFunctionContext
        {
            TickerId = id, ParentId = parentId, ChainRootId = rootId,
            ChainGeneration = generation, Type = TickerType.TimeTicker
        }.SetProperty(x => x.Status, TickerStatus.InProgress));

    private static BsonDocument ResultDocument(Guid id, string kind, byte value) => new()
    {
        ["_id"] = GuidValue(id), ["Kind"] = kind, ["Payload"] = new BsonBinaryData(new[] { value }),
        ["Version"] = 1, ["MediaType"] = "application/json"
    };

    private static BsonBinaryData GuidValue(Guid id)
        => new(id, GuidRepresentation.Standard);
}
