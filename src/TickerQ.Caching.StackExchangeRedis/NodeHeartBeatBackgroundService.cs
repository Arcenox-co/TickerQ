using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TickerQ.Caching.StackExchangeRedis.DependencyInjection;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Interfaces.Managers;

namespace TickerQ.Caching.StackExchangeRedis;

internal class NodeHeartBeatBackgroundService : BackgroundService
{
    private int _started;
    private readonly ITickerQRedisContext _context;
    private readonly TimeSpan _heartbeatInterval;
    private readonly IInternalTickerManager  _internalTickerManager;
    private readonly ILogger<NodeHeartBeatBackgroundService> _logger;
    private readonly ITickerQActivationGate _activationGate;

    public NodeHeartBeatBackgroundService(ServiceExtension.TickerQRedisOptionBuilder schedulerOptionsBuilder,
        ITickerQRedisContext context, IInternalTickerManager internalTickerManager,
        ILogger<NodeHeartBeatBackgroundService> logger, ITickerQActivationGate activationGate)
    {
        _context = context;
        _internalTickerManager = internalTickerManager;
        _logger = logger;
        _activationGate = activationGate ?? throw new ArgumentNullException(nameof(activationGate));
        _heartbeatInterval = schedulerOptionsBuilder.NodeHeartbeatInterval;
        if (_heartbeatInterval <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(schedulerOptionsBuilder.NodeHeartbeatInterval));
    }
    
    public override Task StartAsync(CancellationToken ct)
    {
        return Interlocked.CompareExchange(ref _started, 1, 0) != 0 
            ? Task.CompletedTask : base.StartAsync(ct);
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
        => Task.WhenAll(
            RunActivatedLoopAsync(_context.NotifyNodeAliveAsync, "renewal", stoppingToken),
            RunActivatedLoopAsync(RecoverDeadNodesAsync, "recovery", stoppingToken),
            RunLoopAsync(_context.RetryPendingInvalidationsAsync, TimeSpan.FromSeconds(5), "cache invalidation", stoppingToken));

    private async Task RunActivatedLoopAsync(Func<CancellationToken, Task> action, string operation, CancellationToken ct)
    {
        try
        {
            await _activationGate.WaitForActivationAsync(ct).ConfigureAwait(false);
            await RunLoopAsync(action, _heartbeatInterval, operation, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
    }

    private async Task RunLoopAsync(Func<CancellationToken, Task> action, TimeSpan interval, string operation, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var delay = interval;
                try { await action(ct).ConfigureAwait(false); }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
                catch (Exception error)
                {
                    _logger.LogError(error, "TickerQ Redis {Operation} failed; it will be retried.", operation);
                    delay = TimeSpan.FromMilliseconds(Math.Min(interval.TotalMilliseconds, 5000));
                }
                await Task.Delay(delay, ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
    }

    internal async Task RecoverDeadNodesAsync(CancellationToken ct)
    {
        var deadNodes = await _context.GetDeadNodesAsync(ct).ConfigureAwait(false);
        foreach (var owner in deadNodes)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                await _internalTickerManager.ReleaseDeadNodeResources(owner, ct).ConfigureAwait(false);
                await _context.AcknowledgeDeadNodeAsync(owner, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception error)
            {
                _logger.LogError(error, "TickerQ recovery for owner {Owner} failed; registration is retained for retry.", owner);
            }
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        Interlocked.Exchange(ref _started, 0);
        await base.StopAsync(cancellationToken).ConfigureAwait(false);
    }
}
