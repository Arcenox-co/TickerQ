using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using TickerQ.Utilities.Interfaces.Managers;

namespace TickerQ.BackgroundServices;

/// <summary>
/// Fails startup before initialization, scheduler, worker, or maintenance hosted services can
/// produce side effects when retention was enabled against an unsupported persistence provider.
/// </summary>
internal sealed class TickerQRetentionCapabilityValidator : IHostedService
{
    private readonly IInternalTickerManager _internalTickerManager;

    public TickerQRetentionCapabilityValidator(IInternalTickerManager internalTickerManager)
    {
        _internalTickerManager = internalTickerManager;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_internalTickerManager.SupportsRetention)
            throw new NotSupportedException(
                "TickerQ job retention is configured (ConfigureJobRetention) but the active persistence " +
                "provider does not support retention. Remove the retention configuration or switch to a " +
                "provider that implements it (EF Core, MongoDB, Redis, or the in-memory provider).");

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
