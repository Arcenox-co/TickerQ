using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TickerQ.BackgroundServices;
using TickerQ.DependencyInjection;
using TickerQ.Utilities;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Interfaces.Managers;
using TickerQ.Utilities.Models;
using Xunit;

namespace TickerQ.Tests;

[Collection("TickerFunctionProviderState")]
public sealed class RuntimeScopeAndManifestHardeningTests : IDisposable
{
    [Fact]
    public async Task InMemory_RuntimeAdmission_IsBoundToConfiguredScope_BeforeAnyProtocolCall()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTickerQ(options => options
            .UseDefinedCronApplicationNamespace("app-a")
            .UseReconciliationEpoch(7));
        await using var serviceProvider = services.BuildServiceProvider();
        var provider = serviceProvider.GetRequiredService<ITickerPersistenceProvider<TimeTickerEntity, CronTickerEntity>>();
        var appA = new ReconciliationActivationScope("app-a");
        var appB = new ReconciliationActivationScope("app-b");

        await provider.BeginReconciliationActivationEpochAsync(appB, 7, CancellationToken.None);
        await provider.CommitReconciliationActivationEpochAsync(appB, 7, CancellationToken.None);

        Assert.Equal(0, await provider.AddTimeTickers([NewTimeTicker()], CancellationToken.None));

        await provider.BeginReconciliationActivationEpochAsync(appA, 7, CancellationToken.None);
        await provider.CommitReconciliationActivationEpochAsync(appA, 7, CancellationToken.None);

        Assert.Equal(1, await provider.AddTimeTickers([NewTimeTicker()], CancellationToken.None));
        Assert.Equal(ActivationEpochPhase.Activated,
            (await provider.GetReconciliationActivationStateAsync(appB, CancellationToken.None)).Phase);
    }

    [Fact]
    public async Task InMemory_ActivatingEpoch_AdmitsOnlyMatchingStartupSeederCapability()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTickerQ(options => options
            .UseDefinedCronApplicationNamespace("startup-app")
            .UseReconciliationEpoch(9));
        await using var serviceProvider = services.BuildServiceProvider();
        var provider = serviceProvider.GetRequiredService<ITickerPersistenceProvider<TimeTickerEntity, CronTickerEntity>>();
        var scope = new ReconciliationActivationScope("startup-app");
        await provider.BeginReconciliationActivationEpochAsync(scope, 9, CancellationToken.None);

        Assert.Equal(0, await provider.AddTimeTickers([NewTimeTicker()], CancellationToken.None));

        using (StartupSeederAdmissionContext.Enter(scope.ScopeKey, 8))
            Assert.Equal(0, await provider.AddTimeTickers([NewTimeTicker()], CancellationToken.None));
        using (StartupSeederAdmissionContext.Enter(new ReconciliationActivationScope("other").ScopeKey, 9))
            Assert.Equal(0, await provider.AddTimeTickers([NewTimeTicker()], CancellationToken.None));
        using (StartupSeederAdmissionContext.Enter(scope.ScopeKey, 9))
            Assert.Equal(1, await provider.AddTimeTickers([NewTimeTicker()], CancellationToken.None));

        Assert.Equal(0, await provider.AddTimeTickers([NewTimeTicker()], CancellationToken.None));
    }

    [Fact]
    public async Task InMemory_InitializerSeeder_PublishesBeforeActivationCommit()
    {
        ResetFunctionProvider();
        const string function = "startup-seed";
        TickerFunctionProvider.RegisterFunctions(new Dictionary<string,
            (string, Utilities.Enums.TickerTaskPriority, TickerFunctionDelegate, int)>
        {
            [function] = (string.Empty, Utilities.Enums.TickerTaskPriority.Normal,
                (_, _, _) => Task.CompletedTask, 0)
        });
        var id = Guid.NewGuid();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddTickerQ(options => options
            .UseDefinedCronApplicationNamespace("startup-host")
            .UseReconciliationEpoch(12)
            .UseTickerSeeder(async manager =>
            {
                var result = await manager.AddAsync(new TimeTickerEntity
                {
                    Id = id,
                    Function = function,
                    ExecutionTime = DateTime.UtcNow.AddMinutes(1)
                });
                Assert.True(result.IsSucceeded, result.Exception?.ToString());
            }));
        await using var serviceProvider = services.BuildServiceProvider();
        var initializer = serviceProvider.GetRequiredService<TickerQInitializerHostedService>();
        initializer.InitializationRequested = true;

        await initializer.StartAsync(CancellationToken.None);

        var provider = serviceProvider.GetRequiredService<
            ITickerPersistenceProvider<TimeTickerEntity, CronTickerEntity>>();
        Assert.NotNull(await provider.GetTimeTickerById(id, CancellationToken.None));
        var state = await provider.GetReconciliationActivationStateAsync(
            new ReconciliationActivationScope("startup-host"), CancellationToken.None);
        Assert.Equal(ActivationEpochPhase.Activated, state.Phase);
        Assert.Equal(12, state.Epoch);
    }

    [Fact]
    public async Task InMemory_AuthoritativeManifest_RequiresBoundScopeAndStartupCapability()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTickerQ(options => options
            .UseDefinedCronApplicationNamespace("manifest-a")
            .UseReconciliationEpoch(15));
        await using var serviceProvider = services.BuildServiceProvider();
        var provider = serviceProvider.GetRequiredService<
            ITickerPersistenceProvider<TimeTickerEntity, CronTickerEntity>>();
        var scope = new ReconciliationActivationScope("manifest-a");
        await provider.BeginReconciliationActivationEpochAsync(scope, 15, CancellationToken.None);
        var seed = new DefinedCronTickerSeed("manifest-job", "*/5 * * * *");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            provider.MigrateDefinedCronTickers(
                new DefinedCronSeedManifest("manifest-b", [seed]), CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            provider.MigrateDefinedCronTickers(
                new DefinedCronSeedManifest("manifest-a", [seed]), CancellationToken.None));

        using (StartupSeederAdmissionContext.Enter(scope.ScopeKey, 15))
            await provider.MigrateDefinedCronTickers(
                new DefinedCronSeedManifest("manifest-a", [seed]), CancellationToken.None);

        Assert.Single(await provider.GetCronTickers(
            x => x.Function == "manifest-job", CancellationToken.None));
    }

    [Fact]
    public async Task SchedulerManagers_FailClosedBeforeHostStart_ButQueueOnlyTypedManagersRemainUsable()
    {
        var schedulerServices = new ServiceCollection();
        schedulerServices.AddLogging();
        schedulerServices.AddTickerQ(options => options
            .UseDefinedCronApplicationNamespace("manager-app")
            .UseReconciliationEpoch(3));
        await using var schedulerProvider = schedulerServices.BuildServiceProvider();

        var schedulerResult = await schedulerProvider.GetRequiredService<ITimeTickerManager<TimeTickerEntity>>()
            .AddAsync(NewTimeTicker());
        Assert.False(schedulerResult.IsSucceeded);

        var producerServices = new ServiceCollection();
        producerServices.AddLogging();
        const string producerFunction = "queue-only-pre-start";
        ResetFunctionProvider();
        TickerFunctionProvider.RegisterFunctions(new Dictionary<string,
            (string, Utilities.Enums.TickerTaskPriority, TickerFunctionDelegate, int)>
        {
            [producerFunction] = (string.Empty, Utilities.Enums.TickerTaskPriority.Normal,
                (_, _, _) => Task.CompletedTask, 0)
        });
        producerServices.AddTickerQ(options => options.DisableBackgroundServices());
        await using var producerProvider = producerServices.BuildServiceProvider();

        var timeResult = await producerProvider.GetRequiredService<ITimeTickerManager<TimeTickerEntity>>()
            .AddAsync(new TimeTickerEntity
            {
                Function = producerFunction,
                ExecutionTime = DateTime.UtcNow.AddMinutes(1)
            });
        var cronResult = await producerProvider.GetRequiredService<ICronTickerManager<CronTickerEntity>>()
            .AddAsync(new CronTickerEntity
            {
                Function = producerFunction,
                Expression = "*/5 * * * *"
            });

        Assert.True(timeResult.IsSucceeded, timeResult.Exception?.ToString());
        Assert.True(cronResult.IsSucceeded, cronResult.Exception?.ToString());
    }

    [Fact]
    public void SchedulerRuntimeScopeBinding_IsImmutableAndRejectsConflict()
    {
        var options = new SchedulerOptionsBuilder();
        options.BindRuntimeActivationScope(" app-a ", 4, schedulerEnabled: true);
        options.BindRuntimeActivationScope("app-a", 4, schedulerEnabled: true);

        Assert.Throws<InvalidOperationException>(() =>
            options.BindRuntimeActivationScope("app-b", 4, schedulerEnabled: true));
        Assert.Equal("app-a", options.RuntimeActivationScope.ApplicationNamespace);
        Assert.Equal(4, options.RuntimeActivationEpoch);
    }

    [Fact]
    public void RuntimePartition_IsCanonicalBoundedAndDeterministic()
    {
        var first = new TickerQRuntimePartition("  orders-api  ");
        var second = new TickerQRuntimePartition("orders-api");

        Assert.Equal("orders-api", first.ApplicationNamespace);
        Assert.Equal(first.StorageKey, second.StorageKey);
        Assert.Equal(new TickerQRuntimePartition("café").StorageKey,
            new TickerQRuntimePartition("cafe\u0301").StorageKey);
        Assert.StartsWith("tq:runtime:v1:", first.StorageKey, StringComparison.Ordinal);
        Assert.NotEqual(first.StorageKey, new TickerQRuntimePartition("orders-api-v2").StorageKey);
        Assert.Throws<ArgumentException>(() => new TickerQRuntimePartition(new string('é', 257)));
    }

    [Fact]
    public void RuntimePartition_LegacyGlobal_IsExplicitAndCannotBeConstructedFromMissingNamespace()
    {
        Assert.True(TickerQRuntimePartition.LegacyGlobal.IsLegacyGlobal);
        Assert.Null(TickerQRuntimePartition.LegacyGlobal.ApplicationNamespace);
        Assert.Throws<ArgumentException>(() => new TickerQRuntimePartition("   "));
    }

    [Fact]
    public void RuntimeBinding_RequiresSchedulerNamespace_ButAllowsExplicitQueueOnlyLegacyPartition()
    {
        var scheduler = new SchedulerOptionsBuilder();
        Assert.Throws<InvalidOperationException>(() =>
            scheduler.BindRuntimeActivationScope(null, 1, schedulerEnabled: true));

        var producer = new SchedulerOptionsBuilder();
        producer.BindRuntimeActivationScope(null, 0, schedulerEnabled: false);
        Assert.Same(TickerQRuntimePartition.LegacyGlobal, producer.RuntimePartition);
        Assert.Null(producer.RuntimeActivationScope);
    }

    [Fact]
    public void AddTickerQ_FreezesNamespaceModeAndEpochAgainstLaterBuilderMutation()
    {
        TickerOptionsBuilder<TimeTickerEntity, CronTickerEntity> captured = null;
        var services = new ServiceCollection();
        services.AddTickerQ(options =>
        {
            captured = options;
            options.UseDefinedCronApplicationNamespace("immutable-app")
                .UseReconciliationEpoch(8);
        });

        Assert.Throws<InvalidOperationException>(() =>
            captured.UseDefinedCronApplicationNamespace("redirected-app"));
        Assert.Throws<InvalidOperationException>(() => captured.UseReconciliationEpoch(9));
        Assert.Throws<InvalidOperationException>(() => captured.DisableBackgroundServices());

        using var provider = services.BuildServiceProvider();
        var snapshot = provider.GetRequiredService<SchedulerOptionsBuilder>();
        Assert.Equal("immutable-app", snapshot.RuntimePartition.ApplicationNamespace);
        Assert.Equal(8, snapshot.RuntimeActivationEpoch);
        Assert.True(snapshot.RuntimeSchedulerEnabled);
    }

    [Fact]
    public void SchedulerRuntimeEpoch_CannotBeMutatedAfterBinding()
    {
        var options = new SchedulerOptionsBuilder { ReconciliationEpoch = 4 };
        options.BindRuntimeActivationScope("app-a", 4, schedulerEnabled: true);

        Assert.Throws<InvalidOperationException>(() => options.ReconciliationEpoch = 99);
        Assert.Equal(4, options.ReconciliationEpoch);
        Assert.Equal(4, options.RuntimeActivationEpoch);
        Assert.Equal("app-a", options.RuntimeActivationScope.ApplicationNamespace);
    }

    [Fact]
    public void DirectManifest_TakesDeepCanonicalSnapshot()
    {
        var retryIntervals = new[] { 5, 10 };
        var seeds = new[]
        {
            new DefinedCronTickerSeed("  ProcessOrders  ", "*/5 * * * *", stableDefinitionId: "  orders-v1  ",
                retryIntervals: retryIntervals)
        };
        var mappings = new Dictionary<string, string> { ["  ProcessOrders  "] = "  orders-api  " };

        var manifest = new DefinedCronSeedManifest("  orders-api  ", seeds, mappings);
        retryIntervals[0] = 999;
        seeds[0] = new DefinedCronTickerSeed("mutated", "* * * * *");
        mappings.Clear();

        var seed = Assert.Single(manifest.Seeds);
        Assert.Equal("orders-api", manifest.ApplicationNamespace);
        Assert.Equal("ProcessOrders", seed.Function);
        Assert.Equal("orders-v1", seed.StableDefinitionId);
        Assert.Equal("0 */5 * * * *", seed.Expression);
        Assert.Equal(new[] { 5, 10 }, seed.RetryIntervals);
        Assert.Equal("orders-api", manifest.LegacyOwnershipByFunction["ProcessOrders"]);
        Assert.Contains("ProcessOrders", manifest.DesiredSeedFunctions);
        Assert.False(manifest.IsOrphanedSeedKey(manifest.SeedKeyFor(seed)));

        seed.RetryIntervals[0] = 777;
        Assert.Equal(new[] { 5, 10 }, Assert.Single(manifest.Seeds).RetryIntervals);
        Assert.Throws<NotSupportedException>(() =>
            ((IDictionary<string, string>)manifest.LegacyOwnershipByFunction).Add("other", "owner"));
    }

    [Fact]
    public void DirectManifest_RejectsNormalizedDuplicateMappingsAndSeedIdentities()
    {
        Assert.Throws<ArgumentException>(() => new DefinedCronSeedManifest("app", Array.Empty<DefinedCronTickerSeed>(),
            new Dictionary<string, string>
            {
                ["ProcessOrders"] = "app",
                [" ProcessOrders "] = "other-app"
            }));

        Assert.Throws<ArgumentException>(() => new DefinedCronSeedManifest("app",
        [
            new DefinedCronTickerSeed("one", "* * * * *", stableDefinitionId: "same"),
            new DefinedCronTickerSeed("two", "*/2 * * * *", stableDefinitionId: " same ")
        ]));
    }

    [Fact]
    public void DirectManifest_RejectsNormalizedDuplicateFunctionWithDifferentStableIdentity()
    {
        var error = Assert.Throws<ArgumentException>(() => new DefinedCronSeedManifest("app",
        [
            new DefinedCronTickerSeed("job", "* * * * *", stableDefinitionId: "stable-a"),
            new DefinedCronTickerSeed(" job ", "* * * * *", stableDefinitionId: "stable-b")
        ]));

        Assert.Contains("function", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DirectManifest_CollapsesCanonicallyEquivalentSeedsAndMappings()
    {
        var manifest = new DefinedCronSeedManifest("app",
        [
            new DefinedCronTickerSeed(" job ", "*/5 * * * *", 2, " sha256:v2 ",
                stableDefinitionId: " stable ", retries: 1, retryIntervals: [0], timeoutSeconds: 0),
            new DefinedCronTickerSeed("job", "0 */5 * * * *", 2, "sha256:v2",
                stableDefinitionId: "stable", retries: 1, retryIntervals: [0], timeoutSeconds: -1)
        ], new Dictionary<string, string>
        {
            [" job "] = " app ",
            ["job"] = "app"
        });

        var seed = Assert.Single(manifest.Seeds);
        Assert.Equal("0 */5 * * * *", seed.Expression);
        Assert.Equal("sha256:v2", seed.RequestContractFingerprint);
        Assert.Equal(0, seed.TimeoutSeconds);
        Assert.Single(manifest.LegacyOwnershipByFunction);
    }

    [Fact]
    public void DirectManifest_RejectsInvalidCronAndContractIdentity()
    {
        Assert.Throws<ArgumentException>(() => new DefinedCronSeedManifest("app",
            [new DefinedCronTickerSeed("job", "not-a-cron")]));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DefinedCronSeedManifest("app",
            [new DefinedCronTickerSeed("job", "* * * * *", requestContractVersion: 0)]));
        Assert.Throws<ArgumentException>(() => new DefinedCronSeedManifest("app",
            [new DefinedCronTickerSeed("job", "* * * * *", requestContractFingerprint: "sha256:orphan")]));
        Assert.Throws<ArgumentException>(() => new DefinedCronSeedManifest("app",
            [new DefinedCronTickerSeed("job", "* * * * *", requestContractVersion: 1,
                requestContractFingerprint: "  ")]));
    }

    [Fact]
    public void DirectManifest_AcceptsExactExecutablePolicyBoundaries()
    {
        var manifest = new DefinedCronSeedManifest("app",
        [
            new DefinedCronTickerSeed("job", "* * * * *",
                retries: DefinedCronExecutionLimits.MaxRetries,
                retryIntervals: [DefinedCronExecutionLimits.MaxRetryIntervalSeconds],
                timeoutSeconds: DefinedCronExecutionLimits.MaxTimeoutSeconds)
        ]);

        var seed = Assert.Single(manifest.Seeds);
        Assert.Equal(DefinedCronExecutionLimits.MaxRetries, seed.Retries);
        Assert.Equal([DefinedCronExecutionLimits.MaxRetryIntervalSeconds], seed.RetryIntervals);
        Assert.Equal(DefinedCronExecutionLimits.MaxTimeoutSeconds, seed.TimeoutSeconds);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(int.MaxValue)]
    public void DirectManifest_RejectsRetriesOutsideExecutableBounds(int retries)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new DefinedCronSeedManifest("app",
            [new DefinedCronTickerSeed("job", "* * * * *", retries: retries)]));
    }

    [Fact]
    public void DirectManifest_RejectsRetryIntervalsOutsideExecutableBounds()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new DefinedCronSeedManifest("app",
            [new DefinedCronTickerSeed("job", "* * * * *", retries: 1, retryIntervals: [-1])]));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DefinedCronSeedManifest("app",
            [new DefinedCronTickerSeed("job", "* * * * *", retries: 1,
                retryIntervals: [DefinedCronExecutionLimits.MaxRetryIntervalSeconds + 1])]));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DefinedCronSeedManifest("app",
            [new DefinedCronTickerSeed("job", "* * * * *", retries: 1,
                retryIntervals: [int.MaxValue])]));
    }

    [Fact]
    public void DirectManifest_RejectsTimeoutAboveExecutableBound()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new DefinedCronSeedManifest("app",
            [new DefinedCronTickerSeed("job", "* * * * *",
                timeoutSeconds: DefinedCronExecutionLimits.MaxTimeoutSeconds + 1)]));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DefinedCronSeedManifest("app",
            [new DefinedCronTickerSeed("job", "* * * * *", timeoutSeconds: int.MaxValue)]));
    }

    [Fact]
    public void DirectManifest_ValidatesEveryCanonicalIdentityPartByUtf8Bytes()
    {
        var oversized = new string('é', 257);
        Assert.Throws<ArgumentException>(() =>
            new DefinedCronSeedManifest(oversized, Array.Empty<DefinedCronTickerSeed>()));
        Assert.Throws<ArgumentException>(() =>
            new DefinedCronSeedManifest("app", [new DefinedCronTickerSeed(oversized, "* * * * *")]));
        Assert.Throws<ArgumentException>(() =>
            new DefinedCronSeedManifest("app", [new DefinedCronTickerSeed("fn", "* * * * *", stableDefinitionId: oversized)]));
        Assert.Throws<ArgumentException>(() =>
            new DefinedCronSeedManifest("app", Array.Empty<DefinedCronTickerSeed>(),
                new Dictionary<string, string> { ["fn"] = oversized }));
    }

    private static TimeTickerEntity NewTimeTicker() => new()
    {
        Id = Guid.NewGuid(),
        Function = "scope-test",
        ExecutionTime = DateTime.UtcNow.AddMinutes(1)
    };

    public void Dispose() => ResetFunctionProvider();

    private static void ResetFunctionProvider()
    {
        var type = typeof(TickerFunctionProvider);
        const BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;
        type.GetField("TickerFunctions", flags)!.SetValue(null,
            System.Collections.Frozen.FrozenDictionary<string,
                (string, Utilities.Enums.TickerTaskPriority, TickerFunctionDelegate, int)>.Empty);
        type.GetField("TickerFunctionRequestTypes", flags)!.SetValue(null,
            System.Collections.Frozen.FrozenDictionary<string, (string, Type)>.Empty);
        type.GetField("TickerFunctionRequestInfos", flags)!.SetValue(null,
            System.Collections.Frozen.FrozenDictionary<string, (string, string)>.Empty);
        type.GetField("_snapshot", flags)!.SetValue(null, TickerFunctionRegistrySnapshot.Empty);
        type.GetField("_functionRegistrations", flags)!.SetValue(null, null);
        type.GetField("_requestTypeRegistrations", flags)!.SetValue(null, null);
        type.GetField("_requestInfoRegistrations", flags)!.SetValue(null, null);
        type.GetField("_runtimeRequestRegistrations", flags)!.SetValue(null, null);
        type.GetField("_runtimeResultRegistrations", flags)!.SetValue(null, null);
        ((System.Collections.IList)type.GetField("_pendingDescriptors", flags)!.GetValue(null)!).Clear();
        type.GetProperty("IsBuilt", flags)!.SetValue(null, false);
    }
}
