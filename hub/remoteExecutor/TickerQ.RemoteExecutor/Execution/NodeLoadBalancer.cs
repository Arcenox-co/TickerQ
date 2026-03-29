using System.Collections.Concurrent;

namespace TickerQ.RemoteExecutor.Execution;

internal static class NodeLoadBalancer
{
    private static int _roundRobinCounter;

    /// <summary>
    /// Selects the node with the most available slots.
    /// Round-robin tiebreaker when multiple nodes have equal availability.
    /// Returns null if no nodes can accept work.
    /// </summary>
    public static string? SelectNode(ConcurrentDictionary<string, NodeConnection> connections)
    {
        string? bestNode = null;
        var bestSlots = 0;
        var counter = Interlocked.Increment(ref _roundRobinCounter);
        var index = 0;

        foreach (var (nodeName, connection) in connections)
        {
            var state = connection.State;
            if (!state.CanAcceptWork || !connection.CircuitBreaker.AllowRequest())
                continue;

            var slots = state.AvailableSlots;

            if (slots > bestSlots || (slots == bestSlots && (counter + index) % 2 == 0))
            {
                bestSlots = slots;
                bestNode = nodeName;
            }

            index++;
        }

        return bestNode;
    }
}
