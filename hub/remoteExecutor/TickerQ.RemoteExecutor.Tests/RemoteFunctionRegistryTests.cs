using TickerQ.Utilities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Models;
using Xunit;

namespace TickerQ.RemoteExecutor.Tests;

public sealed class RemoteFunctionRegistryTests
{
    private static Task NoOp(
        CancellationToken cancellationToken,
        IServiceProvider serviceProvider,
        TickerQ.Utilities.Base.TickerFunctionContext context) => Task.CompletedTask;

    [Fact]
    public void Unregister_NormalizesBareAndQualifiedNames_AndRemovesCanonicalEntries()
    {
        var functions = new Dictionary<string, (string, TickerTaskPriority, TickerFunctionDelegate, int)>
        {
            ["BareJob@node-a"] = ("0 * * * *", TickerTaskPriority.Normal, NoOp, 0),
            ["QualifiedJob@node-b"] = ("0 * * * *", TickerTaskPriority.High, NoOp, 0)
        };
        var descriptors = new Dictionary<string, TickerFunctionDescriptor>
        {
            ["BareJob@node-a"] = new("BareJob@node-a"),
            ["QualifiedJob@node-b"] = new("QualifiedJob@node-b", TickerTaskPriority.High)
        };

        TickerFunctionProvider.MergeRemoteSnapshot(
            functions,
            descriptors,
            static key => key.Contains('@', StringComparison.Ordinal));
        RemoteFunctionRegistry.MarkRemote("BareJob", "node-a");
        RemoteFunctionRegistry.MarkRemote("QualifiedJob", "node-b");

        Assert.True(RemoteFunctionRegistry.Unregister("BareJob"));
        Assert.False(RemoteFunctionRegistry.IsRemote("BareJob"));
        Assert.False(TickerFunctionProvider.TickerFunctions.ContainsKey("BareJob@node-a"));
        Assert.False(TickerFunctionProvider.TickerFunctionDescriptors.ContainsKey("BareJob@node-a"));

        Assert.True(RemoteFunctionRegistry.Unregister("QualifiedJob@node-b"));
        Assert.False(RemoteFunctionRegistry.IsRemote("QualifiedJob"));
        Assert.False(TickerFunctionProvider.TickerFunctions.ContainsKey("QualifiedJob@node-b"));
        Assert.False(TickerFunctionProvider.TickerFunctionDescriptors.ContainsKey("QualifiedJob@node-b"));

        Assert.False(RemoteFunctionRegistry.Unregister("BareJob"));
        Assert.False(RemoteFunctionRegistry.Unregister("QualifiedJob@node-b"));
    }
}
