using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using TickerQ.BackgroundServices;
using TickerQ.Utilities;
using TickerQ.Utilities.Base;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Interfaces.Managers;
using TickerQ.Utilities.Models;
using Xunit;

namespace TickerQ.Tests;

/// <summary>
/// Policy tests for how the startup initializer projects code-defined crons into
/// <see cref="DefinedCronTickerSeed"/>s (typed-request-contracts). A code-defined cron seeds an empty
/// payload, so request-required contracts become non-seedable cleanup commands (an empty payload can
/// never satisfy them), while request-less and optional-typed contracts carry authoritative identity.
/// </summary>
[Collection("TickerFunctionProviderState")]
public class DefinedCronSeedingPolicyTests : IDisposable
{
    private const string SchemaJson =
        "{\"type\":\"object\",\"properties\":{\"OrderId\":{\"type\":\"string\"}},\"required\":[\"OrderId\"]}";

    public DefinedCronSeedingPolicyTests() => ResetProvider();

    public void Dispose() => ResetProvider();

    [Fact]
    public void Defined_cron_seed_retains_exact_legacy_five_parameter_constructor()
    {
        var constructor = typeof(DefinedCronTickerSeed).GetConstructor(
            [typeof(string), typeof(string), typeof(int?), typeof(string), typeof(bool)]);

        Assert.NotNull(constructor);
        var seed = (DefinedCronTickerSeed)constructor!.Invoke(
            new object?[] { "legacy", "*/5 * * * *", 3, "sha256:legacy", false });
        Assert.Equal("legacy", seed.StableDefinitionId);
        Assert.Equal(0, seed.Retries);
        Assert.Null(seed.RetryIntervals);
        Assert.Null(seed.TimeoutSeconds);
    }

    [Fact]
    public async Task Seeding_BlocksRequired_And_CarriesIdentity_ForRequestlessAndOptional()
    {
        RegisterFunction("Reqless");
        RegisterFunction("Optional");
        RegisterFunction("Required");
        RegisterDescriptors(
            new TickerFunctionDescriptor("Reqless", TickerTaskPriority.Normal, "* * * * *"),
            new TickerFunctionDescriptor("Optional", TickerTaskPriority.Normal, "* * * * *", 1,
                new TickerRequestContract("Sample.Opt", required: false, schemaJson: SchemaJson)),
            new TickerFunctionDescriptor("Required", TickerTaskPriority.Normal, "* * * * *", 1,
                new TickerRequestContract("Sample.Req", required: true, schemaJson: SchemaJson)));

        var internalManager = Substitute.For<IInternalTickerManager>();
        DefinedCronTickerSeed[] captured = null;
        internalManager
            .When(m => m.MigrateDefinedCronTickers(Arg.Any<DefinedCronSeedManifest>(), Arg.Any<CancellationToken>()))
            .Do(ci => captured = ci.Arg<DefinedCronSeedManifest>().Seeds.ToArray());

        await RunInitializer(internalManager);

        Assert.NotNull(captured);
        var byFunction = captured.ToDictionary(s => s.Function);

        // Required is retained only as a cleanup command so providers can remove a previously seeded row.
        Assert.Equal(3, captured.Length);
        var required = byFunction["Required"];
        Assert.False(required.CanSeed);
        Assert.Equal(TickerFunctionProvider.TickerFunctionDescriptors["Required"].Request!.Fingerprint,
            required.RequestContractFingerprint);

        // Request-less carries the authoritative version with a null fingerprint.
        var reqless = byFunction["Reqless"];
        Assert.True(reqless.CanSeed);
        Assert.Equal(1, reqless.RequestContractVersion);
        Assert.Null(reqless.RequestContractFingerprint);

        // Optional-typed carries version + the descriptor's authoritative fingerprint.
        var optionalDescriptor = TickerFunctionProvider.TickerFunctionDescriptors["Optional"];
        var optional = byFunction["Optional"];
        Assert.True(optional.CanSeed);
        Assert.Equal(optionalDescriptor.ContractVersion, optional.RequestContractVersion);
        Assert.False(string.IsNullOrEmpty(optional.RequestContractFingerprint));
        Assert.Equal(optionalDescriptor.Request!.Fingerprint, optional.RequestContractFingerprint);
    }

    [Fact]
    public void SeedIdentity_IsNamespaced_AndStableAcrossSemanticChanges()
    {
        var appA = CronSeedIdentity.SeedKey("orders", "Reqless");
        var appB = CronSeedIdentity.SeedKey("billing", "Reqless");

        Assert.NotEqual(appA, appB);
        Assert.Equal(appA, CronSeedIdentity.SeedKey("orders", "Reqless"));
        Assert.Throws<ArgumentException>(() => CronSeedIdentity.SeedKey(" ", "Reqless"));
    }

    [Fact]
    public void SeedIdentity_UsesBoundedFixedSizeInjectiveFraming_AndExposesLegacyAdoptionKeys()
    {
        var left = CronSeedIdentity.SeedKey("a:b", "c");
        var right = CronSeedIdentity.SeedKey("a", "b:c");

        Assert.NotEqual(left, right);
        Assert.Equal(CronSeedIdentity.PersistedKeyLength, left.Length);
        Assert.StartsWith("tq:cron-seed:v2:", left, StringComparison.Ordinal);
        Assert.Equal(new[] { "a:b:c", "c" },
            CronSeedIdentity.LegacyAdoptionKeys("a:b", "c"));
        Assert.Throws<ArgumentException>(() => CronSeedIdentity.SeedKey(
            new string('n', CronSeedIdentity.MaxIdentityUtf8Bytes + 1), "id"));
    }

    [Fact]
    public async Task Seeding_ForwardsHostCancellationToken_ToMigrate()
    {
        RegisterFunction("Reqless");
        RegisterDescriptors(
            new TickerFunctionDescriptor("Reqless", TickerTaskPriority.Normal, "* * * * *"));

        var internalManager = Substitute.For<IInternalTickerManager>();
        CancellationToken observed = default;
        var wasObserved = false;
        internalManager
            .When(m => m.MigrateDefinedCronTickers(Arg.Any<DefinedCronSeedManifest>(), Arg.Any<CancellationToken>()))
            .Do(ci =>
            {
                observed = ci.Arg<CancellationToken>();
                wasObserved = true;
            });

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // The initializer must thread the host's shutdown token into startup seeding rather than
        // silently swallowing it (the previous code passed a default CancellationToken), so a
        // cancelled host propagates cancellation into provider seeding I/O.
        await RunInitializer(internalManager, cts.Token);

        Assert.True(wasObserved, "MigrateDefinedCronTickers was never invoked.");
        Assert.True(observed.IsCancellationRequested,
            "Host cancellation token was not forwarded to MigrateDefinedCronTickers.");
    }

    private static async Task RunInitializer(
        IInternalTickerManager internalManager, CancellationToken cancellationToken = default)
    {
        ActivationEpochTestDouble.Configure(internalManager);
        var context = new TickerExecutionContext { OptionsSeeding = new TestOptionsSeeding() };
        var configuration = Substitute.For<IConfiguration>();

        var services = new ServiceCollection();
        services.AddSingleton(context);
        services.AddSingleton(configuration);
        services.AddSingleton(internalManager);
        services.AddSingleton(new SchedulerOptionsBuilder());
        var sp = services.BuildServiceProvider();

        var initializer = new TickerQInitializerHostedService(context, sp, configuration)
        {
            InitializationRequested = true
        };
        await initializer.StartAsync(cancellationToken);
    }

    private sealed class TestOptionsSeeding : ITickerOptionsSeeding
    {
        public bool SeedDefinedCronTickers => true;
        public long ReconciliationEpoch => 1;
        public string DefinedCronApplicationNamespace => "defined-cron-policy-tests";
        public Func<IServiceProvider, CancellationToken, Task> TimeSeederAction => null;
        public Func<IServiceProvider, CancellationToken, Task> CronSeederAction => null;
    }

    private static void RegisterFunction(string name)
    {
        TickerFunctionProvider.RegisterFunctions(
            new Dictionary<string, (string, TickerTaskPriority, TickerFunctionDelegate, int)>
            {
                [name] = ("* * * * *", TickerTaskPriority.Normal, NoOp, 1)
            });
    }

    private static Task NoOp(CancellationToken ct, IServiceProvider sp, TickerFunctionContext c) => Task.CompletedTask;

    private static void RegisterDescriptors(params TickerFunctionDescriptor[] descriptors)
    {
        TickerFunctionProvider.RegisterDescriptors(
            descriptors.ToDictionary(d => d.FunctionName));
    }

    private static void ResetProvider()
    {
        var type = typeof(TickerFunctionProvider);
        const BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;

        type.GetField("TickerFunctions", flags)!.SetValue(null,
            System.Collections.Frozen.FrozenDictionary<string, (string, TickerTaskPriority, TickerFunctionDelegate, int)>.Empty);
        type.GetField("TickerFunctionRequestTypes", flags)!.SetValue(null,
            System.Collections.Frozen.FrozenDictionary<string, (string, Type)>.Empty);
        type.GetField("TickerFunctionRequestInfos", flags)!.SetValue(null,
            System.Collections.Frozen.FrozenDictionary<string, (string, string)>.Empty);
        type.GetField("_snapshot", flags)!.SetValue(null, TickerFunctionRegistrySnapshot.Empty);
        type.GetField("_functionRegistrations", flags)!.SetValue(null, null);
        type.GetField("_requestTypeRegistrations", flags)!.SetValue(null, null);
        type.GetField("_requestInfoRegistrations", flags)!.SetValue(null, null);
        type.GetField("_runtimeRequestRegistrations", flags)!.SetValue(null, null);
        ((System.Collections.IList)type.GetField("_pendingDescriptors", flags)!.GetValue(null)!).Clear();
        type.GetProperty("IsBuilt", flags)!.SetValue(null, false);
    }
}
