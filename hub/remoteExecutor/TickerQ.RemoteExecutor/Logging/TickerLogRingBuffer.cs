using System;
using System.Collections.Generic;

namespace TickerQ.RemoteExecutor.Logging;

/// <summary>A log line received from an SDK or emitted by the scheduler.</summary>
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
/// Recent logs, bounded by lines per execution, execution count and retained bytes.
/// Least recently written executions are evicted first. Sparse executions allocate
/// only a small queue, rather than an entire 500-line ring.
/// </summary>
public sealed class TickerLogRingBuffer
{
    public const int MaximumMessageCharacters = 4096;
    public const int MaximumMetadataCharacters = 512;
    private readonly object _gate = new();
    private readonly Dictionary<Guid, RingState> _byTicker = new();
    private readonly LinkedList<Guid> _oldest = new();
    private readonly int _capacity;
    private readonly int _maxExecutions;
    private readonly long _maxBytes;
    private long _bytes;
    private long _evicted;

    public TickerLogRingBuffer(int capacity = 500, int maxExecutions = 1000, long maxBytes = 16 * 1024 * 1024)
    {
        if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        if (maxExecutions <= 0) throw new ArgumentOutOfRangeException(nameof(maxExecutions));
        if (maxBytes <= 0) throw new ArgumentOutOfRangeException(nameof(maxBytes));
        _capacity = capacity;
        _maxExecutions = maxExecutions;
        _maxBytes = maxBytes;
    }

    public int ExecutionCount { get { lock (_gate) return _byTicker.Count; } }
    public long BufferedBytes { get { lock (_gate) return _bytes; } }
    public long EvictedExecutions { get { lock (_gate) return _evicted; } }

    public void Append(TickerLogLine line)
    {
        if (line == null || line.TickerId == Guid.Empty) return;
        line = Normalize(line);
        var bytes = Size(line);
        lock (_gate)
        {
            if (bytes > _maxBytes) return;
            if (!_byTicker.TryGetValue(line.TickerId, out var state))
            {
                while (_byTicker.Count >= _maxExecutions) EvictOldest();
                state = new RingState(_oldest.AddLast(line.TickerId));
                _byTicker.Add(line.TickerId, state);
            }
            else
            {
                _oldest.Remove(state.Node);
                _oldest.AddLast(state.Node);
            }
            if (state.Lines.Count == _capacity)
            {
                var removedBytes = Size(state.Lines.Dequeue());
                state.Bytes -= removedBytes;
                _bytes -= removedBytes;
            }
            state.Lines.Enqueue(line);
            state.LastTouchedUtc = DateTime.UtcNow;
            state.Bytes += bytes;
            _bytes += bytes;
            while (_bytes > _maxBytes) EvictOldest();
        }
    }

    public IReadOnlyList<TickerLogLine> GetTail(Guid tickerId)
    {
        lock (_gate)
            return _byTicker.TryGetValue(tickerId, out var state) ? state.Lines.ToArray() : Array.Empty<TickerLogLine>();
    }

    /// <summary>Drop entries last written before the cutoff.</summary>
    public int Sweep(DateTime cutoffUtc)
    {
        lock (_gate)
        {
            var evicted = 0;
            while (_oldest.First is { } first && _byTicker[first.Value].LastTouchedUtc < cutoffUtc)
            {
                EvictOldest();
                evicted++;
            }
            return evicted;
        }
    }

    internal static TickerLogLine Normalize(TickerLogLine line)
    {
        if (line.Message is { Length: <= MaximumMessageCharacters } && line.Source is { Length: <= MaximumMetadataCharacters } &&
            line.Category is { Length: <= MaximumMetadataCharacters } && line.FunctionName is { Length: <= MaximumMetadataCharacters })
            return line;
        return new TickerLogLine
        {
            TickerId = line.TickerId, TickerType = line.TickerType, UnixMs = line.UnixMs, Level = line.Level,
            Message = Clip(line.Message, MaximumMessageCharacters), Source = Clip(line.Source, MaximumMetadataCharacters),
            Category = Clip(line.Category, MaximumMetadataCharacters), FunctionName = Clip(line.FunctionName, MaximumMetadataCharacters)
        };
    }

    internal static string Clip(string? text, int maximum)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        if (text.Length <= maximum) return text;
        var length = maximum - 1;
        if (length > 0 && char.IsHighSurrogate(text[length - 1])) length--;
        return text.Substring(0, length) + "…";
    }

    // UTF-16 strings plus a conservative allowance for each line, queue slot and string headers.
    private static long Size(TickerLogLine line) => 256L + 2L *
        (line.Message.Length + line.Source.Length + line.Category.Length + line.FunctionName.Length);

    private void EvictOldest()
    {
        var first = _oldest.First!;
        _bytes -= _byTicker[first.Value].Bytes;
        _byTicker.Remove(first.Value);
        _oldest.RemoveFirst();
        _evicted++;
    }

    private sealed class RingState(LinkedListNode<Guid> node)
    {
        public readonly LinkedListNode<Guid> Node = node;
        public readonly Queue<TickerLogLine> Lines = new(4);
        public long Bytes;
        public DateTime LastTouchedUtc;
    }
}
