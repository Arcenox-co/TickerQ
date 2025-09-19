using System;
using System.Threading;
using System.Threading.Tasks;
using TickerQ.Utilities.Interfaces;

namespace TickerQ.Utilities.Temps;

internal class NoOpTickerQRedisContext : ITickerQRedisContext
{
    public Task<TResult[]> GetOrSetArrayAsync<TResult>(string cacheKey, Func<CancellationToken, Task<TResult[]>> factory, TimeSpan? expiration = null,
        CancellationToken cancellationToken = default) where TResult : class
    {
        return factory(cancellationToken);
    }
}