using Microsoft.Extensions.Hosting;
using TickerQ.Caching.StackExchangeRedis.DependencyInjection;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Interfaces.Managers;

namespace TickerQ.Caching.StackExchangeRedis;

internal class NodeHeartBeatBackgroundService : BackgroundService
{
    private int _started;
    private readonly ITickerQRedisContext _context;
    private readonly PeriodicTimer _tickerHeartBeatPeriodicTimer;
    private readonly IInternalTickerManager  _internalTickerManager;

    public NodeHeartBeatBackgroundService(ServiceExtension.TickerQRedisOptionBuilder schedulerOptionsBuilder, ITickerQRedisContext context, IInternalTickerManager internalTickerManager)
    {
        _context = context;
        _internalTickerManager = internalTickerManager;
        _tickerHeartBeatPeriodicTimer = new PeriodicTimer(schedulerOptionsBuilder.NodeHeartbeatInterval);
    }
    
    public override Task StartAsync(CancellationToken ct)
    {
        return Interlocked.CompareExchange(ref _started, 1, 0) != 0 
            ? Task.CompletedTask : base.StartAsync(ct);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RunTickerQFallbackAsync(stoppingToken);
    }
    
    private async Task RunTickerQFallbackAsync(CancellationToken stoppingToken)
    {
        await _context.NotifyNodeAliveAsync();

        while (await _tickerHeartBeatPeriodicTimer.WaitForNextTickAsync(stoppingToken))
        {
            var deadNodes = await _context.GetDeadNodesAsync();
            
            if (deadNodes.Length != 0)
            {
                foreach (var deadNode in deadNodes)
                {
                    await _internalTickerManager.ReleaseDeadNodeResources(deadNode, stoppingToken);
                }
            }
            
            await _context.NotifyNodeAliveAsync();
        }
    }
       
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        Interlocked.Exchange(ref _started, 0);
        await base.StopAsync(cancellationToken);
    }
}