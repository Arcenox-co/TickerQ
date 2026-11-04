using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace TickerQ.Utilities.Infrastructure
{
    /// <summary>
    /// A <see cref="BackgroundService"/> whose stop cannot be failed by someone else's cancellation
    /// callback. Stopping cancels the stopping token, which runs every callback registered on it,
    /// including a database driver's for a query still in flight; one that throws makes
    /// <see cref="CancellationTokenSource.Cancel()"/> throw. The token is cancelled by then, and the
    /// base has still waited for <see cref="BackgroundService.ExecuteTask"/> as it always does, so
    /// the stop happened: the failure is reported instead of failing the host's or Dashboard's stop.
    /// </summary>
    internal abstract class TickerQBackgroundService(ILogger logger) : BackgroundService
    {
        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            try
            {
                await base.StopAsync(cancellationToken).ConfigureAwait(false);
            }
            // The base rethrows only what cancelling the stopping token threw.
            catch (AggregateException exception)
            {
                logger.LogWarning(exception,
                    "A cancellation callback failed while {Service} stopped; the service stopped normally",
                    GetType().Name);
            }
        }
    }
}
