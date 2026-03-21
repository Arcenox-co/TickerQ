using Microsoft.Extensions.Hosting;
using TickerQ.SDK.Infrastructure;

namespace TickerQ.SDK.HostedServices;

/// <summary>
/// Sends the list of registered ticker functions to the remote executor on application startup.
/// The delegate itself is not transmitted; instead, a callback name is provided.
/// </summary>
internal sealed class TickerQFunctionRegistrationHostedService : IHostedService
{
    private readonly TickerQFunctionSyncService _syncService;

    public TickerQFunctionRegistrationHostedService(TickerQFunctionSyncService syncService)
    {
        _syncService = syncService ?? throw new ArgumentNullException(nameof(syncService));
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await _syncService.SyncAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
