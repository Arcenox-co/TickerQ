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

    [Fact]
    public async Task ExplicitLegacyAdoption_MovesOnceAndRejectsDifferentOwner()
    {
        var id = Guid.NewGuid();
        var legacy = CreateAdoptionProvider(null);
        await legacy.AddTimeTickers([new AdoptionTimeTicker
        {
            Id = id, Function = "legacy", ExecutionTime = DateTime.UtcNow.AddMinutes(1),
            Status = TickerStatus.Idle, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        }]);
        var target = CreateAdoptionProvider("adoption-owner");
        var adoption = new LegacyRuntimePartitionAdoption(
            new TickerQRuntimePartition("adoption-owner"), 17);

        await target.AdoptLegacyRuntimePartitionAsync(adoption);
        await target.AdoptLegacyRuntimePartitionAsync(adoption);

        Assert.Equal("legacy", (await target.GetTimeTickerById(id))!.Function);
        Assert.Null(await legacy.GetTimeTickerById(id));
        var other = CreateAdoptionProvider("other-owner");
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            other.AdoptLegacyRuntimePartitionAsync(new LegacyRuntimePartitionAdoption(
                new TickerQRuntimePartition("other-owner"), 17)));
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
}

[CollectionDefinition("InMemoryApplicationPartition", DisableParallelization = true)]
public sealed class InMemoryApplicationPartitionCollection;
