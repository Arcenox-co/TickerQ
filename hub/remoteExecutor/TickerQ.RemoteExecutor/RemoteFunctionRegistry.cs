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
    /// Fully unregisters a function: drops its execution entry AND its canonical wire descriptor
    /// (and runtime metadata) from the one registry snapshot in a single coherent publication, then
    /// drops it from this registry so the scheduler stops scanning for it. Called when the Hub pushes
    /// a <c>RemoveFunction</c> webhook.
    /// </summary>
    public static bool Unregister(string functionName)
    {
        if (string.IsNullOrWhiteSpace(functionName)) return false;

        var separator = functionName.LastIndexOf('@');
        var isQualified = separator > 0 && separator < functionName.Length - 1;
        var bareName = isQualified ? functionName[..separator] : functionName;
        var nodeName = isQualified ? functionName[(separator + 1)..] : GetNodeName(bareName);
        var qualifiedName = string.IsNullOrEmpty(nodeName) ? bareName : $"{bareName}@{nodeName}";

        // Canonical single-snapshot removal: function + descriptor + runtime disappear together, so
        // no reader can observe a snapshot where a removed function still has a descriptor.
        var removedCanonical = TickerQ.Utilities.TickerFunctionProvider.UnregisterRemoteFunction(qualifiedName);
        var removedRegistry = FunctionNodeMap.TryRemove(bareName, out _);
        return removedCanonical || removedRegistry;
    }
}
