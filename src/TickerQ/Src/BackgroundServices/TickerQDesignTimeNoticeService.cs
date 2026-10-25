using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace TickerQ.BackgroundServices;

/// <summary>
/// Replaces TickerQ's hosted services when a design-time tool starts the host, so the tool's log
/// says why no scheduler, store, or license work ran.
/// </summary>
internal sealed class TickerQDesignTimeNoticeService : IHostedService
{
    private readonly string _toolName;
    private readonly ILogger<TickerQDesignTimeNoticeService> _logger;

    public TickerQDesignTimeNoticeService(string toolName, ILogger<TickerQDesignTimeNoticeService> logger)
    {
        _toolName = toolName;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("TickerQ did not start: the host is running under the design-time tool {Tool}.", _toolName);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
