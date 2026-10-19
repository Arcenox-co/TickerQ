using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using TickerQ.Utilities.Interfaces.Managers;

namespace TickerQ.BackgroundServices;

/// <summary>Fails host startup before reconciliation when the provider cannot durably fence an activation epoch.</summary>
internal sealed class TickerQReconciliationActivationCapabilityValidator : IHostedService
{
    private readonly IInternalTickerManager _manager;

    public TickerQReconciliationActivationCapabilityValidator(IInternalTickerManager manager) =>
        _manager = manager ?? throw new ArgumentNullException(nameof(manager));

    public Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_manager.SupportsReconciliationActivationEpoch)
            throw new NotSupportedException(
                "The configured persistence provider does not support the durable reconciliation activation epoch required before scheduling. Upgrade the provider or drain old writers and perform a controlled single-node activation.");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}