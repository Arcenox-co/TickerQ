using System.Collections.Concurrent;
using System.Threading.Channels;
using Grpc.Core;
using Microsoft.Extensions.Logging;
using TickerQ.Grpc.Contracts;

namespace TickerQ.RemoteExecutor.Execution;

internal sealed class GrpcNodeConnectionManager
{
    private readonly ConcurrentDictionary<string, NodeConnection> _connections = new();
    private readonly ILogger<GrpcNodeConnectionManager>? _logger;
    private readonly int _channelCapacity;
    private readonly int _circuitBreakerThreshold;
    private readonly TimeSpan _circuitBreakerCooldown;

    public GrpcNodeConnectionManager(
        TickerQRemoteExecutionOptions options,
        ILogger<GrpcNodeConnectionManager>? logger = null)
    {
        _channelCapacity = options.NodeChannelCapacity;
        _circuitBreakerThreshold = options.CircuitBreakerFailureThreshold;
        _circuitBreakerCooldown = options.CircuitBreakerCooldown;
        _logger = logger;
    }

    public void RegisterNode(
        string nodeName,
        IServerStreamWriter<ExecutionCommand> writer,
        int maxConcurrency,
        CancellationToken streamLifetime)
    {
        var channel = Channel.CreateBounded<ExecutionCommand>(new BoundedChannelOptions(_channelCapacity)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait
        });

        var writerTask = Task.Run(async () =>
        {
            await foreach (var command in channel.Reader.ReadAllAsync(streamLifetime))
            {
                await writer.WriteAsync(command, streamLifetime);
            }
        }, streamLifetime);

        var state = new NodeState(maxConcurrency);
        var circuitBreaker = new NodeCircuitBreaker(_circuitBreakerThreshold, _circuitBreakerCooldown);
        var connection = new NodeConnection(nodeName, channel.Writer, writerTask, state, circuitBreaker);

        _connections[nodeName] = connection;
        _logger?.LogInformation("Node connected: {NodeName} (maxConcurrency={MaxConcurrency})", nodeName, maxConcurrency);
    }

    public void UnregisterNode(string nodeName)
    {
        if (_connections.TryRemove(nodeName, out var connection))
        {
            connection.WriteChannel.TryComplete();
            _logger?.LogInformation("Node disconnected: {NodeName}", nodeName);
        }
    }

    public bool DispatchTask(string nodeName, DispatchTask task)
    {
        if (!_connections.TryGetValue(nodeName, out var connection))
        {
            _logger?.LogWarning("Cannot dispatch task {TickerId}: node {NodeName} not connected",
                task.Id, nodeName);
            return false;
        }

        if (!connection.CircuitBreaker.AllowRequest())
        {
            _logger?.LogWarning("Cannot dispatch task {TickerId}: node {NodeName} circuit is open",
                task.Id, nodeName);
            return false;
        }

        var command = new ExecutionCommand { DispatchTask = task };

        if (!connection.WriteChannel.TryWrite(command))
        {
            _logger?.LogWarning("Cannot dispatch task {TickerId}: node {NodeName} channel is full",
                task.Id, nodeName);
            return false;
        }

        connection.State.RecordDispatched();
        return true;
    }

    /// <summary>
    /// Fire-and-forget: selects the best node via load balancer, enqueues the task.
    /// Falls back to trying remaining nodes if the selected node's channel is full.
    /// </summary>
    public void DispatchToAny(DispatchTask task)
    {
        // Try load-balanced selection first
        var bestNode = NodeLoadBalancer.SelectNode(_connections);
        if (bestNode != null && TryDispatchToNode(bestNode, task))
            return;

        // Fallback: try any node that can accept work
        foreach (var (nodeName, connection) in _connections)
        {
            if (nodeName == bestNode)
                continue;

            if (!connection.State.CanAcceptWork || !connection.CircuitBreaker.AllowRequest())
                continue;

            if (TryDispatchToNode(nodeName, task))
                return;
        }

        throw new InvalidOperationException($"No available nodes to dispatch task {task.Id}");
    }

    public void RecordTaskResult(string nodeName, bool success)
    {
        if (!_connections.TryGetValue(nodeName, out var connection))
            return;

        connection.State.RecordCompleted();

        if (success)
            connection.CircuitBreaker.RecordSuccess();
        else
            connection.CircuitBreaker.RecordFailure();
    }

    public void UpdateNodeCapacity(string nodeName, int activeTasks, int maxConcurrency)
    {
        if (_connections.TryGetValue(nodeName, out var connection))
            connection.State.UpdateCapacity(activeTasks, maxConcurrency);
    }

    public void InitiateDrain(string nodeName, string reason)
    {
        if (!_connections.TryGetValue(nodeName, out var connection))
            return;

        connection.State.SetDraining(true);
        connection.WriteChannel.TryWrite(new ExecutionCommand
        {
            Drain = new DrainSignal { Reason = reason }
        });
        _logger?.LogInformation("Drain initiated for node {NodeName}: {Reason}", nodeName, reason);
    }

    public void HandleDrainComplete(string nodeName, int tasksDrained)
    {
        _logger?.LogInformation("Node {NodeName} drain complete ({TasksDrained} tasks drained)",
            nodeName, tasksDrained);
    }

    public void HandleDrainSignalFromNode(string nodeName)
    {
        if (_connections.TryGetValue(nodeName, out var connection))
        {
            connection.State.SetDraining(true);
            _logger?.LogInformation("Node {NodeName} requested drain (shutting down)", nodeName);
        }
    }

    public void SendResyncToAll()
    {
        var command = new ExecutionCommand { Resync = new ResyncSignal() };

        foreach (var (nodeName, connection) in _connections)
        {
            if (!connection.WriteChannel.TryWrite(command))
                _logger?.LogWarning("Failed to enqueue resync for node {NodeName}", nodeName);
        }
    }

    public bool HasConnectedNodes => !_connections.IsEmpty;

    public IReadOnlyCollection<string> ConnectedNodes => _connections.Keys.ToArray();

    private bool TryDispatchToNode(string nodeName, DispatchTask task)
    {
        if (!_connections.TryGetValue(nodeName, out var connection))
            return false;

        var command = new ExecutionCommand { DispatchTask = task };
        if (!connection.WriteChannel.TryWrite(command))
            return false;

        connection.State.RecordDispatched();
        _logger?.LogDebug("Dispatched task {TickerId} to node {NodeName}", task.Id, nodeName);
        return true;
    }
}
