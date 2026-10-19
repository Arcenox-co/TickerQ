using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Distributed;

namespace TickerQ.Utilities.Interfaces;

internal interface ITickerQRedisContext
{
    // Remove/RemoveAsync also advance array-cache generations to invalidate in-flight fills.
    IDistributedCache DistributedCache { get; }
    public bool HasRedisConnection { get; }
    /// <summary>
    /// Reads a bounded snapshot cache. Null expiration defaults to ten minutes; non-positive
    /// expiration bypasses the cache. Caller cancellation is propagated, cache outages fall
    /// back to the factory, and factory failures are never hidden.
    /// </summary>
    Task<TResult[]> GetOrSetArrayAsync<TResult>(
        string cacheKey,
        Func<CancellationToken, Task<TResult[]>> factory,
        TimeSpan? expiration = null,
        CancellationToken cancellationToken = default) where TResult : class;

    Task<string[]> GetDeadNodesAsync(CancellationToken cancellationToken = default);

    Task NotifyNodeAliveAsync(CancellationToken cancellationToken = default);
    Task AcknowledgeDeadNodeAsync(string owner, CancellationToken cancellationToken = default);
    Task RetryPendingInvalidationsAsync(CancellationToken cancellationToken = default);
}
