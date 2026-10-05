using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using TickerQ.DependencyInjection;
using TickerQ.Utilities;
using TickerQ.Utilities.Base;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Models;
using Xunit;

namespace TickerQ.Tests;

/// <summary>
/// Real registration-boundary tests: every typed registration mode must publish a canonical
/// descriptor into <see cref="TickerFunctionProvider.TickerFunctionDescriptors"/>, and request-less
/// registrations must publish a descriptor with <c>Request == null</c>.
/// </summary>
[Collection("TickerFunctionProviderState")]
public class TickerEndpointRegistrationTests : IDisposable
{
    public TickerEndpointRegistrationTests() => ResetProvider();
    public void Dispose() => ResetProvider();

    public sealed record BoundaryRequest(string Value);

    public sealed class TypedBoundaryJob : ITickerFunction<BoundaryRequest>
    {
        public Task ExecuteAsync(TickerFunctionContext<BoundaryRequest> context, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    public sealed class RequestLessBoundaryJob : ITickerFunction
    {
        public Task ExecuteAsync(TickerFunctionContext context, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    [Fact]
    public void MapTicker_InterfaceTyped_EmitsDescriptorWithRequest()
    {
        var services = new ServiceCollection();
        services.MapTicker<TypedBoundaryJob, BoundaryRequest>();
        TickerFunctionProvider.Build();

        var descriptor = TickerFunctionProvider.TickerFunctionDescriptors[nameof(TypedBoundaryJob)];
        Assert.NotNull(descriptor.Request);
        Assert.Equal(typeof(BoundaryRequest).FullName, descriptor.Request!.TypeName);
    }

    [Fact]
    public void MapTicker_InterfaceRequestLess_EmitsDescriptorWithNullRequest()
    {
        var services = new ServiceCollection();
        services.MapTicker<RequestLessBoundaryJob>();
        TickerFunctionProvider.Build();

        var descriptor = TickerFunctionProvider.TickerFunctionDescriptors[nameof(RequestLessBoundaryJob)];
        Assert.Null(descriptor.Request);
    }

    [Fact]
    public void MapTicker_FluentLambdaTyped_EmitsDescriptorWithRequest()
    {
        var services = new ServiceCollection();
        services.MapTicker<BoundaryRequest>("Fluent.Typed", (ctx, ct) => Task.CompletedTask);
        TickerFunctionProvider.Build();

        var descriptor = TickerFunctionProvider.TickerFunctionDescriptors["Fluent.Typed"];
        Assert.NotNull(descriptor.Request);
        Assert.Equal(typeof(BoundaryRequest).FullName, descriptor.Request!.TypeName);
    }

    [Fact]
    public void MapTicker_FluentLambdaRequestLess_EmitsDescriptorWithNullRequest()
    {
        var services = new ServiceCollection();
        services.MapTicker("Fluent.RequestLess", (ctx, ct) => Task.CompletedTask);
        TickerFunctionProvider.Build();

        Assert.True(TickerFunctionProvider.TickerFunctionDescriptors.ContainsKey("Fluent.RequestLess"));
        var descriptor = TickerFunctionProvider.TickerFunctionDescriptors["Fluent.RequestLess"];
        Assert.Null(descriptor.Request);
    }

    [Fact]
    public void MapTicker_FluentLambdaRequestLessWithServiceProvider_EmitsDescriptorWithNullRequest()
    {
        var services = new ServiceCollection();
        services.MapTicker("Fluent.RequestLess.Sp", (ctx, sp, ct) => Task.CompletedTask);
        TickerFunctionProvider.Build();

        Assert.True(TickerFunctionProvider.TickerFunctionDescriptors.ContainsKey("Fluent.RequestLess.Sp"));
        var descriptor = TickerFunctionProvider.TickerFunctionDescriptors["Fluent.RequestLess.Sp"];
        Assert.Null(descriptor.Request);
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
