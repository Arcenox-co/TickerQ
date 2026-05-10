using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace TickerQ.RemoteExecutor.Logging;

/// <summary>
/// One execution log line. Plain DTO — the buffer never stores protobuf-generated
/// types directly so this layer doesn't depend on the gRPC code-gen.
/// </summary>
public sealed class TickerLogLine
{
    public Guid TickerId { get; init; }
    public int TickerType { get; init; }
    public long UnixMs { get; init; }
    public int Level { get; init; }
    public string Source { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public string FunctionName { get; init; } = string.Empty;
}

/// <summary>
/// Per-ticker in-memory ring buffer of recent log lines. Captures SDK ILogger
/// output forwarded over the worker stream plus any scheduler-side lifecycle
/// events. Used by the dashboard panel's tail-on-open hydration; live tailing
/// goes through the existing tunnel notification fan-out and doesn't read here.
///
/// Bounded per ticker (default 500 lines) and time-decayed by
/// <see cref="TickerLogRingBufferSweeper"/>.
/// </summary>
public sealed class TickerLogRingBuffer
{
    private readonly ConcurrentDictionary<Guid, RingState> _byTicker = new();
    private readonly int _capacity;

    public TickerLogRingBuffer(int capacity = 500)
    {
        if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        _capacity = capacity;
    }

    public void Append(TickerLogLine line)
    {
        if (line == null || line.TickerId == Guid.Empty) return;
        var state = _byTicker.GetOrAdd(line.TickerId, _ => new RingState(_capacity));
        state.Append(line);
    }

    public IReadOnlyList<TickerLogLine> GetTail(Guid tickerId)
    {
        return _byTicker.TryGetValue(tickerId, out var state)
            ? state.Snapshot()
            : Array.Empty<TickerLogLine>();
    }

    /// <summary>Drop entries whose last-touched timestamp is older than <paramref name="cutoffUtc"/>.</summary>
    public int Sweep(DateTime cutoffUtc)
    {
        var evicted = 0;
        foreach (var kv in _byTicker)
        {
            if (kv.Value.LastTouchedUtc < cutoffUtc &&
                _byTicker.TryRemove(kv.Key, out _))
            {
                evicted++;
            }
        }
        return evicted;
    }

    private sealed class RingState
    {
        private readonly TickerLogLine[] _ring;
        private readonly object _lock = new();
        private int _next;
        private int _count;

        public DateTime LastTouchedUtc { get; private set; } = DateTime.UtcNow;

        public RingState(int capacity) { _ring = new TickerLogLine[capacity]; }

        public void Append(TickerLogLine line)
        {
            lock (_lock)
            {
                _ring[_next] = line;
                _next = (_next + 1) % _ring.Length;
                if (_count < _ring.Length) _count++;
                LastTouchedUtc = DateTime.UtcNow;
            }
        }

        public IReadOnlyList<TickerLogLine> Snapshot()
        {
            lock (_lock)
            {
                var result = new TickerLogLine[_count];
                var start = _count < _ring.Length ? 0 : _next;
                for (var i = 0; i < _count; i++)
                    result[i] = _ring[(start + i) % _ring.Length];
                return result;
            }
        }
    }
}
