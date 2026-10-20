#nullable enable
using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TickerQ.Utilities.Enums;

namespace TickerQ.Utilities.Cluster;

public sealed class TickerQNodeRecord
{
    public string NodeKey { get; set; } = "";
    public string ExecutionOwnerId { get; set; } = "";
    public string NodeIdentifier { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string? DashboardUrl { get; set; }
    public string Version { get; set; } = "";
    public DateTime StartedAtUtc { get; set; }
    public DateTime LastSeenAtUtc { get; set; }
    public bool IsRunning { get; set; }
    public int MaxConcurrency { get; set; }
    public int FunctionCount { get; set; }
    public int ActiveExecutions { get; set; }
    public int QueuedExecutions { get; set; }
    public int ActiveWorkflows { get; set; }
    public int WaitingForFunction { get; set; }
    public int RetryingExecutions { get; set; }
}

public sealed class TickerQExecutionOwnership
{
    public TickerType Type { get; set; }
    public Guid TickerId { get; set; }
    public string ExecutionOwnerId { get; set; } = "";
    public Guid? AcquisitionToken { get; set; }
    public TickerStatus Status { get; set; }
}

public enum TickerQControlCommandStatus
{
    Pending = 0,
    Completed = 1,
    Expired = 2
}

public sealed class TickerQControlCommand
{
    public Guid Id { get; set; }
    public string TargetNodeKey { get; set; } = "";
    public string TargetExecutionOwnerId { get; set; } = "";
    public TickerType TickerType { get; set; }
    public Guid TickerId { get; set; }
    public Guid? AcquisitionToken { get; set; }
    public DateTime RequestedAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public TickerQControlCommandStatus Status { get; set; }
    public string? Outcome { get; set; }
}

public static class TickerQClusterIdentity
{
    public static string NodeKey(string executionOwnerId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executionOwnerId);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(executionOwnerId))).ToLowerInvariant();
    }

    public static Guid CancellationCommandId(TickerType type, Guid tickerId, Guid? acquisitionToken)
    {
        var value = $"cancel:{(int)type}:{tickerId:N}:{acquisitionToken?.ToString("N") ?? "none"}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return new Guid(hash.AsSpan(0, 16));
    }
}

/// <summary>
/// Optional provider capability for node discovery and durable cross-node control.
/// Default members preserve compatibility with external persistence providers.
/// </summary>
public interface ITickerQClusterControlStore
{
    bool SupportsClusterControl => false;

    Task UpsertNodeAsync(TickerQNodeRecord node, CancellationToken cancellationToken = default)
        => Task.FromException(new NotSupportedException("This operational store does not support cluster control."));

    Task<IReadOnlyList<TickerQNodeRecord>> GetNodesAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<TickerQNodeRecord>>(Array.Empty<TickerQNodeRecord>());

    Task<TickerQExecutionOwnership?> GetExecutionOwnershipAsync(
        TickerType type, Guid tickerId, CancellationToken cancellationToken = default)
        => Task.FromResult<TickerQExecutionOwnership?>(null);

    /// <summary>Idempotently persists a cancellation request for one execution generation.</summary>
    Task<bool> RequestCancellationAsync(TickerQControlCommand command, CancellationToken cancellationToken = default)
        => Task.FromResult(false);

    Task<IReadOnlyList<TickerQControlCommand>> GetPendingCommandsAsync(
        string executionOwnerId, int take, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<TickerQControlCommand>>(Array.Empty<TickerQControlCommand>());

    Task<bool> CompleteCommandAsync(
        Guid commandId, TickerQControlCommandStatus status, string outcome, DateTime completedAtUtc,
        CancellationToken cancellationToken = default)
        => Task.FromResult(false);

    Task PruneClusterControlAsync(DateTime nodesBeforeUtc, DateTime commandsBeforeUtc,
        CancellationToken cancellationToken = default) => Task.CompletedTask;
}
