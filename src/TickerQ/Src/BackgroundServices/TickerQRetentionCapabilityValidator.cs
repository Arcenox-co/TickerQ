using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using TickerQ.Utilities.Interfaces.Managers;

namespace TickerQ.BackgroundServices
{
    internal sealed class TickerQRetentionCapabilityValidator : IHostedService
    {
        private readonly IInternalTickerManager _manager;

        public TickerQRetentionCapabilityValidator(IInternalTickerManager manager)
        {
            _manager = manager;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!_manager.SupportsRetention)
                throw new NotSupportedException(
                    "TickerQ job retention is configured, but the active persistence provider does not support retention.");
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
