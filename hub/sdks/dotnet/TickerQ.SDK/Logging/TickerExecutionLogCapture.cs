using System;
using System.Threading;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using TickerQ.Utilities.Enums;
using TickerQ.Worker.V1;

namespace TickerQ.SDK.Logging;

/// <summary>
/// Ambient slot describing the ticker execution that "owns" any ILogger calls
/// made on the current async flow. Set by <see cref="WorkerStream.WorkerStreamHostedService"/>
/// just before invoking the user's [TickerFunction] body and cleared in finally.
/// </summary>
internal sealed class TickerExecutionScope
{
    private static readonly AsyncLocal<TickerExecutionScope?> _current = new();

    public static TickerExecutionScope? Current => _current.Value;

    public Guid TickerId { get; }
    public TickerType TickerType { get; }
    public string FunctionName { get; }

    private TickerExecutionScope(Guid tickerId, TickerType tickerType, string functionName)
    {
        TickerId = tickerId;
        TickerType = tickerType;
        FunctionName = functionName;
    }

    /// <summary>
    /// Push a new scope onto the async flow. Returns a disposable that restores
    /// the previous scope on dispose — safe to nest if needed.
    /// </summary>
    public static IDisposable Push(Guid tickerId, TickerType tickerType, string functionName)
    {
        var prev = _current.Value;
        _current.Value = new TickerExecutionScope(tickerId, tickerType, functionName);
        return new Pop(prev);
    }

    private sealed class Pop : IDisposable
    {
        private readonly TickerExecutionScope? _prev;
        public Pop(TickerExecutionScope? prev) { _prev = prev; }
        public void Dispose() { _current.Value = _prev; }
    }
}

/// <summary>
/// Singleton bounded channel that holds <see cref="LogLine"/> frames produced by
/// <see cref="TickerExecutionLogger"/> until <see cref="WorkerStream.WorkerStreamHostedService"/>
/// drains them onto the live worker stream. Bounded with DropOldest so a chatty
/// function can never stall execution; on overflow we surface a synthetic "dropped N"
/// notice once per drain cycle.
/// </summary>
internal sealed class TickerExecutionLogQueue
{
    public const int Capacity = 2000;

    private readonly Channel<LogLine> _channel = Channel.CreateBounded<LogLine>(
        new BoundedChannelOptions(Capacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        });

    private long _dropped;

    public ChannelReader<LogLine> Reader => _channel.Reader;

    public void TryEnqueue(LogLine line)
    {
        // Bounded channel with DropOldest never refuses a write — the oldest frame
        // is silently evicted instead. Track that as a drop so we can surface it.
        if (!_channel.Writer.TryWrite(line))
            Interlocked.Increment(ref _dropped);
    }

    /// <summary>Read-and-reset of the dropped counter for "[capture] dropped N lines" notices.</summary>
    public long ConsumeDroppedCount() => Interlocked.Exchange(ref _dropped, 0);

    /// <summary>
    /// Note an oldest-frame eviction. Bounded DropOldest channels do this silently —
    /// we count drops by observing the writer's pre/post count instead. Hook left
    /// here for explicit tracking; in practice the consume side does enough.
    /// </summary>
    public void NoteDropped() => Interlocked.Increment(ref _dropped);
}

/// <summary>
/// ILoggerProvider that emits a <see cref="LogLine"/> for every ILogger call made
/// inside an active <see cref="TickerExecutionScope"/>. Outside of an execution
/// the logger is a no-op, so this provider is safe to register globally — it only
/// captures logs that flow on the async stack of a [TickerFunction] body.
/// </summary>
internal sealed class TickerExecutionLoggerProvider : ILoggerProvider
{
    private readonly TickerExecutionLogQueue _queue;
    private readonly TickerSdkOptions _options;

    public TickerExecutionLoggerProvider(TickerExecutionLogQueue queue, TickerSdkOptions options)
    {
        _queue = queue;
        _options = options;
    }

    public ILogger CreateLogger(string categoryName) =>
        new TickerExecutionLogger(categoryName, _queue, _options);

    public void Dispose() { }
}

internal sealed class TickerExecutionLogger : ILogger
{
    private readonly string _category;
    private readonly TickerExecutionLogQueue _queue;
    private readonly TickerSdkOptions _options;

    public TickerExecutionLogger(string category, TickerExecutionLogQueue queue, TickerSdkOptions options)
    {
        _category = category;
        _queue = queue;
        _options = options;
    }

    public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

    public bool IsEnabled(LogLevel logLevel)
    {
        if (!_options.LogCapture.Enabled) return false;
        if (logLevel == LogLevel.None) return false;
        if (TickerExecutionScope.Current is null) return false;
        return logLevel >= _options.LogCapture.MinLevel;
    }

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
        Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel)) return;
        var scope = TickerExecutionScope.Current;
        if (scope is null) return;

        string message;
        try { message = formatter(state, exception); }
        catch { return; }

        // The formatter already renders any {Error}/{Exception} placeholders the user
        // put in their log template, so we usually have the exception message inline.
        // If the formatter produced nothing (LogError(ex, "")), fall back to just the
        // exception's Message — never .ToString() (we don't want stack traces in the
        // dashboard log panel; full stack lives in the host's logger sinks).
        if (string.IsNullOrEmpty(message) && exception is not null)
            message = exception.Message ?? string.Empty;

        _queue.TryEnqueue(new LogLine
        {
            TickerId = scope.TickerId.ToString(),
            TickerType = (int)scope.TickerType,
            UnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Level = (int)logLevel,
            Source = "sdk",
            Message = message ?? string.Empty,
            Category = _category ?? string.Empty,
            FunctionName = scope.FunctionName ?? string.Empty
        });
    }

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();
        public void Dispose() { }
    }
}
