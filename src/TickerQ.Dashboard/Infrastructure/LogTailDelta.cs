using System;
using System.Linq;
using System.Security.Cryptography;
using TickerQ.Utilities.Infrastructure;

namespace TickerQ.Dashboard.Infrastructure;

internal static class LogTailDelta
{
    // A prefix fingerprint also detects late arrivals from another node. If a
    // previous prefix was evicted/changed, replace the bounded tail rather than
    // silently skipping lines with older timestamps or retaining expired lines.
    public static TickerLogBatchItemResponse Create(Guid id, ExecutionLogReadResult result, string cursor)
    {
        using var prefix = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> idBytes = stackalloc byte[16];
        var start = 0;
        var reset = true;
        var next = "empty";
        if (cursor == next) reset = false;
        for (var i = 0; i < result.Lines.Count; i++)
        {
            result.Lines[i].Id.TryWriteBytes(idBytes);
            prefix.AppendData(idBytes);
            next = Convert.ToHexString(prefix.GetCurrentHash());
            if (next == cursor) { start = i + 1; reset = false; }
        }
        return new TickerLogBatchItemResponse
        {
            TickerId = id, Cursor = next, Reset = reset,
            Tail = new TickerLogTailResponseBody
            {
                Truncated = result.Truncated, PersistenceAvailable = result.PersistenceAvailable,
                Lines = result.Lines.Skip(start).Select(line => new TickerLogLineResponse
                {
                    Id = line.Id, UnixMs = line.UnixMs, Level = line.Level, Message = line.Message,
                    Category = line.Category, FunctionName = line.FunctionName,
                    Source = line.LifecycleEvent == TickerLifecycleEvent.None ? string.Empty : "scheduler",
                    LifecycleEvent = line.LifecycleEvent == TickerLifecycleEvent.None ? null : line.LifecycleEvent.ToString(),
                    Status = line.Status?.ToString(), Attempt = line.Attempt
                }).ToList()
            }
        };
    }
}
