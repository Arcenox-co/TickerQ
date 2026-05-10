using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace TickerQ.RemoteExecutor.WorkerStream;

/// <summary>
/// Process-wide registry of live worker streams. Each SDK process opens one stream;
/// multiple SDK replicas with the same NodeName produce N streams under the same
/// (envId, nodeName) bucket. Outbound dispatch picks one stream per call via
/// round-robin so multi-replica SDKs get load-balanced automatically.
/// </summary>
public sealed class WorkerStreamRegistry
{
    // Connections keyed by their unique workerId (assigned at Hello time).
    private readonly ConcurrentDictionary<string, SchedulerWorkerConnection> _byWorkerId = new();
    // Round-robin counter per (envId, nodeName) bucket.
    private readonly ConcurrentDictionary<(Guid Env, string Node), int> _rrCounters
        = new();

    /// <summary>
    /// Fires whenever a worker stream is registered or unregistered. Subscribers (like the
    /// tunnel-side SDK report publisher) snapshot <see cref="All"/> on each event to push
    /// fresh state to the Hub.
    /// </summary>
    public event Action? Changed;

    public void Register(SchedulerWorkerConnection conn)
    {
        if (conn == null) throw new ArgumentNullException(nameof(conn));
        _byWorkerId[conn.WorkerId] = conn;
        Changed?.Invoke();
    }

    public void Unregister(SchedulerWorkerConnection conn)
    {
        if (conn == null) return;
        if (_byWorkerId.TryRemove(conn.WorkerId, out _))
            Changed?.Invoke();
    }

    /// <summary>
    /// Returns one live stream for (envId, nodeName), round-robin across replicas.
    /// Returns null when no SDK is connected for that name.
    /// </summary>
    public SchedulerWorkerConnection? PickForNode(Guid environmentId, string nodeName)
    {
        if (string.IsNullOrWhiteSpace(nodeName)) return null;

        var matches = _byWorkerId.Values
            .Where(c => c.EnvironmentId == environmentId
                        && string.Equals(c.NodeName, nodeName, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (matches.Length == 0) return null;
        if (matches.Length == 1) return matches[0];

        var key = (environmentId, nodeName);
        var idx = _rrCounters.AddOrUpdate(key, 0, (_, prev) => unchecked(prev + 1));
        return matches[(int)((uint)idx % (uint)matches.Length)];
    }

    /// <summary>All live streams, for diagnostics/dashboard.</summary>
    public IReadOnlyCollection<SchedulerWorkerConnection> All() => _byWorkerId.Values.ToArray();

    /// <summary>All live streams for an env, for env-wide operations like resync.</summary>
    public IReadOnlyCollection<SchedulerWorkerConnection> AllForEnvironment(Guid environmentId)
        => _byWorkerId.Values.Where(c => c.EnvironmentId == environmentId).ToArray();
}
