using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;

namespace TickerQ.RemoteExecutor.Logging;

/// <summary>
/// Background hosted service that periodically evicts ticker log buffers
/// whose last-touched timestamp is older than the TTL. Without this the
/// in-memory buffer would grow unboundedly across the process lifetime —
/// every executed ticker leaves behind a 500-line slot.
/// </summary>
internal sealed class TickerLogRingBufferSweeper : BackgroundService
{
    private static readonly TimeSpan SweepInterval = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(30);

    private readonly TickerLogRingBuffer _buffer;

    public TickerLogRingBufferSweeper(TickerLogRingBuffer buffer)
    {
        _buffer = buffer;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await Task.Delay(SweepInterval, stoppingToken).ConfigureAwait(false); }
            catch (OperationCanceledException) { return; }

            try { _buffer.Sweep(DateTime.UtcNow - Ttl); }
            catch { /* sweeper is best-effort; never crash the host */ }
        }
    }
}
