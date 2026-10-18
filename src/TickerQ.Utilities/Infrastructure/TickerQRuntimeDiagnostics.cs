using System;
using System.Diagnostics.Metrics;
using System.Threading;
using TickerQ.Utilities.Instrumentation;

namespace TickerQ.Utilities.Infrastructure;

/// <summary>Node-local health; no exception messages, job payloads or customer data.</summary>
internal sealed class TickerQRuntimeDiagnostics : IDisposable
{
    private readonly Meter _meter = new(TickerQMetrics.MeterName);
    private long _lastFallbackSuccess, _lastFallbackFailure, _fallbackFailures;
    private long _lastFlushSuccess, _lastFlushFailure, _flushFailures;
    private int _writeState;

    public TickerQRuntimeDiagnostics(InMemoryTickerExecutionLogStore memory)
    {
        _meter.CreateObservableGauge("tickerq.fallback.last_success", () => UnixSeconds(LastSuccessfulFallbackScanUtc), "s");
        _meter.CreateObservableCounter("tickerq.fallback.failures", () => FallbackScanFailures, "{failure}");
        _meter.CreateObservableGauge("tickerq.logs.pending_writes", () => memory.Health.PendingWrites, "{tail}");
        _meter.CreateObservableCounter("tickerq.logs.dropped_unflushed", () => memory.Health.DroppedUnflushedExecutions, "{tail}");
        _meter.CreateObservableGauge("tickerq.logs.last_successful_flush", () => UnixSeconds(LastSuccessfulLogFlushUtc), "s");
        _meter.CreateObservableCounter("tickerq.logs.write_failures", () => LogWriteFailures, "{failure}");
        _meter.CreateObservableGauge("tickerq.logs.write_available", () => LogWritesAvailable is { } value ? value ? 1 : 0 : -1);
    }

    public DateTimeOffset? LastSuccessfulFallbackScanUtc => Timestamp(Interlocked.Read(ref _lastFallbackSuccess));
    public DateTimeOffset? LastFallbackFailureUtc => Timestamp(Interlocked.Read(ref _lastFallbackFailure));
    public long FallbackScanFailures => Interlocked.Read(ref _fallbackFailures);
    public DateTimeOffset? LastSuccessfulLogFlushUtc => Timestamp(Interlocked.Read(ref _lastFlushSuccess));
    public DateTimeOffset? LastLogWriteFailureUtc => Timestamp(Interlocked.Read(ref _lastFlushFailure));
    public long LogWriteFailures => Interlocked.Read(ref _flushFailures);
    public bool? LogWritesAvailable => Volatile.Read(ref _writeState) switch { 1 => true, -1 => false, _ => null };
    public void FallbackSucceeded() => Interlocked.Exchange(ref _lastFallbackSuccess, DateTime.UtcNow.Ticks);
    public void FallbackFailed()
    {
        Interlocked.Increment(ref _fallbackFailures);
        Interlocked.Exchange(ref _lastFallbackFailure, DateTime.UtcNow.Ticks);
    }
    public void FlushSucceeded()
    {
        Interlocked.Exchange(ref _lastFlushSuccess, DateTime.UtcNow.Ticks);
        Volatile.Write(ref _writeState, 1);
    }
    public void FlushFailed()
    {
        Interlocked.Increment(ref _flushFailures);
        Interlocked.Exchange(ref _lastFlushFailure, DateTime.UtcNow.Ticks);
        Volatile.Write(ref _writeState, -1);
    }
    private static DateTimeOffset? Timestamp(long ticks) => ticks == 0 ? null : new DateTimeOffset(ticks, TimeSpan.Zero);
    private static long UnixSeconds(DateTimeOffset? value) => value?.ToUnixTimeSeconds() ?? 0;
    public void Dispose() => _meter.Dispose();
}
