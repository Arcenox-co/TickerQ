using Microsoft.Extensions.Hosting;
using TickerQ.Caching.StackExchangeRedis.DependencyInjection;
using TickerQ.Utilities.Interfaces;

namespace TickerQ.Caching.StackExchangeRedis;

internal class NodeHeartBeatBackgroundService : BackgroundService
{
    private int _started;
    private readonly ITickerQRedisContext _context;
    private readonly PeriodicTimer _tickerHeartBeatPeriodicTimer;

    public NodeHeartBeatBackgroundService(ServiceExtension.TickerQRedisOptionBuilder schedulerOptionsBuilder, ITickerQRedisContext context)
    {
        _context = context;
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
            await _context.NotifyNodeAliveAsync();
        }
    }
       
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        Interlocked.Exchange(ref _started, 0);
        await base.StopAsync(cancellationToken);
    }
}