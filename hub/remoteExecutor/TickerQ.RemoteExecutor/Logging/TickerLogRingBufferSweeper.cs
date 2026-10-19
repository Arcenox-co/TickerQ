using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using TickerQ.Utilities.Interfaces;
using TickerQ.RemoteExecutor.TunnelClient;

namespace TickerQ.RemoteExecutor.Logging;

/// <summary>
/// Background hosted service that periodically evicts ticker log buffers
/// whose last-touched timestamp is older than the TTL, releasing inactive
/// execution logs and lifecycle state before the global limits are reached.
/// </summary>
internal sealed class TickerLogRingBufferSweeper : BackgroundService
{
    private static readonly TimeSpan SweepInterval = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(30);

    private readonly TickerLogRingBuffer _buffer;
    private readonly TunnelTickerQNotificationHubSender? _sender;

    public TickerLogRingBufferSweeper(TickerLogRingBuffer buffer, ITickerQNotificationHubSender? sender = null)
    {
        _buffer = buffer;
        _sender = sender as TunnelTickerQNotificationHubSender;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await Task.Delay(SweepInterval, stoppingToken).ConfigureAwait(false); }
            catch (OperationCanceledException) { return; }

            try
            {
                _buffer.Sweep(DateTime.UtcNow - Ttl);
                _sender?.SweepLifecycleState();
            }
            catch { /* sweeper is best-effort; never crash the host */ }
        }
    }
}
