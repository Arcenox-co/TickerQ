using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TickerQ.SDK.Infrastructure;

namespace TickerQ.SDK.HostedServices;

/// <summary>
/// Sends the list of registered ticker functions to the remote executor on application startup.
/// Retries with exponential backoff if Hub or RemoteExecutor is unavailable.
/// </summary>
internal sealed class TickerQFunctionRegistrationHostedService : IHostedService
{
    private readonly TickerQFunctionSyncService _syncService;
    private readonly TickerSdkOptions _options;
    private readonly ILogger<TickerQFunctionRegistrationHostedService>? _logger;

    private const int MaxRetries = 10;
    private static readonly TimeSpan InitialDelay = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan MaxDelay = TimeSpan.FromSeconds(60);

    public TickerQFunctionRegistrationHostedService(
        TickerQFunctionSyncService syncService,
        TickerSdkOptions options,
        ILogger<TickerQFunctionRegistrationHostedService>? logger = null)
    {
        _syncService = syncService ?? throw new ArgumentNullException(nameof(syncService));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var delay = InitialDelay;

        for (var attempt = 1; attempt <= MaxRetries; attempt++)
        {
            try
            {
                await _syncService.SyncAsync(cancellationToken).ConfigureAwait(false);
                _logger?.LogInformation("TickerQ SDK synced successfully with Hub ({HubUrl})", _options.HubUri);
                return;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                var remoteExecutorUrl = _options.ApiUri?.ToString() ?? "not yet resolved";

                _logger?.LogWarning(
                    "TickerQ SDK sync failed (attempt {Attempt}/{MaxRetries}). " +
                    "Hub: {HubUrl} | RemoteExecutor: {RemoteExecutorUrl} | Error: {Error}. " +
                    "Retrying in {Delay}s...",
                    attempt, MaxRetries, _options.HubUri, remoteExecutorUrl,
                    ex.Message, delay.TotalSeconds);

                if (attempt == MaxRetries)
                {
                    _logger?.LogError(ex,
                        "TickerQ SDK failed to sync after {MaxRetries} attempts. " +
                        "Ensure Hub ({HubUrl}) and RemoteExecutor ({RemoteExecutorUrl}) are reachable.",
                        MaxRetries, _options.HubUri, remoteExecutorUrl);
                    throw;
                }

                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                delay = TimeSpan.FromSeconds(Math.Min(delay.TotalSeconds * 2, MaxDelay.TotalSeconds));
            }
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
