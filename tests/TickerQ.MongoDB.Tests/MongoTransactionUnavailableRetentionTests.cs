using MongoDB.Driver;
using NSubstitute;
using Testcontainers.MongoDb;
using TickerQ.MongoDB.Indexes;
using TickerQ.MongoDB.Infrastructure;
using TickerQ.MongoDB.Serialization;
using TickerQ.Utilities;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Models;

namespace TickerQ.MongoDB.Tests;

/// <summary>
/// Verifies the fail-closed contract on a MongoDB deployment that does NOT support multi-document
/// transactions (a standalone). Time-chain deletion is all-or-nothing and therefore requires a
/// transaction; when transactions are unavailable it must delete nothing rather than risk a partial
/// chain deletion. Cron-occurrence retention (single atomic delete, no chain) stays functional.
///
/// <para>
/// Testcontainers' MongoDb module initializes a single-node replica set (transactions supported) only
/// when it runs without authentication. Supplying credentials makes it run as a plain standalone with
/// auth, which is exactly the transaction-unavailable topology we need here. This suite therefore uses
/// its own standalone container instead of the shared replica-set fixture.
/// </para>
///
/// <para>Requires Docker; skipped/blocked in environments without a container runtime.</para>
/// </summary>
public sealed class MongoTransactionUnavailableRetentionTests : IAsyncLifetime
{
    private readonly MongoDbContainer _container = new MongoDbBuilder("mongo:7")
        .WithUsername("root")
        .WithPassword("secret")
        .Build();

    private IMongoClient _client = null!;
    private ITickerMongoContext<TimeTickerEntity, CronTickerEntity> _context = null!;
    private TickerMongoPersistenceProvider<TimeTickerEntity, CronTickerEntity> _provider = null!;
    private readonly DateTime _now = new(2025, 6, 15, 12, 0, 0, DateTimeKind.Utc);

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        TickerClassMaps.RegisterOnce<TimeTickerEntity, CronTickerEntity>();

        _client = new MongoClient(_container.GetConnectionString());
        var database = _client.GetDatabase("tickerq_test");
        _context = new TickerMongoContext<TimeTickerEntity, CronTickerEntity>(database, "ticker_");

        var clock = Substitute.For<ITickerClock>();
        clock.UtcNow.Returns(_now);
        var options = new SchedulerOptionsBuilder { NodeIdentifier = "standalone-node" };
        _provider = new TickerMongoPersistenceProvider<TimeTickerEntity, CronTickerEntity>(_context, clock, options);

        var provisioner = new TickerIndexProvisioner<TimeTickerEntity, CronTickerEntity>(_context, _provider);
        await provisioner.StartAsync(CancellationToken.None);
    }

    public async Task DisposeAsync() => await _container.DisposeAsync();

    [Fact]
    public void DurableNodeFinalizationCapabilityIsFalseOnStandalone()
        => Assert.False(_provider.SupportsDurableNodeFinalizationOutbox);

    [Fact]
    public async Task ReconciliationActivation_fails_closed_without_claim_on_standalone()
    {
        Assert.True(_provider.SupportsReconciliationActivationEpoch);
        var exception = await Assert.ThrowsAsync<NotSupportedException>(() =>
            _provider.BeginReconciliationActivationEpochAsync(1));
        Assert.Contains("replica-set transactions", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Activation was not claimed", exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, await _context.StoreMetadata.CountDocumentsAsync(
            FilterDefinition<global::MongoDB.Bson.BsonDocument>.Empty));
    }

    [Fact]
    public async Task DefinitionRevisionAcquisition_FailsClosed_WhenTransactionsUnavailable()
    {
        var cron = new CronTickerEntity
        {
            Id = Guid.NewGuid(), Function = "standalone-revision", Expression = "* * * * *",
            DefinitionRevision = 2, Request = [], IsEnabled = true, CreatedAt = _now, UpdatedAt = _now
        };
        var occurrence = new CronTickerOccurrenceEntity<CronTickerEntity>
        {
            Id = Guid.NewGuid(), CronTickerId = cron.Id, DefinitionRevision = 2,
            ExecutionTime = _now, Status = TickerStatus.Idle, CreatedAt = _now, UpdatedAt = _now
        };
        await _context.CronTickers.InsertOneAsync(cron);
        await _context.CronTickerOccurrences.InsertOneAsync(occurrence);

        Assert.Empty(await _provider.AcquireImmediateCronOccurrencesAsync([occurrence.Id], CancellationToken.None));
        Assert.Null(await _provider.GetEarliestAvailableCronOccurrence([cron.Id], CancellationToken.None));
        Assert.Equal(TickerStatus.Idle,
            (await _context.CronTickerOccurrences.Find(x => x.Id == occurrence.Id).SingleAsync()).Status);
    }

    private DateTime Ago(double days) => _now - TimeSpan.FromDays(days);

    private TimeTickerEntity Node(TickerStatus status, DateTime? executedAt, Guid? parentId = null)
    {
        var entity = new TimeTickerEntity
        {
            Id = Guid.NewGuid(),
            Function = "retention-test",
            Request = Array.Empty<byte>(),
            ExecutionTime = _now.AddHours(-1)
        };
        typeof(TimeTickerEntity).GetProperty(nameof(entity.Status))!.SetValue(entity, status);
        typeof(TimeTickerEntity).GetProperty(nameof(entity.ExecutedAt))!.SetValue(entity, executedAt);
        typeof(TimeTickerEntity).GetProperty(nameof(entity.ParentId))!.SetValue(entity, parentId);
        typeof(TimeTickerEntity).GetProperty(nameof(entity.CreatedAt))!.SetValue(entity, Ago(40));
        typeof(TimeTickerEntity).GetProperty(nameof(entity.UpdatedAt))!.SetValue(entity, Ago(40));
        return entity;
    }

    [Fact]
    public async Task TimeChainDeletion_FailsClosed_WhenTransactionsUnavailable()
    {
        var root = Node(TickerStatus.Done, Ago(10));
        var child = Node(TickerStatus.Done, Ago(10), root.Id);
        await _context.TimeTickers.InsertManyAsync([root, child]);

        var result = await _provider.DeleteEligibleTimeTickerChainsAsync(
            new RetentionCutoffs(Ago(7), null, null, null), 10, RetentionCursor.Start, CancellationToken.None);

        // Fail closed: nothing deleted, both nodes retained, and no partial (orphaned) state.
        Assert.Equal(0, result.Deleted);
        Assert.False(result.HasMore);
        Assert.Equal(2, await _context.TimeTickers.CountDocumentsAsync(FilterDefinition<TimeTickerEntity>.Empty));
    }

    [Fact]
    public async Task ExplicitTimeChainDeletion_FailsClosed_WhenTransactionsUnavailable()
    {
        var root = Node(TickerStatus.Done, Ago(10));
        var child = Node(TickerStatus.Done, Ago(10), root.Id);
        await _context.TimeTickers.InsertManyAsync([root, child]);

        await Assert.ThrowsAsync<NotSupportedException>(() =>
            _provider.RemoveTimeTickers([root.Id], CancellationToken.None));

        Assert.Equal(2, await _context.TimeTickers.CountDocumentsAsync(FilterDefinition<TimeTickerEntity>.Empty));
    }

    [Fact]
    public async Task CronOccurrenceRetention_FailsClosed_WhenTransactionsUnavailable()
    {
        var cron = new CronTickerEntity
        {
            Id = Guid.NewGuid(),
            Function = "retention-cron",
            Expression = "* * * * *",
            Request = Array.Empty<byte>(),
            IsEnabled = true
        };
        await _context.CronTickers.InsertOneAsync(cron);
        await _context.CronTickerOccurrences.InsertOneAsync(new CronTickerOccurrenceEntity<CronTickerEntity>
        {
            Id = Guid.NewGuid(),
            CronTickerId = cron.Id,
            Status = TickerStatus.Done,
            ExecutedAt = Ago(10),
            ExecutionTime = Ago(10),
            CreatedAt = Ago(10),
            UpdatedAt = Ago(10)
        });

        await Assert.ThrowsAsync<NotSupportedException>(() =>
            _provider.DeleteEligibleCronTickerOccurrencesAsync(
                new RetentionCutoffs(Ago(7), null, null, null), 10, CancellationToken.None));

        Assert.Equal(1, await _context.CronTickers.CountDocumentsAsync(FilterDefinition<CronTickerEntity>.Empty));
        Assert.Equal(1, await _context.CronTickerOccurrences.CountDocumentsAsync(
            FilterDefinition<CronTickerOccurrenceEntity<CronTickerEntity>>.Empty));
    }

    [Fact]
    public async Task ExplicitCronOccurrenceDelete_FailsClosed_WhenTransactionsUnavailable()
    {
        var occurrence = new CronTickerOccurrenceEntity<CronTickerEntity>
        {
            Id = Guid.NewGuid(), CronTickerId = Guid.NewGuid(), Status = TickerStatus.Done,
            ExecutedAt = Ago(10), ExecutionTime = Ago(10), CreatedAt = Ago(10), UpdatedAt = Ago(10)
        };
        await _context.CronTickerOccurrences.InsertOneAsync(occurrence);

        await Assert.ThrowsAsync<NotSupportedException>(() =>
            _provider.RemoveCronTickerOccurrences([occurrence.Id], CancellationToken.None));
        Assert.Equal(1, await _context.CronTickerOccurrences.CountDocumentsAsync(
            FilterDefinition<CronTickerOccurrenceEntity<CronTickerEntity>>.Empty));
    }
}
