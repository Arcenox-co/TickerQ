using System;
using System.Threading;
using System.Threading.Tasks;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Models;

namespace TickerQ.Utilities.Interfaces.Managers
{
    internal interface IInternalTickerManager
    {
        Task<(TimeSpan TimeRemaining, InternalFunctionContext[] Functions)> GetNextTickers(CancellationToken cancellationToken = default);
        Task ReleaseAcquiredResources(InternalFunctionContext[] context, CancellationToken cancellationToken = default);
        Task SetTickersInProgress(InternalFunctionContext[] context, CancellationToken cancellationToken = default);
        Task UpdateTickerAsync(InternalFunctionContext context, CancellationToken cancellationToken = default);
        Task<T> GetRequestAsync<T>(Guid tickerId, TickerType type, CancellationToken cancellationToken = default);
        Task<InternalFunctionContext[]> RunTimedOutTickers(CancellationToken cancellationToken = default);
        Task MigrateDefinedCronTickers((string, string)[] cronExpressions, CancellationToken cancellationToken = default);
        Task DeleteTicker(Guid tickerId, TickerType type, CancellationToken cancellationToken = default);
        Task ReleaseDeadNodeResources(string instanceIdentifier, CancellationToken cancellationToken = default);
        Task UpdateSkipTimeTickersWithUnifiedContextAsync(InternalFunctionContext[] context, CancellationToken cancellationToken = default);
    }
}