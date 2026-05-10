using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Grpc.Core;
using TickerQ.Worker.V1;

namespace TickerQ.RemoteExecutor.WorkerStream;

/// <summary>
/// Live worker stream from a single SDK process. Wraps the gRPC server-stream writer
/// with a write-lock (gRPC server streams reject concurrent writes) and a pending-op
/// tracker keyed by request_id so outbound commands (ExecuteFunction / TriggerResync /
/// RemoveFunction) can await their corresponding inbound ExecutionResult / CommandAck
/// without blocking the read loop.
/// </summary>
public sealed class SchedulerWorkerConnection : IAsyncDisposable
{
    private readonly IServerStreamWriter<SchedulerCommand> _writer;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly ConcurrentDictionary<string, TaskCompletionSource<ExecutionResult>> _pendingExecutions = new();
    private readonly ConcurrentDictionary<string, TaskCompletionSource<CommandAck>> _pendingAcks = new();
    private readonly CancellationTokenSource _closedCts = new();

    public SchedulerWorkerConnection(
        Guid environmentId,
        Guid applicationId,
        Guid nodeApplicationId,
        string nodeName,
        string workerId,
        int maxConcurrency,
        IServerStreamWriter<SchedulerCommand> writer)
    {
        EnvironmentId = environmentId;
        ApplicationId = applicationId;
        NodeApplicationId = nodeApplicationId;
        NodeName = nodeName;
        WorkerId = workerId;
        MaxConcurrency = maxConcurrency;
        _writer = writer ?? throw new ArgumentNullException(nameof(writer));
        ConnectedAt = DateTime.UtcNow;
    }

    public Guid EnvironmentId { get; }
    public Guid ApplicationId { get; }
    public Guid NodeApplicationId { get; }
    public string NodeName { get; }
    public string WorkerId { get; }
    public int MaxConcurrency { get; }
    public DateTime ConnectedAt { get; }
    public CancellationToken Closed => _closedCts.Token;

    /// <summary>
    /// Writes a command frame to the SDK. Serializes via the per-connection write lock.
    /// </summary>
    public async Task WriteAsync(SchedulerCommand command, CancellationToken ct = default)
    {
        await _writeLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await _writer.WriteAsync(command, ct).ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    /// <summary>
    /// Sends an ExecuteFunction and awaits the matching ExecutionResult by request_id.
    /// </summary>
    public async Task<ExecutionResult> ExecuteFunctionAsync(ExecuteFunction request, TimeSpan timeout, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(request.RequestId))
            request.RequestId = Guid.NewGuid().ToString("N");

        var tcs = new TaskCompletionSource<ExecutionResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingExecutions[request.RequestId] = tcs;

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _closedCts.Token);
        linked.CancelAfter(timeout);
        await using var _ = linked.Token.Register(() =>
        {
            if (_pendingExecutions.TryRemove(request.RequestId, out var s))
                s.TrySetException(new TimeoutException($"Worker did not return ExecutionResult for {request.RequestId} within {timeout}"));
        }).ConfigureAwait(false);

        await WriteAsync(new SchedulerCommand { ExecuteFunction = request }, ct).ConfigureAwait(false);
        return await tcs.Task.ConfigureAwait(false);
    }

    /// <summary>
    /// Sends a TriggerResync command and awaits CommandAck by request_id.
    /// </summary>
    public async Task<CommandAck> TriggerResyncAsync(TimeSpan timeout, CancellationToken ct)
    {
        var requestId = Guid.NewGuid().ToString("N");
        var tcs = new TaskCompletionSource<CommandAck>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingAcks[requestId] = tcs;

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _closedCts.Token);
        linked.CancelAfter(timeout);
        await using var _ = linked.Token.Register(() =>
        {
            if (_pendingAcks.TryRemove(requestId, out var s))
                s.TrySetException(new TimeoutException($"Worker did not ack TriggerResync within {timeout}"));
        }).ConfigureAwait(false);

        await WriteAsync(new SchedulerCommand
        {
            TriggerResync = new TriggerResync { RequestId = requestId }
        }, ct).ConfigureAwait(false);
        return await tcs.Task.ConfigureAwait(false);
    }

    /// <summary>Called by the read loop when an ExecutionResult frame arrives.</summary>
    public void CompleteExecution(ExecutionResult result)
    {
        if (result == null || string.IsNullOrEmpty(result.RequestId)) return;
        if (_pendingExecutions.TryRemove(result.RequestId, out var tcs))
            tcs.TrySetResult(result);
    }

    /// <summary>Called by the read loop when a CommandAck frame arrives.</summary>
    public void CompleteAck(CommandAck ack)
    {
        if (ack == null || string.IsNullOrEmpty(ack.RequestId)) return;
        if (_pendingAcks.TryRemove(ack.RequestId, out var tcs))
            tcs.TrySetResult(ack);
    }

    /// <summary>Marks the connection closed, failing all pending awaiters.</summary>
    public void SignalClosed()
    {
        _closedCts.Cancel();
        foreach (var kv in _pendingExecutions)
        {
            kv.Value.TrySetException(new InvalidOperationException("Worker stream closed before ExecutionResult arrived"));
        }
        _pendingExecutions.Clear();
        foreach (var kv in _pendingAcks)
        {
            kv.Value.TrySetException(new InvalidOperationException("Worker stream closed before ack arrived"));
        }
        _pendingAcks.Clear();
    }

    public ValueTask DisposeAsync()
    {
        SignalClosed();
        _writeLock.Dispose();
        _closedCts.Dispose();
        return ValueTask.CompletedTask;
    }
}
