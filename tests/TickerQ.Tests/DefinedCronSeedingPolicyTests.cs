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
            .When(m => m.MigrateDefinedCronTickers(Arg.Any<DefinedCronTickerSeed[]>(), Arg.Any<CancellationToken>()))
            .Do(ci => captured = ci.Arg<DefinedCronTickerSeed[]>());

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

    private static async Task RunInitializer(IInternalTickerManager internalManager)
    {
        var context = new TickerExecutionContext();
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
        await initializer.StartAsync(CancellationToken.None);
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
