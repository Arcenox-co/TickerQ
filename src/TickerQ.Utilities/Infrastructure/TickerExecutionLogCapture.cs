using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.Extensions.Logging;

namespace TickerQ.Utilities.Infrastructure
{
    /// <summary>
    /// Ambient slot describing the ticker execution that "owns" any ILogger calls
    /// made on the current async flow. Pushed by the execution task handler just
    /// before invoking the ticker function body and restored in a using/finally.
    /// </summary>
    internal sealed class TickerExecutionLogScope
    {
        private static readonly AsyncLocal<TickerExecutionLogScope> Ambient = new AsyncLocal<TickerExecutionLogScope>();

        public static TickerExecutionLogScope Current => Ambient.Value;

        public Guid TickerId { get; }
        public string FunctionName { get; }
        public bool IsEphemeral { get; }

        private TickerExecutionLogScope(Guid tickerId, string functionName, bool isEphemeral)
        {
            TickerId = tickerId;
            FunctionName = functionName;
            IsEphemeral = isEphemeral;
        }

        /// <summary>
        /// Push a new scope onto the async flow. Returns a disposable that restores
        /// the previous scope on dispose — safe to nest (chained child tickers).
        /// </summary>
        public static IDisposable Push(Guid tickerId, string functionName, bool isEphemeral = false)
        {
            var prev = Ambient.Value;
            Ambient.Value = new TickerExecutionLogScope(tickerId, functionName, isEphemeral);
            return new Pop(prev);
        }

        private sealed class Pop : IDisposable
        {
            private readonly TickerExecutionLogScope _prev;
            public Pop(TickerExecutionLogScope prev) { _prev = prev; }
            public void Dispose() { Ambient.Value = _prev; }
        }
    }

    /// <summary>One captured ILogger call, tagged with the execution that produced it.</summary>
    internal sealed class TickerExecutionLogLine
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid TickerId { get; set; }
        public long UnixMs { get; set; }
        public int Level { get; set; }
        public string Message { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string FunctionName { get; set; } = string.Empty;
        /// <summary>Memory-only test output; never offered to the database log writer.</summary>
        public bool IsEphemeral { get; set; }
        public bool Truncated { get; set; }
        public TickerLifecycleEvent LifecycleEvent { get; set; }
        public TickerQ.Utilities.Enums.TickerStatus? Status { get; set; }
        public int Attempt { get; set; }
    }

    /// <summary>
    /// In-memory tail of log lines captured per ticker execution, read by the
    /// dashboard's log-tail endpoint.
    /// </summary>
    internal interface ITickerExecutionLogStore
    {
        void Add(TickerExecutionLogLine line);
        IReadOnlyList<TickerExecutionLogLine> GetTail(Guid tickerId);
        bool IsEphemeral(Guid tickerId) => false;
    }

    /// <summary>
    /// Bounded diagnostic tails. Limits apply to encoded payload bytes as well as line and execution counts.
    /// </summary>
    internal sealed class InMemoryTickerExecutionLogStore : ITickerExecutionLogStore
    {
        private readonly Dictionary<Guid, Buffer> _buffers = new();
        private readonly object _gate = new();
        private readonly ExecutionLogOptions _options;
        private int _bytes;
        private long _evicted;
        private long _droppedUnflushed;
        private long _nextPrune;
        public InMemoryTickerExecutionLogStore(ExecutionLogOptions options = null) => _options = options ?? new();
        public long EvictedExecutions => Interlocked.Read(ref _evicted);
        public (int PendingWrites, long DroppedUnflushedExecutions) Health
        {
            get
            {
                lock (_gate) return (_options.PersistToDatabase
                    ? _buffers.Count(x => !x.Value.IsEphemeral && x.Value.Version > x.Value.PersistedVersion) : 0, _droppedUnflushed);
            }
        }

        public void Add(TickerExecutionLogLine line)
        {
            if (!_options.Enabled) return;
            line.Message = Clip(line.Message, _options.MaxMessageBytes, out var truncated);
            line.Category = Clip(line.Category, 256, out _);
            line.FunctionName = Clip(line.FunctionName, 256, out _);
            line.Truncated |= truncated;
            var size = ExecutionLogCodec.LineSize(line);
            lock (_gate)
            {
                PruneLocked(DateTime.UtcNow.Ticks);
                if (!_buffers.TryGetValue(line.TickerId, out var buffer))
                {
                    while (_buffers.Count >= _options.MaxBufferedExecutions) EvictOldest();
                    _buffers[line.TickerId] = buffer = new Buffer();
                }
                buffer.IsEphemeral |= line.IsEphemeral;
                if (line.LifecycleEvent != TickerLifecycleEvent.None && buffer.Lines.Any(x => x.Line.Id == line.Id)) return;
                buffer.Lines.Enqueue((line, size));
                buffer.Bytes += size; _bytes += size;
                buffer.Truncated |= line.Truncated;
                while (buffer.Lines.Count > _options.MaxLinesPerExecution || buffer.Bytes + 8 > _options.MaxBytesPerExecution)
                {
                    var removed = buffer.Lines.Dequeue();
                    buffer.Bytes -= removed.Size; _bytes -= removed.Size;
                    buffer.Truncated = true;
                }
                buffer.Version = Math.Max(DateTime.UtcNow.Ticks, buffer.Version + 1);
                while (_bytes > _options.MaxBufferedBytes) EvictOldest();
            }
        }

        public IReadOnlyList<TickerExecutionLogLine> GetTail(Guid tickerId) => Snapshot(tickerId)?.Lines ?? Array.Empty<TickerExecutionLogLine>();

        public bool IsEphemeral(Guid tickerId)
        {
            lock (_gate) return _buffers.TryGetValue(tickerId, out var buffer) && buffer.IsEphemeral;
        }

        public ExecutionLogSnapshot Snapshot(Guid id)
        {
            lock (_gate)
            {
                PruneLocked(DateTime.UtcNow.Ticks);
                if (_buffers.TryGetValue(id, out var expired) && DateTime.UtcNow.Ticks - expired.Version > _options.MemoryRetention.Ticks)
                    Remove(id);
                return _buffers.TryGetValue(id, out var b) ? Copy(id, b) : null;
            }
        }

        public ExecutionLogSnapshot[] Pending(int count)
        {
            lock (_gate)
            {
                PruneLocked(DateTime.UtcNow.Ticks);
                return _buffers.Where(x => !x.Value.IsEphemeral && x.Value.Version > x.Value.PersistedVersion)
                    .OrderBy(x => x.Value.PersistedVersion).ThenBy(x => x.Value.Version)
                    .Take(count).Select(x => Copy(x.Key, x.Value)).ToArray();
            }
        }

        public void MarkPersisted(Guid id, long version)
        { lock (_gate) if (_buffers.TryGetValue(id, out var b)) b.PersistedVersion = Math.Max(b.PersistedVersion, version); }

        public void Prune() { lock (_gate) PruneLocked(DateTime.UtcNow.Ticks); }

        private static ExecutionLogSnapshot Copy(Guid id, Buffer b) => new(id, b.Version,
            b.Lines.Select(x => x.Line).ToArray(), b.Truncated);

        private void PruneLocked(long now)
        {
            if (now < _nextPrune) return;
            _nextPrune = now + Math.Min(_options.MemoryRetention.Ticks, TimeSpan.TicksPerMinute);
            foreach (var id in _buffers.Where(x => now - x.Value.Version > _options.MemoryRetention.Ticks).Select(x => x.Key).ToArray())
                Remove(id);
        }

        private void EvictOldest() { Remove(_buffers.MinBy(x => x.Value.Version).Key); Interlocked.Increment(ref _evicted); }
        private void Remove(Guid id)
        {
            var buffer = _buffers[id];
            if (!buffer.IsEphemeral && _options.PersistToDatabase && buffer.Version > buffer.PersistedVersion)
                _droppedUnflushed++;
            _bytes -= buffer.Bytes;
            _buffers.Remove(id);
        }

        internal static string Clip(string value, int maxBytes, out bool truncated)
        {
            value ??= string.Empty;
            truncated = Encoding.UTF8.GetByteCount(value) > maxBytes;
            if (!truncated) return value;
            var bytes = new byte[maxBytes - 3];
            Encoding.UTF8.GetEncoder().Convert(value.AsSpan(), bytes, true, out _, out var used, out _);
            return Encoding.UTF8.GetString(bytes, 0, used) + "…";
        }

        private sealed class Buffer
        {
            public readonly Queue<(TickerExecutionLogLine Line, int Size)> Lines = new();
            public int Bytes;
            public long Version;
            public long PersistedVersion;
            public bool Truncated;
            public bool IsEphemeral;
        }
    }

    /// <summary>
    /// ILoggerProvider that captures every ILogger call made inside an active
    /// <see cref="TickerExecutionLogScope"/> into the log store. Outside of an
    /// execution the logger is a no-op, so registering it globally is safe — it
    /// only sees logs flowing on the async stack of a ticker function body.
    /// </summary>
    internal sealed class TickerExecutionLoggerProvider : ILoggerProvider
    {
        private readonly ITickerExecutionLogStore _store;
        private readonly ExecutionLogOptions _options;

        public TickerExecutionLoggerProvider(ITickerExecutionLogStore store, ExecutionLogOptions options = null)
        {
            _store = store;
            _options = options ?? new();
        }

        public ILogger CreateLogger(string categoryName) => new CaptureLogger(categoryName, _store, _options);

        public void Dispose() { }

        private sealed class CaptureLogger : ILogger
        {
            private readonly string _category;
            private readonly ITickerExecutionLogStore _store;
            private readonly ExecutionLogOptions _options;

            public CaptureLogger(string category, ITickerExecutionLogStore store, ExecutionLogOptions options)
            {
                _category = category;
                _store = store;
                _options = options;
            }

            public IDisposable BeginScope<TState>(TState state) => NullScope.Instance;

            public bool IsEnabled(LogLevel logLevel)
                => _options.Enabled && logLevel >= _options.MinimumLevel && logLevel != LogLevel.None &&
                   TickerExecutionLogScope.Current != null && !IsCategoryExcluded();

            private bool IsCategoryExcluded()
            {
                var prefixes = _options.ExcludedCategoryPrefixes;
                if (prefixes == null || prefixes.Length == 0) return false;
                foreach (var prefix in prefixes)
                {
                    if (!string.IsNullOrEmpty(prefix) &&
                        _category?.StartsWith(prefix, StringComparison.Ordinal) == true)
                        return true;
                }
                return false;
            }

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
                Exception exception, Func<TState, Exception, string> formatter)
            {
                var scope = TickerExecutionLogScope.Current;
                if (!IsEnabled(logLevel)) return;

                string message;
                try { message = formatter(state, exception); }
                catch { return; }

                // The formatter renders any exception placeholders in the user's log
                // template. If it produced nothing (LogError(ex, "")), fall back to the
                // exception's Message — never .ToString(): stack traces belong in the
                // host's logger sinks, not the dashboard log panel.
                if (string.IsNullOrEmpty(message) && exception != null)
                    message = exception.Message ?? string.Empty;

                try
                {
                    if (_options.Redact != null) message = _options.Redact(message);
                    _store.Add(new TickerExecutionLogLine
                    {
                        TickerId = scope.TickerId,
                        UnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                        Level = (int)logLevel,
                        Message = message ?? string.Empty,
                        Category = _category ?? string.Empty,
                        FunctionName = scope.FunctionName ?? string.Empty,
                        IsEphemeral = scope.IsEphemeral
                    });
                }
                catch { /* Diagnostic capture must never fail a job. */ }
            }

            private sealed class NullScope : IDisposable
            {
                public static readonly NullScope Instance = new NullScope();
                public void Dispose() { }
            }
        }
    }
}
