using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Models;

namespace TickerQ.Utilities.Interfaces.Managers
{
    internal interface IInternalTickerManager
    {
        Task<(TimeSpan TimeRemaining, InternalFunctionContext[] Functions)> GetNextTickers(
            CancellationToken cancellationToken = default);

        Task ReleaseAcquiredResources(InternalFunctionContext[] context, CancellationToken cancellationToken = default);

        Task ReleaseOrCancelAllAcquiredResources(bool terminateExpiredTicker,
            CancellationToken cancellationToken = default);

        Task SetTickersInProgress(InternalFunctionContext[] context, CancellationToken cancellationToken = default);
        Task SetTickerStatus(InternalFunctionContext context, CancellationToken cancellationToken = default);
        Task<T> GetRequestAsync<T>(Guid tickerId, TickerType type, CancellationToken cancellationToken = default);
        Task<InternalFunctionContext[]> GetTimedOutFunctions(CancellationToken cancellationToken = default);
        Task UpdateTickerRetries(InternalFunctionContext context, CancellationToken cancellationToken = default);

        Task SyncWithDbMemoryCronTickers(IList<(string, string)> cronExpressions,
            CancellationToken cancellationToken = default);

        Task DeleteTicker(Guid tickerId, TickerType type, CancellationToken cancellationToken = default);

        Task CascadeBatchUpdate(Guid parentTickerId, TickerStatus currentStatus,
            CancellationToken cancellationToken = default);
    }
}