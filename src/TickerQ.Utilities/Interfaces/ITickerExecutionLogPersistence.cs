using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TickerQ.Utilities.Models;

namespace TickerQ.Utilities.Interfaces;

/// <summary>Optional durable diagnostic tails; implementations must isolate application partitions.</summary>
public interface ITickerExecutionLogPersistence
{
    /// <summary>Idempotently write opaque snapshots, preserving any higher UpdatedAtUtcTicks version.</summary>
    Task WriteAsync(IReadOnlyList<ExecutionLogArchive> records, CancellationToken cancellationToken);
    /// <summary>Read one unexpired snapshot in the exact partition, or return null when absent.</summary>
    Task<ExecutionLogArchive> ReadAsync(string partitionKey, Guid tickerId, long nowUtcTicks, CancellationToken cancellationToken);
    /// <summary>Read a bounded group of tails. Built-in stores use a database batch; custom stores remain compatible.</summary>
    async Task<IReadOnlyList<ExecutionLogArchive>> ReadBatchAsync(string partitionKey, IReadOnlyList<Guid> tickerIds,
        long nowUtcTicks, CancellationToken cancellationToken)
    {
        var rows = new List<ExecutionLogArchive>();
        foreach (var id in tickerIds)
        {
            var row = await ReadAsync(partitionKey, id, nowUtcTicks, cancellationToken);
            if (row != null) rows.Add(row);
        }
        return rows;
    }
    /// <summary>Bounded cleanup of expired and oldest excess records in this partition.</summary>
    Task PruneAsync(string partitionKey, long nowUtcTicks, int maxExecutions, int batchSize, CancellationToken cancellationToken);
}
