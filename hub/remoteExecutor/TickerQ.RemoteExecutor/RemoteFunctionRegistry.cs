using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace TickerQ.RemoteExecutor;

internal static class RemoteFunctionRegistry
{
    private static readonly ConcurrentDictionary<string, string> FunctionNodeMap = new();

    /// <summary>Snapshot of all remote function names currently tracked.</summary>
    public static IReadOnlyCollection<string> SnapshotFunctionNames() => FunctionNodeMap.Keys.ToArray();

    public static void MarkRemote(string functionName, string nodeName = "")
    {
        if (string.IsNullOrWhiteSpace(functionName))
            return;

        FunctionNodeMap[functionName] = nodeName ?? string.Empty;
    }

    public static void Remove(string functionName)
    {
        if (string.IsNullOrWhiteSpace(functionName))
            return;

        FunctionNodeMap.TryRemove(functionName, out _);
    }

    public static bool IsRemote(string functionName)
        => !string.IsNullOrWhiteSpace(functionName) && FunctionNodeMap.ContainsKey(functionName);

    public static string GetNodeName(string functionName)
    {
        if (string.IsNullOrWhiteSpace(functionName)) return string.Empty;
        return FunctionNodeMap.TryGetValue(functionName, out var n) ? n : string.Empty;
    }

    /// <summary>
    /// Fully unregisters a function: drops it from <see cref="TickerFunctionProvider.TickerFunctions"/>,
    /// from this registry, and republishes the provider so the scheduler stops scanning for it.
    /// Called when the Hub pushes a <c>RemoveFunction</c> webhook.
    /// </summary>
    public static bool Unregister(string functionName)
    {
        if (string.IsNullOrWhiteSpace(functionName)) return false;

        var dict = TickerQ.Utilities.TickerFunctionProvider.TickerFunctions.ToDictionary();
        if (!dict.Remove(functionName)) return false;

        Remove(functionName);
        TickerQ.Utilities.TickerFunctionProvider.ReplaceFunctions(dict);
        return true;
    }
}
