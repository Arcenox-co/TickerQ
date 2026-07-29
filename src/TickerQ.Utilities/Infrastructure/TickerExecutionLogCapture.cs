using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
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

        private TickerExecutionLogScope(Guid tickerId, string functionName)
        {
            TickerId = tickerId;
            FunctionName = functionName;
        }

        /// <summary>
        /// Push a new scope onto the async flow. Returns a disposable that restores
        /// the previous scope on dispose — safe to nest (chained child tickers).
        /// </summary>
        public static IDisposable Push(Guid tickerId, string functionName)
        {
            var prev = Ambient.Value;
            Ambient.Value = new TickerExecutionLogScope(tickerId, functionName);
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
        public Guid TickerId { get; set; }
        public long UnixMs { get; set; }
        public int Level { get; set; }
        public string Message { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string FunctionName { get; set; } = string.Empty;
    }

    /// <summary>
    /// In-memory tail of log lines captured per ticker execution, read by the
    /// dashboard's log-tail endpoint.
    /// </summary>
    internal interface ITickerExecutionLogStore
    {
        void Add(TickerExecutionLogLine line);
        IReadOnlyList<TickerExecutionLogLine> GetTail(Guid tickerId);
    }

    /// <summary>
    /// Bounded per-ticker ring buffers with a ~30 minute idle TTL, so a chatty
    /// function can never grow memory unbounded and finished runs stay inspectable
    /// for a while. Mirrors the SDK-side capture contract.
    /// </summary>
    internal sealed class InMemoryTickerExecutionLogStore : ITickerExecutionLogStore
    {
        private const int MaxLinesPerTicker = 1000;
        private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(30);
        private static readonly TimeSpan SweepInterval = TimeSpan.FromMinutes(1);

        private readonly ConcurrentDictionary<Guid, Buffer> _buffers = new ConcurrentDictionary<Guid, Buffer>();
        private long _lastSweepUtcTicks;

        public void Add(TickerExecutionLogLine line)
        {
            _buffers.GetOrAdd(line.TickerId, _ => new Buffer()).Add(line);
            SweepIfDue();
        }

        public IReadOnlyList<TickerExecutionLogLine> GetTail(Guid tickerId)
            => _buffers.TryGetValue(tickerId, out var buffer)
                ? buffer.Snapshot()
                : Array.Empty<TickerExecutionLogLine>();

        // Amortized eviction on the write path — no timer/hosted service needed.
        private void SweepIfDue()
        {
            var now = DateTime.UtcNow.Ticks;
            var last = Interlocked.Read(ref _lastSweepUtcTicks);
            if (now - last < SweepInterval.Ticks) return;
            if (Interlocked.CompareExchange(ref _lastSweepUtcTicks, now, last) != last) return;

            foreach (var kv in _buffers)
            {
                if (now - kv.Value.LastWriteUtcTicks > Ttl.Ticks)
                    _buffers.TryRemove(kv.Key, out _);
            }
        }

        private sealed class Buffer
        {
            private readonly Queue<TickerExecutionLogLine> _lines = new Queue<TickerExecutionLogLine>();
            private long _lastWriteUtcTicks = DateTime.UtcNow.Ticks;

            public long LastWriteUtcTicks => Interlocked.Read(ref _lastWriteUtcTicks);

            public void Add(TickerExecutionLogLine line)
            {
                lock (_lines)
                {
                    _lines.Enqueue(line);
                    if (_lines.Count > MaxLinesPerTicker)
                        _lines.Dequeue();
                }
                Interlocked.Exchange(ref _lastWriteUtcTicks, DateTime.UtcNow.Ticks);
            }

            public IReadOnlyList<TickerExecutionLogLine> Snapshot()
            {
                lock (_lines)
                    return _lines.ToArray();
            }
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

        public TickerExecutionLoggerProvider(ITickerExecutionLogStore store)
        {
            _store = store;
        }

        public ILogger CreateLogger(string categoryName) => new CaptureLogger(categoryName, _store);

        public void Dispose() { }

        private sealed class CaptureLogger : ILogger
        {
            private readonly string _category;
            private readonly ITickerExecutionLogStore _store;

            public CaptureLogger(string category, ITickerExecutionLogStore store)
            {
                _category = category;
                _store = store;
            }

            public IDisposable BeginScope<TState>(TState state) => NullScope.Instance;

            public bool IsEnabled(LogLevel logLevel)
                => logLevel != LogLevel.None && TickerExecutionLogScope.Current != null;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
                Exception exception, Func<TState, Exception, string> formatter)
            {
                var scope = TickerExecutionLogScope.Current;
                if (scope == null || logLevel == LogLevel.None) return;

                string message;
                try { message = formatter(state, exception); }
                catch { return; }

                // The formatter renders any exception placeholders in the user's log
                // template. If it produced nothing (LogError(ex, "")), fall back to the
                // exception's Message — never .ToString(): stack traces belong in the
                // host's logger sinks, not the dashboard log panel.
                if (string.IsNullOrEmpty(message) && exception != null)
                    message = exception.Message ?? string.Empty;

                _store.Add(new TickerExecutionLogLine
                {
                    TickerId = scope.TickerId,
                    UnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    Level = (int)logLevel,
                    Message = message ?? string.Empty,
                    Category = _category ?? string.Empty,
                    FunctionName = scope.FunctionName ?? string.Empty
                });
            }

            private sealed class NullScope : IDisposable
            {
                public static readonly NullScope Instance = new NullScope();
                public void Dispose() { }
            }
        }
    }
}
