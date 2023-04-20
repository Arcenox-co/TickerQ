using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TickerQ.Utilities.Enums;

namespace TickerQ.Utilities.Interfaces
{
    internal interface IInternalTickerManager
    {
        Task<(TimeSpan TimeRemaining, (string, Guid, TickerType)[] Functions)> GetNextTickers(CancellationToken cancellationToken = default);
        Task ReleaseAcquiredResources(IEnumerable<(Guid TickerId, TickerType type)> resources, CancellationToken cancellationToken = default);
        Task SetTickersInprogress(IEnumerable<(Guid TickerId, TickerType type)> resources, CancellationToken cancellationToken = default);
        Task SetTickerStatus(Guid tickerId, TickerType tickerType, TickerStatus tickerStatus, CancellationToken cancellationToken = default);
        Task<T> GetRequest<T>(Guid tickerId, TickerType type, CancellationToken cancellationToken = default);
        Task<(string, Guid, TickerType)[]> GetTimeoutedFunctions(CancellationToken cancellationToken = default);
    }
}
