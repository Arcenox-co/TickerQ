using System.Collections.Frozen;
using System.Reflection;
using TickerQ.Utilities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Models;

namespace TickerQ.Tests;

[Collection("TickerFunctionProviderState")]
public sealed class TickerFunctionProviderDuplicateExecutionRegistrationTests : IDisposable
{
    public TickerFunctionProviderDuplicateExecutionRegistrationTests() => ResetProvider();

    public void Dispose() => ResetProvider();

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Build_ConflictingExecutionRegistrations_IsOrderIndependentAtomicAndRetrySafe(bool reverseOrder)
    {
        RegisterUniqueExecution();
        if (reverseOrder)
        {
            RegisterBetaExecution();
            RegisterAlphaExecution();
        }
        else
        {
            RegisterAlphaExecution();
            RegisterBetaExecution();
        }

        var first = Assert.Throws<InvalidOperationException>(TickerFunctionProvider.Build);

        Assert.Contains("DuplicateExecution", first.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(RegisterAlphaExecution), first.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(RegisterBetaExecution), first.Message, StringComparison.Ordinal);
        Assert.False(TickerFunctionProvider.IsBuilt);
        Assert.Empty(TickerFunctionProvider.TickerFunctions);

        var retry = Assert.Throws<InvalidOperationException>(TickerFunctionProvider.Build);

        Assert.Equal(first.Message, retry.Message);
        Assert.False(TickerFunctionProvider.IsBuilt);
        Assert.Empty(TickerFunctionProvider.TickerFunctions);
    }

    [Fact]
    public void Build_ExactlyIdenticalExecutionRegistrations_AreIdempotent()
    {
        TickerFunctionDelegate execution = AlphaExecution;
        var registration = new Dictionary<string, (string, TickerTaskPriority, TickerFunctionDelegate, int)>
        {
            ["IdempotentExecution"] = ("*/5 * * * *", TickerTaskPriority.High, execution, 3)
        };

        TickerFunctionProvider.RegisterFunctions(registration);
        TickerFunctionProvider.RegisterFunctions(registration);
        TickerFunctionProvider.Build();

        var published = Assert.Single(TickerFunctionProvider.TickerFunctions);
        Assert.Equal("IdempotentExecution", published.Key);
        Assert.Equal("*/5 * * * *", published.Value.cronExpression);
        Assert.Equal(TickerTaskPriority.High, published.Value.Priority);
        Assert.Same(execution, published.Value.Delegate);
        Assert.Equal(3, published.Value.MaxConcurrency);
    }

    private static void RegisterUniqueExecution() =>
        TickerFunctionProvider.RegisterFunctions(new Dictionary<string, (string, TickerTaskPriority, TickerFunctionDelegate, int)>
        {
            ["UniqueExecution"] = (string.Empty, TickerTaskPriority.Normal, AlphaExecution, 0)
        });

    private static void RegisterAlphaExecution() =>
        TickerFunctionProvider.RegisterFunctions(new Dictionary<string, (string, TickerTaskPriority, TickerFunctionDelegate, int)>
        {
            ["DuplicateExecution"] = ("shared-cron", TickerTaskPriority.High, AlphaExecution, 1)
        });

    private static void RegisterBetaExecution() =>
        TickerFunctionProvider.RegisterFunctions(new Dictionary<string, (string, TickerTaskPriority, TickerFunctionDelegate, int)>
        {
            ["DuplicateExecution"] = ("shared-cron", TickerTaskPriority.High, BetaExecution, 1)
        });

    private static Task AlphaExecution(CancellationToken cancellationToken, IServiceProvider serviceProvider, TickerQ.Utilities.Base.TickerFunctionContext context) =>
        Task.CompletedTask;

    private static Task BetaExecution(CancellationToken cancellationToken, IServiceProvider serviceProvider, TickerQ.Utilities.Base.TickerFunctionContext context) =>
        Task.CompletedTask;

    private static void ResetProvider()
    {
        var type = typeof(TickerFunctionProvider);
        const BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;

        type.GetField("TickerFunctions", flags)!.SetValue(null,
            FrozenDictionary<string, (string, TickerTaskPriority, TickerFunctionDelegate, int)>.Empty);
        type.GetField("TickerFunctionRequestTypes", flags)!.SetValue(null,
            FrozenDictionary<string, (string, Type)>.Empty);
        type.GetField("TickerFunctionRequestInfos", flags)!.SetValue(null,
            FrozenDictionary<string, (string, string)>.Empty);
        type.GetField("_snapshot", flags)!.SetValue(null, TickerFunctionRegistrySnapshot.Empty);
        type.GetField("_functionRegistrations", flags)!.SetValue(null, null);
        type.GetField("_requestTypeRegistrations", flags)!.SetValue(null, null);
        type.GetField("_requestInfoRegistrations", flags)!.SetValue(null, null);
        type.GetField("_runtimeRequestRegistrations", flags)!.SetValue(null, null);
        ((System.Collections.IList)type.GetField("_pendingDescriptors", flags)!.GetValue(null)!).Clear();
        type.GetProperty("IsBuilt", flags)!.SetValue(null, false);
    }
}
