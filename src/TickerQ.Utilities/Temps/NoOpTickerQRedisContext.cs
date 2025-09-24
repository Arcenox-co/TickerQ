using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TickerQ.Utilities.Interfaces;

namespace TickerQ.Utilities.Temps;

internal class NoOpTickerQRedisContext : ITickerQRedisContext
{
    public bool HasRedisConnection => false;

    public Task<TResult[]> GetOrSetArrayAsync<TResult>(string cacheKey, Func<CancellationToken, Task<TResult[]>> factory, TimeSpan? expiration = null,
        CancellationToken cancellationToken = default) where TResult : class
    {
        return factory(cancellationToken);
    }

    public Task<string[]> GetDeadNodesAsync()
    {
        return Task.FromResult(Array.Empty<string>());
    }

    public Task NotifyNodeAliveAsync()
    {
       return Task.CompletedTask;
    }
}