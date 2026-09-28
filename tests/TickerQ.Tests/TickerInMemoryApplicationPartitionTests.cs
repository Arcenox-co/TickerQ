using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using TickerQ.Provider;
using TickerQ.Utilities;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Models;
using Xunit;

namespace TickerQ.Tests;

[Collection("InMemoryApplicationPartition")]
public sealed class TickerInMemoryApplicationPartitionTests
{
    private sealed class PartitionTimeTicker : TimeTickerEntity<PartitionTimeTicker> { }
    private sealed class PartitionCronTicker : CronTickerEntity { }
    private sealed class AdoptionTimeTicker : TimeTickerEntity<AdoptionTimeTicker> { }
    private sealed class AdoptionCronTicker : CronTickerEntity { }
    private sealed class ConflictTimeTicker : TimeTickerEntity<ConflictTimeTicker> { }
    private sealed class ConflictCronTicker : CronTickerEntity { }

    public TickerInMemoryApplicationPartitionTests()
    {
        TickerInMemoryPersistenceProvider<PartitionTimeTicker, PartitionCronTicker>
            .ResetAllStateForTests();
        TickerInMemoryPersistenceProvider<AdoptionTimeTicker, AdoptionCronTicker>
            .ResetAllStateForTests();
        TickerInMemoryPersistenceProvider<ConflictTimeTicker, ConflictCronTicker>
            .ResetAllStateForTests();
    }

    [Fact]
    public async Task ExplicitLegacyAdoption_MovesOnceAndRejectsDifferentOwner()
    {
        TickerInMemoryPersistenceProvider<AdoptionTimeTicker, AdoptionCronTicker>
            .ResetAllStateForTests();
        var id = Guid.NewGuid();
        var legacy = CreateAdoptionProvider(null);
        await legacy.AddTimeTickers([new AdoptionTimeTicker
        {
            Id = id, Function = "legacy", ExecutionTime = DateTime.UtcNow.AddMinutes(1),
            Status = TickerStatus.Idle, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        }]);
        var target = CreateAdoptionProvider("adoption-owner");
        var adoption = new LegacyRuntimePartitionAdoption(
            new TickerQRuntimePartition("adoption-owner"), 17, legacyWritersDrained: true);

        await target.AdoptLegacyRuntimePartitionAsync(adoption);
        await target.AdoptLegacyRuntimePartitionAsync(adoption);

        Assert.Equal("legacy", (await target.GetTimeTickerById(id))!.Function);
        Assert.Null(await legacy.GetTimeTickerById(id));
        await AssertLegacyWritesFencedAsync(legacy);
        var other = CreateAdoptionProvider("other-owner");
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            other.AdoptLegacyRuntimePartitionAsync(new LegacyRuntimePartitionAdoption(
                new TickerQRuntimePartition("other-owner"), 17, legacyWritersDrained: true)));
    }

    [Fact]
    public async Task LegacyAdoptionConflict_IsAtomicAndRetryable()
    {
        var movableId = Guid.NewGuid();
        var conflictId = Guid.NewGuid();
        var legacy = CreateConflictProvider(null);
        await legacy.AddTimeTickers([NewConflictTicker(movableId, "movable")]);
        await legacy.InsertCronTickers(
            [NewConflictCronTicker(conflictId, "legacy-conflict")], CancellationToken.None);
        var target = CreateConflictProvider("atomic-adopter");
        await target.InsertCronTickers(
            [NewConflictCronTicker(conflictId, "target-conflict")], CancellationToken.None);
        var adoption = new LegacyRuntimePartitionAdoption(
            new TickerQRuntimePartition("atomic-adopter"), 23, legacyWritersDrained: true);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            target.AdoptLegacyRuntimePartitionAsync(adoption));

        Assert.Equal(TickerQRuntimePartition.LegacyGlobal.StorageKey,
            (await legacy.GetTimeTickerById(movableId))!.ApplicationNamespaceKey);
        Assert.Equal(TickerQRuntimePartition.LegacyGlobal.StorageKey,
            (await legacy.GetCronTickerById(conflictId, CancellationToken.None))!.ApplicationNamespaceKey);
        Assert.Null(await target.GetTimeTickerById(movableId));
        Assert.Equal("target-conflict",
            (await target.GetCronTickerById(conflictId, CancellationToken.None))!.Function);

        Assert.Equal(1, await target.RemoveCronTickers([conflictId], CancellationToken.None));
        await target.AdoptLegacyRuntimePartitionAsync(adoption);

        Assert.Equal("movable", (await target.GetTimeTickerById(movableId))!.Function);
        Assert.Equal("legacy-conflict",
            (await target.GetCronTickerById(conflictId, CancellationToken.None))!.Function);
        Assert.Null(await legacy.GetTimeTickerById(movableId));
        Assert.Null(await legacy.GetCronTickerById(conflictId, CancellationToken.None));
    }

    [Fact]
    public async Task LegacyAdoption_BlocksReadersUntilCompleteGraphIsVisible()
    {
        TickerInMemoryPersistenceProvider<AdoptionTimeTicker, AdoptionCronTicker>
            .ResetAllStateForTests();
        var timeId = Guid.NewGuid();
        var cronId = Guid.NewGuid();
        var occurrenceId = Guid.NewGuid();
        var legacy = CreateAdoptionProvider(null);
        await legacy.AddTimeTickers([new AdoptionTimeTicker
        {
            Id = timeId, Function = "legacy-time", ExecutionTime = DateTime.UtcNow.AddMinutes(1),
            Status = TickerStatus.Idle, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        }]);
        await legacy.InsertCronTickers([new AdoptionCronTicker
        {
            Id = cronId, Function = "legacy-cron", Expression = "* * * * *",
            DefinitionRevision = 1, Request = [7],
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        }], CancellationToken.None);
        await legacy.InsertCronTickerOccurrences([new CronTickerOccurrenceEntity<AdoptionCronTicker>
        {
            Id = occurrenceId, CronTickerId = cronId, DefinitionRevision = 1,
            Status = TickerStatus.Idle, ExecutionTime = DateTime.UtcNow.AddMinutes(1),
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        }], CancellationToken.None);
        var target = CreateAdoptionProvider("visibility-adopter");
        var adoption = new LegacyRuntimePartitionAdoption(
            new TickerQRuntimePartition("visibility-adopter"), 24, legacyWritersDrained: true);
        using var readerStarted = new ManualResetEventSlim();
        Task<(AdoptionTimeTicker Time, AdoptionCronTicker Cron, AdoptionTimeTicker[] Query,
            CronTickerOccurrenceEntity<AdoptionCronTicker> Occurrence, byte[] Request)> reader = null;
        TickerInMemoryPersistenceProvider<AdoptionTimeTicker, AdoptionCronTicker>
            .AfterLegacyAdoptionFirstCollectionCopiedForTest = () =>
        {
            reader = Task.Run(async () =>
            {
                readerStarted.Set();
                return (await target.GetTimeTickerById(timeId),
                    await target.GetCronTickerById(cronId, CancellationToken.None),
                    await target.TimeTickersQuery().ToArrayAsync(),
                    await target.GetEarliestAvailableCronOccurrence([cronId]),
                    await target.GetCronTickerOccurrenceRequest(occurrenceId));
            });
            Assert.True(readerStarted.Wait(TimeSpan.FromSeconds(2)));
            Assert.False(reader.Wait(TimeSpan.FromMilliseconds(50)));
        };

        try
        {
            await target.AdoptLegacyRuntimePartitionAsync(adoption);
            var visible = await reader;
            Assert.Equal("legacy-time", visible.Time.Function);
            Assert.Equal("legacy-cron", visible.Cron.Function);
            Assert.Equal(timeId, Assert.Single(visible.Query).Id);
            Assert.Equal(occurrenceId, visible.Occurrence.Id);
            Assert.Equal(new byte[] { 7 }, visible.Request);
        }
        finally
        {
            TickerInMemoryPersistenceProvider<AdoptionTimeTicker, AdoptionCronTicker>
                .AfterLegacyAdoptionFirstCollectionCopiedForTest = null;
        }
    }

    [Fact]
    public async Task LegacyAdoption_FailureAfterFirstCollection_RollsBackAndRemainsRetryable()
    {
        TickerInMemoryPersistenceProvider<AdoptionTimeTicker, AdoptionCronTicker>
            .ResetAllStateForTests();
        var timeId = Guid.NewGuid();
        var cronId = Guid.NewGuid();
        var legacy = CreateAdoptionProvider(null);
        await legacy.AddTimeTickers([new AdoptionTimeTicker
        {
            Id = timeId, Function = "legacy-time", ExecutionTime = DateTime.UtcNow.AddMinutes(1),
            Status = TickerStatus.Idle, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        }]);
        await legacy.InsertCronTickers([new AdoptionCronTicker
        {
            Id = cronId, Function = "legacy-cron", Expression = "* * * * *",
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        }], CancellationToken.None);
        var target = CreateAdoptionProvider("rollback-adopter");
        var adoption = new LegacyRuntimePartitionAdoption(
            new TickerQRuntimePartition("rollback-adopter"), 25, legacyWritersDrained: true);
        TickerInMemoryPersistenceProvider<AdoptionTimeTicker, AdoptionCronTicker>
            .AfterLegacyAdoptionFirstCollectionCopiedForTest = () =>
                throw new InvalidOperationException("injected adoption failure");

        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                target.AdoptLegacyRuntimePartitionAsync(adoption));
            Assert.Null(await target.GetTimeTickerById(timeId));
            Assert.Null(await target.GetCronTickerById(cronId, CancellationToken.None));
            Assert.Equal(TickerQRuntimePartition.LegacyGlobal.StorageKey,
                (await legacy.GetTimeTickerById(timeId))!.ApplicationNamespaceKey);
            Assert.Equal(TickerQRuntimePartition.LegacyGlobal.StorageKey,
                (await legacy.GetCronTickerById(cronId, CancellationToken.None))!.ApplicationNamespaceKey);
            await AssertLegacyWritesFencedAsync(legacy);
        }
        finally
        {
            TickerInMemoryPersistenceProvider<AdoptionTimeTicker, AdoptionCronTicker>
                .AfterLegacyAdoptionFirstCollectionCopiedForTest = null;
        }

        await target.AdoptLegacyRuntimePartitionAsync(adoption);
        Assert.NotNull(await target.GetTimeTickerById(timeId));
        Assert.NotNull(await target.GetCronTickerById(cronId, CancellationToken.None));
        Assert.Null(await legacy.GetTimeTickerById(timeId));
        await AssertLegacyWritesFencedAsync(legacy);
    }

    [Fact]
    public async Task LegacyAdoption_DoesNotBlockCronPublicationInUnrelatedPartition()
    {
        TickerInMemoryPersistenceProvider<AdoptionTimeTicker, AdoptionCronTicker>
            .ResetAllStateForTests();
        var legacy = CreateAdoptionProvider(null);
        await legacy.AddTimeTickers([new AdoptionTimeTicker
        {
            Id = Guid.NewGuid(), Function = "legacy", ExecutionTime = DateTime.UtcNow.AddMinutes(1),
            Status = TickerStatus.Idle, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        }]);
        var target = CreateAdoptionProvider("publication-adopter");
        var unrelated = CreateAdoptionProvider("publication-unrelated");
        Task<CronTickerOccurrenceEntity<AdoptionCronTicker>[]> publication = null;
        TickerInMemoryPersistenceProvider<AdoptionTimeTicker, AdoptionCronTicker>
            .AfterLegacyAdoptionFirstCollectionCopiedForTest = () =>
        {
            publication = Task.Run(async () =>
            {
                var cron = new AdoptionCronTicker
                {
                    Id = Guid.NewGuid(), Function = "unrelated-cron", Expression = "* * * * *",
                    DefinitionRevision = 1, IsEnabled = true,
                    CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
                };
                await unrelated.InsertCronTickers([cron], CancellationToken.None);
                var context = new InternalManagerContext(cron.Id)
                {
                    FunctionName = cron.Function,
                    Expression = cron.Expression,
                    DefinitionRevision = cron.DefinitionRevision
                };
                return await unrelated.QueueCronTickerOccurrences(
                    (DateTime.UtcNow.AddMinutes(1), [context]), CancellationToken.None).ToArrayAsync();
            });
            Assert.True(publication.Wait(TimeSpan.FromSeconds(2)));
        };

        try
        {
            await target.AdoptLegacyRuntimePartitionAsync(new LegacyRuntimePartitionAdoption(
                new TickerQRuntimePartition("publication-adopter"), 26, legacyWritersDrained: true));
            Assert.Single(await publication!);
        }
        finally
        {
            TickerInMemoryPersistenceProvider<AdoptionTimeTicker, AdoptionCronTicker>
                .AfterLegacyAdoptionFirstCollectionCopiedForTest = null;
        }
    }

    [Fact]
    public async Task LegacyWriterStartedDuringAdoption_IsRejectedAfterCutover()
    {
        TickerInMemoryPersistenceProvider<AdoptionTimeTicker, AdoptionCronTicker>
            .ResetAllStateForTests();
        var legacy = CreateAdoptionProvider(null);
        await legacy.AddTimeTickers([new AdoptionTimeTicker
        {
            Id = Guid.NewGuid(), Function = "legacy", ExecutionTime = DateTime.UtcNow.AddMinutes(1),
            Status = TickerStatus.Idle, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        }]);
        var target = CreateAdoptionProvider("late-writer-adopter");
        using var writerStarted = new ManualResetEventSlim();
        Task<int> lateWrite = null;
        TickerInMemoryPersistenceProvider<AdoptionTimeTicker, AdoptionCronTicker>
            .AfterLegacyAdoptionFirstCollectionCopiedForTest = () =>
        {
            lateWrite = Task.Run(async () =>
            {
                writerStarted.Set();
                return await legacy.AddTimeTickers([new AdoptionTimeTicker
                {
                    Id = Guid.NewGuid(), Function = "late", ExecutionTime = DateTime.UtcNow,
                    Status = TickerStatus.Idle, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
                }]);
            });
            Assert.True(writerStarted.Wait(TimeSpan.FromSeconds(2)));
            Assert.False(lateWrite.Wait(TimeSpan.FromMilliseconds(50)));
        };

        try
        {
            await target.AdoptLegacyRuntimePartitionAsync(new LegacyRuntimePartitionAdoption(
                new TickerQRuntimePartition("late-writer-adopter"), 27, legacyWritersDrained: true));
            await Assert.ThrowsAsync<InvalidOperationException>(async () => await lateWrite!);
        }
        finally
        {
            TickerInMemoryPersistenceProvider<AdoptionTimeTicker, AdoptionCronTicker>
                .AfterLegacyAdoptionFirstCollectionCopiedForTest = null;
        }
    }

    [Fact]
    public async Task CronTerminalResult_IsCopiedAtomicallyWhenAdoptionStartsAfterMutation()
    {
        TickerInMemoryPersistenceProvider<AdoptionTimeTicker, AdoptionCronTicker>
            .ResetAllStateForTests();
        var legacy = CreateAdoptionProvider(null);
        var cron = new AdoptionCronTicker
        {
            Id = Guid.NewGuid(), Function = "legacy-cron", Expression = "* * * * *",
            DefinitionRevision = 1, IsEnabled = true,
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        };
        await legacy.InsertCronTickers([cron], CancellationToken.None);
        var occurrence = new CronTickerOccurrenceEntity<AdoptionCronTicker>
        {
            Id = Guid.NewGuid(), CronTickerId = cron.Id, DefinitionRevision = 1,
            ExecutionTime = DateTime.UtcNow, Status = TickerStatus.Idle,
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        };
        await legacy.InsertCronTickerOccurrences([occurrence], CancellationToken.None);
        var acquired = Assert.Single(await legacy.AcquireImmediateCronOccurrencesAsync([occurrence.Id]));
        var target = CreateAdoptionProvider("cron-result-adopter");
        var adoption = new LegacyRuntimePartitionAdoption(
            new TickerQRuntimePartition("cron-result-adopter"), 28, legacyWritersDrained: true);
        Task adoptionTask = null;
        using var adoptionStarted = new ManualResetEventSlim();
        TickerInMemoryPersistenceProvider<AdoptionTimeTicker, AdoptionCronTicker>
            .AfterCronOccurrenceTerminalMutationForTest = () =>
        {
            adoptionTask = Task.Run(async () =>
            {
                adoptionStarted.Set();
                await target.AdoptLegacyRuntimePartitionAsync(adoption);
            });
            Assert.True(adoptionStarted.Wait(TimeSpan.FromSeconds(2)));
            Assert.False(adoptionTask.Wait(TimeSpan.FromMilliseconds(50)));
        };

        try
        {
            var result = new TickerResultEnvelope([9], 1, "application/octet-stream");
            var terminal = new InternalFunctionContext()
                .SetProperty(x => x.TickerId, occurrence.Id)
                .SetProperty(x => x.ParentId, cron.Id)
                .SetProperty(x => x.Type, TickerType.CronTickerOccurrence)
                .SetProperty(x => x.Status, TickerStatus.Done)
                .SetProperty(x => x.AcquisitionToken, acquired.AcquisitionToken)
                .SetProperty(x => x.ResultEnvelope, result);
            await legacy.UpdateCronTickerOccurrence(terminal, CancellationToken.None);
            await adoptionTask!;

            var adopted = Assert.Single(await target.GetAllCronTickerOccurrences(
                row => row.Id == occurrence.Id, CancellationToken.None));
            Assert.Equal(TickerStatus.Done, adopted.Status);
            Assert.Equal([9], (await target.GetCronTickerOccurrenceResultAsync(
                occurrence.Id, CancellationToken.None))!.ToPayloadArray());
            Assert.Null(await legacy.GetCronTickerOccurrenceResultAsync(
                occurrence.Id, CancellationToken.None));
        }
        finally
        {
            TickerInMemoryPersistenceProvider<AdoptionTimeTicker, AdoptionCronTicker>
                .AfterCronOccurrenceTerminalMutationForTest = null;
        }
    }

    [Fact]
    public async Task TimeOnDemandReacquisition_RemovesPriorResultBeforeAdoptionCanCopyGraph()
    {
        TickerInMemoryPersistenceProvider<AdoptionTimeTicker, AdoptionCronTicker>
            .ResetAllStateForTests();
        var id = Guid.NewGuid();
        var legacy = CreateAdoptionProvider(null);
        await legacy.AddTimeTickers([new AdoptionTimeTicker
        {
            Id = id, Function = "legacy-time", ExecutionTime = DateTime.UtcNow,
            Status = TickerStatus.Idle, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        }]);
        var firstRun = Assert.Single(await legacy.AcquireImmediateTimeTickersAsync([id]));
        var firstResult = new TickerResultEnvelope([4], 1, "application/octet-stream");
        var terminal = new InternalFunctionContext()
            .SetProperty(x => x.TickerId, id)
            .SetProperty(x => x.Type, TickerType.TimeTicker)
            .SetProperty(x => x.Status, TickerStatus.Done)
            .SetProperty(x => x.AcquisitionToken, firstRun.AcquisitionToken)
            .SetProperty(x => x.ChainRootId, firstRun.ChainRootId)
            .SetProperty(x => x.ChainGeneration, firstRun.ChainGeneration)
            .SetProperty(x => x.ResultEnvelope, firstResult);
        Assert.Equal(1, await legacy.UpdateTimeTicker(terminal, CancellationToken.None));
        Assert.NotNull(await legacy.GetTimeTickerResultAsync(id, CancellationToken.None));

        var target = CreateAdoptionProvider("time-rerun-adopter");
        var adoption = new LegacyRuntimePartitionAdoption(
            new TickerQRuntimePartition("time-rerun-adopter"), 29, legacyWritersDrained: true);
        Task adoptionTask = null;
        using var adoptionStarted = new ManualResetEventSlim();
        TickerInMemoryPersistenceProvider<AdoptionTimeTicker, AdoptionCronTicker>
            .AfterTimeTickerOnDemandMutationForTest = _ =>
        {
            adoptionTask = Task.Run(async () =>
            {
                adoptionStarted.Set();
                await target.AdoptLegacyRuntimePartitionAsync(adoption);
            });
            Assert.True(adoptionStarted.Wait(TimeSpan.FromSeconds(2)));
            Assert.False(adoptionTask.Wait(TimeSpan.FromMilliseconds(50)));
        };

        try
        {
            var secondRun = await legacy.AcquireTimeTickerOnDemandAsync(
                id, DateTime.UtcNow.AddMinutes(1), CancellationToken.None);
            Assert.NotNull(secondRun);
            await adoptionTask!;

            Assert.Equal(TickerStatus.InProgress, (await target.GetTimeTickerById(id))!.Status);
            Assert.Null(await target.GetTimeTickerResultAsync(id, CancellationToken.None));
            Assert.Null(await legacy.GetTimeTickerById(id));
        }
        finally
        {
            TickerInMemoryPersistenceProvider<AdoptionTimeTicker, AdoptionCronTicker>
                .AfterTimeTickerOnDemandMutationForTest = null;
        }
    }

    [Fact]
    public async Task QueuedAcquisition_IsPublishedBeforeAdoptionCanCutOverPartition()
    {
        TickerInMemoryPersistenceProvider<AdoptionTimeTicker, AdoptionCronTicker>
            .ResetAllStateForTests();
        var id = Guid.NewGuid();
        var legacy = CreateAdoptionProvider(null);
        await legacy.AddTimeTickers([new AdoptionTimeTicker
        {
            Id = id, Function = "legacy-time", ExecutionTime = DateTime.UtcNow.AddMinutes(-2),
            Status = TickerStatus.Idle, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        }]);
        var target = CreateAdoptionProvider("queue-publication-adopter");
        var adoption = new LegacyRuntimePartitionAdoption(
            new TickerQRuntimePartition("queue-publication-adopter"), 30, legacyWritersDrained: true);
        Task adoptionTask = null;
        using var adoptionStarted = new ManualResetEventSlim();
        TickerInMemoryPersistenceProvider<AdoptionTimeTicker, AdoptionCronTicker>
            .AfterAcquisitionMutationForTest = acquiredId =>
        {
            if (acquiredId != id)
                return;
            adoptionTask = Task.Run(async () =>
            {
                adoptionStarted.Set();
                await target.AdoptLegacyRuntimePartitionAsync(adoption);
            });
            Assert.True(adoptionStarted.Wait(TimeSpan.FromSeconds(2)));
            Assert.False(adoptionTask.Wait(TimeSpan.FromMilliseconds(50)));
        };

        try
        {
            var published = await legacy.QueueTimedOutTimeTickers(CancellationToken.None).ToArrayAsync();
            Assert.Equal(id, Assert.Single(published).Id);
            await adoptionTask!;

            var adopted = await target.GetTimeTickerById(id);
            Assert.Equal(TickerStatus.InProgress, adopted!.Status);
            Assert.NotNull(adopted.AcquisitionToken);
            Assert.Null(await legacy.GetTimeTickerById(id));
        }
        finally
        {
            TickerInMemoryPersistenceProvider<AdoptionTimeTicker, AdoptionCronTicker>
                .AfterAcquisitionMutationForTest = null;
        }
    }

    [Fact]
    public async Task IdenticalIds_AreIsolatedAcrossCrudAcquisitionResultsAndRemoval()
    {
        var id = Guid.NewGuid();
        var providerA = CreateProvider("partition-a");
        var providerB = CreateProvider("partition-b");
        await providerA.AddTimeTickers([NewTicker(id, "function-a")]);
        await providerB.AddTimeTickers([NewTicker(id, "function-b")]);

        Assert.Equal("function-a", (await providerA.GetTimeTickerById(id)).Function);
        Assert.Equal("function-b", (await providerB.GetTimeTickerById(id)).Function);

        var acquiredA = Assert.Single(await providerA.AcquireImmediateTimeTickersAsync([id]));
        var acquiredB = Assert.Single(await providerB.AcquireImmediateTimeTickersAsync([id]));
        var envelopeA = new TickerResultEnvelope([1], TickerResultEnvelope.CurrentVersion, "application/octet-stream");
        var completion = new InternalFunctionContext()
            .SetProperty(x => x.TickerId, id)
            .SetProperty(x => x.Type, TickerType.TimeTicker)
            .SetProperty(x => x.Status, TickerStatus.Done)
            .SetProperty(x => x.AcquisitionToken, acquiredA.AcquisitionToken)
            .SetProperty(x => x.ChainRootId, id)
            .SetProperty(x => x.ChainGeneration, acquiredA.ChainGeneration)
            .SetProperty(x => x.ResultEnvelope, envelopeA);
        completion.RuntimePartitionKey = new TickerQRuntimePartition("partition-a").StorageKey;
        Assert.True(await providerA.CommitSuccessfulTickerAsync(completion));

        Assert.Same(envelopeA, await providerA.GetTimeTickerResultAsync(id));
        Assert.Null(await providerB.GetTimeTickerResultAsync(id));
        Assert.Equal(TickerStatus.InProgress, (await providerB.GetTimeTickerById(id)).Status);

        Assert.Equal(1, await providerA.RemoveTimeTickers([id]));
        Assert.Null(await providerA.GetTimeTickerById(id));
        Assert.NotNull(await providerB.GetTimeTickerById(id));
        Assert.Equal(acquiredB.AcquisitionToken, (await providerB.GetTimeTickerById(id)).AcquisitionToken);
    }

    [Fact]
    public async Task ActivationAndRepairInA_DoNotFenceMutateOrBlockB()
    {
        var providerA = CreateProvider("repair-a", schedulerEnabled: true);
        var providerB = CreateProvider("repair-b");
        var id = Guid.NewGuid();
        await providerB.AddTimeTickers([NewTicker(id, "function-b")]);

        await providerA.BeginReconciliationActivationEpochAsync(
            new ReconciliationActivationScope("repair-a"), 1);
        Assert.Single(await providerB.AcquireImmediateTimeTickersAsync([id]));
        await providerA.CommitReconciliationActivationEpochAsync(
            new ReconciliationActivationScope("repair-a"), 1);

        var entered = new ManualResetEventSlim();
        var release = new ManualResetEventSlim();
        var hook = typeof(TickerInMemoryPersistenceProvider<PartitionTimeTicker, PartitionCronTicker>)
            .GetField("DuringGlobalRepairMutationHook", BindingFlags.Static | BindingFlags.NonPublic)!;
        hook.SetValue(null, (Action)(() => { entered.Set(); release.Wait(); }));
        try
        {
            var repair = Task.Run(() => providerA.RepairTimeTickerChainsAsync());
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
            var readB = Task.Run(() => providerB.GetTimeTickerById(id));
            Assert.Same(await readB.WaitAsync(TimeSpan.FromSeconds(2)), await readB);
            release.Set();
            await repair;
        }
        finally
        {
            release.Set();
            hook.SetValue(null, null);
        }
    }

    private static TickerInMemoryPersistenceProvider<PartitionTimeTicker, PartitionCronTicker> CreateProvider(
        string applicationNamespace, bool schedulerEnabled = false)
    {
        var options = new SchedulerOptionsBuilder { ReconciliationEpoch = 1 };
        options.BindRuntimeActivationScope(applicationNamespace, 1, schedulerEnabled);
        var services = new ServiceCollection().AddSingleton(options).BuildServiceProvider();
        return new TickerInMemoryPersistenceProvider<PartitionTimeTicker, PartitionCronTicker>(services);
    }

    private static PartitionTimeTicker NewTicker(Guid id, string function) => new()
    {
        Id = id,
        Function = function,
        ExecutionTime = DateTime.UtcNow.AddMinutes(1),
        Status = TickerStatus.Idle,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    };

    private static TickerInMemoryPersistenceProvider<AdoptionTimeTicker, AdoptionCronTicker>
        CreateAdoptionProvider(string? applicationNamespace)
    {
        var options = new SchedulerOptionsBuilder { ReconciliationEpoch = 1 };
        options.BindRuntimeActivationScope(applicationNamespace, 1, false);
        var services = new ServiceCollection().AddSingleton(options).BuildServiceProvider();
        return new TickerInMemoryPersistenceProvider<AdoptionTimeTicker, AdoptionCronTicker>(services);
    }

    private static TickerInMemoryPersistenceProvider<ConflictTimeTicker, ConflictCronTicker>
        CreateConflictProvider(string? applicationNamespace)
    {
        var options = new SchedulerOptionsBuilder { ReconciliationEpoch = 1 };
        options.BindRuntimeActivationScope(applicationNamespace, 1, false);
        var services = new ServiceCollection().AddSingleton(options).BuildServiceProvider();
        return new TickerInMemoryPersistenceProvider<ConflictTimeTicker, ConflictCronTicker>(services);
    }

    private static async Task AssertLegacyWritesFencedAsync(
        TickerInMemoryPersistenceProvider<AdoptionTimeTicker, AdoptionCronTicker> legacy)
    {
        var cronId = Guid.NewGuid();
        await Assert.ThrowsAsync<InvalidOperationException>(() => legacy.AddTimeTickers([
            new AdoptionTimeTicker
            {
                Id = Guid.NewGuid(), Function = "late-time", ExecutionTime = DateTime.UtcNow,
                Status = TickerStatus.Idle, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
            }]));
        await Assert.ThrowsAsync<InvalidOperationException>(() => legacy.InsertCronTickers([
            new AdoptionCronTicker
            {
                Id = cronId, Function = "late-cron", Expression = "* * * * *",
                CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
            }], CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() => legacy.InsertCronTickerOccurrences([
            new CronTickerOccurrenceEntity<AdoptionCronTicker>
            {
                Id = Guid.NewGuid(), CronTickerId = cronId, ExecutionTime = DateTime.UtcNow,
                Status = TickerStatus.Idle, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
            }], CancellationToken.None));
    }

    private static ConflictTimeTicker NewConflictTicker(Guid id, string function) => new()
    {
        Id = id,
        Function = function,
        ExecutionTime = DateTime.UtcNow.AddMinutes(1),
        Status = TickerStatus.Idle,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    };

    private static ConflictCronTicker NewConflictCronTicker(Guid id, string function) => new()
    {
        Id = id,
        Function = function,
        Expression = "*/5 * * * *",
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    };
}

[CollectionDefinition("InMemoryApplicationPartition", DisableParallelization = true)]
public sealed class InMemoryApplicationPartitionCollection;
