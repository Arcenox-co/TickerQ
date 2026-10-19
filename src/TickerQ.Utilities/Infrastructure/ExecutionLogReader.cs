using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Models;

namespace TickerQ.Utilities.Infrastructure;

internal sealed record ExecutionLogReadResult(IReadOnlyList<TickerExecutionLogLine> Lines, bool Truncated, bool PersistenceAvailable);

internal sealed class ExecutionLogReader(InMemoryTickerExecutionLogStore memory, ExecutionLogOptions options,
    SchedulerOptionsBuilder scheduler, IServiceProvider services)
{
    public async Task<ExecutionLogReadResult> ReadAsync(Guid id, CancellationToken cancellationToken)
    {
        if (!options.Enabled) return new(Array.Empty<TickerExecutionLogLine>(), false, true);
        var local = memory.Snapshot(id);
        var lines = new List<TickerExecutionLogLine>();
        var truncated = local?.Truncated == true;
        var available = true;
        if (options.PersistToDatabase)
        {
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(options.StorageTimeout);
                var storage = services.GetRequiredService<ITickerExecutionLogPersistence>();
                var record = await storage.ReadAsync((scheduler.RuntimePartition ?? TickerQRuntimePartition.LegacyGlobal).StorageKey,
                    id, DateTime.UtcNow.Ticks, timeout.Token);
                if (record != null) { lines.AddRange(ExecutionLogCodec.Decode(record)); truncated |= record.Truncated; }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch { available = false; }
        }
        if (local != null) lines.AddRange(local.Lines);
        var ordered = lines.DistinctBy(x => x.Id).OrderBy(x => x.UnixMs).ToList();
        var size = ordered.Sum(ExecutionLogCodec.LineSize) + 8;
        while (ordered.Count > options.MaxLinesPerExecution || size > options.MaxBytesPerExecution)
        { size -= ExecutionLogCodec.LineSize(ordered[0]); ordered.RemoveAt(0); truncated = true; }
        return new(ordered, truncated, available);
    }

    public const int MaximumBatchSize = 50;

    public async Task<IReadOnlyDictionary<Guid, ExecutionLogReadResult>> ReadBatchAsync(IReadOnlyList<Guid> ids, CancellationToken ct)
    {
        if (ids.Count > MaximumBatchSize) throw new ArgumentOutOfRangeException(nameof(ids));
        var rows = new Dictionary<Guid, ExecutionLogArchive>();
        var available = true;
        if (options.Enabled && options.PersistToDatabase && ids.Count > 0)
        {
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(options.StorageTimeout);
                var storage = services.GetRequiredService<ITickerExecutionLogPersistence>();
                foreach (var row in await storage.ReadBatchAsync((scheduler.RuntimePartition ?? TickerQRuntimePartition.LegacyGlobal).StorageKey,
                    ids, DateTime.UtcNow.Ticks, timeout.Token)) rows[row.TickerId] = row;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch { available = false; }
        }
        var results = new Dictionary<Guid, ExecutionLogReadResult>();
        foreach (var id in ids.Distinct())
        {
            if (!options.Enabled) { results[id] = new(Array.Empty<TickerExecutionLogLine>(), false, true); continue; }
            var local = memory.Snapshot(id);
            var truncated = local?.Truncated == true;
            var readable = available;
            var lines = new List<TickerExecutionLogLine>();
            if (rows.TryGetValue(id, out var row))
            {
                try { lines.AddRange(ExecutionLogCodec.Decode(row)); truncated |= row.Truncated; }
                catch { readable = false; }
            }
            if (local != null) lines.AddRange(local.Lines);
            var ordered = lines.DistinctBy(x => x.Id).OrderBy(x => x.UnixMs).ToList();
            var bytes = ordered.Sum(ExecutionLogCodec.LineSize) + 8;
            while (ordered.Count > options.MaxLinesPerExecution || bytes > options.MaxBytesPerExecution)
            { bytes -= ExecutionLogCodec.LineSize(ordered[0]); ordered.RemoveAt(0); truncated = true; }
            results[id] = new(ordered, truncated, readable);
        }
        return results;
    }
}
