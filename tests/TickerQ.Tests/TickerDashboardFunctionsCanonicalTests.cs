using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using TickerQ.Dashboard;
using TickerQ.Dashboard.Infrastructure.Dashboard;
using TickerQ.DependencyInjection;
using TickerQ.Utilities;
using TickerQ.Utilities.Base;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Infrastructure;
using TickerQ.Utilities.Models;
using Xunit;

namespace TickerQ.Tests;

/// <summary>
/// Boundary regression for <see cref="TickerDashboardRepository{TTimeTicker,TCronTicker}.GetTickerFunctions"/>.
/// The dashboard must project ONLY the one canonical descriptor generation
/// (<see cref="TickerFunctionProvider.TickerFunctionDescriptors"/>). A deliberately inconsistent
/// legacy mirror (TickerFunctions / TickerFunctionRequestTypes / TickerFunctionRequestInfos) must be
/// unable to influence the result, and the example JSON must come from the descriptor's canonical
/// example — never from the legacy request-info string.
/// </summary>
[Collection("TickerFunctionProviderState")]
public class TickerDashboardFunctionsCanonicalTests : IDisposable
{
    public sealed class FakeTimeTicker : TimeTickerEntity<FakeTimeTicker> { }
    public sealed class FakeCronTicker : CronTickerEntity { }

    public TickerDashboardFunctionsCanonicalTests() => ResetProvider();

    public void Dispose() => ResetProvider();

    private static Task NoOpDelegate(CancellationToken ct, IServiceProvider sp, TickerQ.Utilities.Base.TickerFunctionContext ctx)
        => Task.CompletedTask;

    private static TickerDashboardRepository<FakeTimeTicker, FakeCronTicker> CreateRepository()
        => new(
            new TickerExecutionContext(),
            Substitute.For<ITickerPersistenceProvider<FakeTimeTicker, FakeCronTicker>>(),
            Substitute.For<ITickerQHostScheduler>(),
            Substitute.For<ITickerQNotificationHubSender>(),
            new DashboardOptionsBuilder(),
            Substitute.For<ITickerQDispatcher>());

    [Fact]
    public void GetTickerFunctions_ProjectsCanonicalDescriptors_IgnoringInconsistentLegacyMirror()
    {
        // Canonical generation: one function with a descriptor carrying its wire type name and a
        // single canonical example. Priority is reconciled from the functions registry at Build().
        TickerFunctionProvider.RegisterFunctions(new Dictionary<string, (string, TickerTaskPriority, TickerFunctionDelegate, int)>
        {
            ["Canon.Job"] = ("0 0 * * *", TickerTaskPriority.High, NoOpDelegate, 0)
        });
        TickerFunctionProvider.RegisterDescriptors(new Dictionary<string, TickerFunctionDescriptor>
        {
            ["Canon.Job"] = new TickerFunctionDescriptor(
                "Canon.Job",
                TickerTaskPriority.High,
                request: new TickerRequestContract(
                    "Canonical.Request.Type",
                    examples: new[] { new TickerRequestExample("default", null, "{\"canonical\":1}") }))
        }, origin: "test");
        TickerFunctionProvider.Build();

        // Now deliberately desynchronise EVERY legacy mirror WITHOUT touching the canonical snapshot:
        //  * TickerFunctions: drop the real function, inject a ghost that has no descriptor.
        //  * TickerFunctionRequestInfos: wrong type name + wrong example for the real function.
        //  * TickerFunctionRequestTypes: a third contradictory type name.
        // ReplaceFunctions / ReplaceRequestInfo rewrite only the legacy mirror fields; the published
        // registry snapshot (and thus TickerFunctionDescriptors) is left intact.
        TickerFunctionProvider.ReplaceFunctions(new Dictionary<string, (string, TickerTaskPriority, TickerFunctionDelegate, int)>
        {
            ["Legacy.Ghost"] = ("* * * * *", TickerTaskPriority.Low, NoOpDelegate, 0)
        });
        TickerFunctionProvider.ReplaceRequestInfo(new Dictionary<string, (string RequestType, string RequestExampleJson)>
        {
            ["Canon.Job"] = ("Legacy.Wrong.Type", "{\"legacy\":999}")
        });
        typeof(TickerFunctionProvider)
            .GetField("TickerFunctionRequestTypes", BindingFlags.Static | BindingFlags.Public)!
            .SetValue(null, new Dictionary<string, (string, Type)>
            {
                ["Canon.Job"] = ("Legacy.Reflected.Type", typeof(int))
            }.ToFrozenDictionaryShim());

        var result = CreateRepository().GetTickerFunctions().ToList();

        // Exactly the canonical generation is projected — the ghost never appears.
        var entry = Assert.Single(result);
        Assert.Equal("Canon.Job", entry.Item1);

        var (typeName, exampleJson, priority) = entry.Item2;
        // Type name comes from the descriptor's request contract, not either legacy mirror.
        Assert.Equal("Canonical.Request.Type", typeName);
        Assert.NotEqual("Legacy.Wrong.Type", typeName);
        Assert.NotEqual("Legacy.Reflected.Type", typeName);
        // Example JSON is the descriptor's first canonical example's raw text, not the legacy string.
        Assert.Equal("{\"canonical\":1}", exampleJson);
        Assert.NotEqual("{\"legacy\":999}", exampleJson);
        // Priority is the descriptor's (reconciled at Build), not the mutated legacy TickerFunctions.
        Assert.Equal(TickerTaskPriority.High, priority);
    }

    [Fact]
    public async Task GetAllFunctionsAsync_ProjectsPresenceAwareCanonicalContractAndLegacyMirrors()
    {
        TickerFunctionProvider.RegisterFunctions(new Dictionary<string, (string, TickerTaskPriority, TickerFunctionDelegate, int)>
        {
            ["Transport.Job"] = ("0 * * * *", TickerTaskPriority.High, NoOpDelegate, 0),
            ["Transport.Bare"] = (null, TickerTaskPriority.Normal, NoOpDelegate, 0)
        });
        TickerFunctionProvider.RegisterDescriptors(new Dictionary<string, TickerFunctionDescriptor>
        {
            ["Transport.Job"] = new TickerFunctionDescriptor(
                "Transport.Job",
                TickerTaskPriority.High,
                "0 * * * *",
                contractVersion: 4,
                request: new TickerRequestContract(
                    "Transport.Request",
                    schemaJson: "{\"type\":\"object\",\"properties\":{\"amount\":{\"type\":\"integer\"}}}",
                    examples: new[] { new TickerRequestExample("default", "Example", "{\"amount\":5}") })),
            ["Transport.Bare"] = new TickerFunctionDescriptor("Transport.Bare")
        }, origin: "test");
        TickerFunctionProvider.Build();

        var service = new TickerDashboardDataService<FakeTimeTicker, FakeCronTicker>(
            Substitute.For<ITickerPersistenceProvider<FakeTimeTicker, FakeCronTicker>>());

        var functions = await service.GetAllFunctionsAsync();

        var typed = Assert.Single(functions, x => x.FunctionName == "Transport.Job");
        Assert.Equal(4, typed.ContractVersion);
        Assert.NotNull(typed.RequestContract);
        Assert.Equal("Transport.Request", typed.RequestContract!.TypeName);
        Assert.Equal("application/json", typed.RequestContract.MediaType);
        Assert.True(typed.RequestContract.Required);
        Assert.Equal("https://json-schema.org/draft/2020-12/schema", typed.RequestContract.SchemaDialect);
        Assert.Contains("\"amount\"", typed.RequestContract.SchemaJson);
        Assert.StartsWith("sha256:", typed.RequestContract.Fingerprint);
        var example = Assert.Single(typed.RequestContract.Examples);
        Assert.Equal("default", example.Key);
        Assert.Equal("Example", example.Summary);
        Assert.Equal("{\"amount\":5}", example.ValueJson);
        Assert.Equal(typed.RequestContract.TypeName, typed.RequestType);
        Assert.Equal(example.ValueJson, typed.RequestExample);

        var bare = Assert.Single(functions, x => x.FunctionName == "Transport.Bare");
        Assert.Equal(1, bare.ContractVersion);
        Assert.Null(bare.RequestContract);
        Assert.Null(bare.RequestType);
        Assert.Null(bare.RequestExample);
    }

    [Fact]
    public void GetTickerFunctions_RequestLessDescriptor_YieldsEmptyTypeAndNullExample()
    {
        TickerFunctionProvider.RegisterFunctions(new Dictionary<string, (string, TickerTaskPriority, TickerFunctionDelegate, int)>
        {
            ["Bare.Job"] = ("0 0 * * *", TickerTaskPriority.Normal, NoOpDelegate, 0)
        });
        TickerFunctionProvider.RegisterDescriptors(new Dictionary<string, TickerFunctionDescriptor>
        {
            ["Bare.Job"] = new TickerFunctionDescriptor("Bare.Job", TickerTaskPriority.Normal)
        }, origin: "test");
        TickerFunctionProvider.Build();

        var entry = Assert.Single(CreateRepository().GetTickerFunctions().ToList());
        Assert.Equal("Bare.Job", entry.Item1);
        var (typeName, exampleJson, priority) = entry.Item2;
        Assert.Equal(string.Empty, typeName);
        Assert.Null(exampleJson);
        Assert.Equal(TickerTaskPriority.Normal, priority);
    }

    public sealed class DashboardFluentRequest
    {
        public int Amount { get; set; }
        public string Name { get; set; }
    }

    [Fact]
    public void GetTickerFunctions_FluentTypedRegistration_YieldsGeneratedExample_AndNoLegacyGhost()
    {
        // Production wires the request serializer via WithJsonContext; a test must set a reflection
        // resolver so the compatibility fallback's JsonExampleGenerator can produce example JSON.
        var savedOptions = TickerHelper.RequestJsonSerializerOptions;
        TickerHelper.RequestJsonSerializerOptions = new JsonSerializerOptions
        {
            TypeInfoResolverChain = { new DefaultJsonTypeInfoResolver() }
        };
        try
        {
            // Real fluent typed registration: publishes a canonical descriptor built as
            // TickerRequestContract(typeName) with NO examples, and captures the CLR request type
            // in the legacy request-type snapshot — exactly the ordinary local typed case that the
            // canonical migration would otherwise regress from generated JSON to null.
            var services = new ServiceCollection();
            services.MapTicker<DashboardFluentRequest>(
                "Fluent.Typed.Job",
                (TickerFunctionContext<DashboardFluentRequest> ctx, CancellationToken ct) => Task.CompletedTask);
            TickerFunctionProvider.Build();

            // Confirm the precondition: the canonical descriptor genuinely carries NO example, so a
            // non-null example below can only come from the compatibility fallback.
            var descriptor = TickerFunctionProvider.TickerFunctionDescriptors["Fluent.Typed.Job"];
            Assert.NotNull(descriptor.Request);
            Assert.Empty(descriptor.Request!.Examples);

            // Inject a legacy-only request-type entry (no matching descriptor) to prove membership is
            // sourced solely from the descriptor table — a legacy ghost must never appear.
            typeof(TickerFunctionProvider)
                .GetField("TickerFunctionRequestTypes", BindingFlags.Static | BindingFlags.Public)!
                .SetValue(null, new Dictionary<string, (string, Type)>
                {
                    ["Fluent.Typed.Job"] = (typeof(DashboardFluentRequest).FullName!, typeof(DashboardFluentRequest)),
                    ["Legacy.Ghost.Typed"] = ("Legacy.Ghost.Request", typeof(DashboardFluentRequest))
                }.ToFrozenDictionaryShim());

            var result = CreateRepository().GetTickerFunctions().ToList();

            // Only the descriptor-backed function appears; the legacy-only ghost does not.
            var entry = Assert.Single(result);
            Assert.Equal("Fluent.Typed.Job", entry.Item1);

            var (typeName, exampleJson, _) = entry.Item2;
            Assert.Equal(typeof(DashboardFluentRequest).FullName, typeName);
            // Compatibility fallback regenerated a non-null example from the CLR request type.
            Assert.False(string.IsNullOrEmpty(exampleJson));
            Assert.Contains("\"Amount\"", exampleJson);
            Assert.Contains("\"Name\"", exampleJson);
        }
        finally
        {
            TickerHelper.RequestJsonSerializerOptions = savedOptions;
        }
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

internal static class FrozenDictionaryShimExtensions
{
    public static System.Collections.Frozen.FrozenDictionary<TKey, TValue> ToFrozenDictionaryShim<TKey, TValue>(
        this IDictionary<TKey, TValue> source) where TKey : notnull
        => System.Collections.Frozen.FrozenDictionary.ToFrozenDictionary(source);
}
