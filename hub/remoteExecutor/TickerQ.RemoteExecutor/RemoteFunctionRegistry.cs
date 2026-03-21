using System.Collections.Concurrent;

namespace TickerQ.RemoteExecutor;

internal static class RemoteFunctionRegistry
{
    private static readonly ConcurrentDictionary<string, byte> RemoteFunctions = new();

    public static void MarkRemote(string functionName)
    {
        if (string.IsNullOrWhiteSpace(functionName))
            return;

        RemoteFunctions[functionName] = 0;
    }

    public static void Remove(string functionName)
    {
        if (string.IsNullOrWhiteSpace(functionName))
            return;

        RemoteFunctions.TryRemove(functionName, out _);
    }

    public static bool IsRemote(string functionName)
        => !string.IsNullOrWhiteSpace(functionName) && RemoteFunctions.ContainsKey(functionName);
}
