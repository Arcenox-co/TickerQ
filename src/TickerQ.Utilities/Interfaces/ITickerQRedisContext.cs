using System;
using System.Threading;
using System.Threading.Tasks;

namespace TickerQ.Utilities.Interfaces;

internal interface ITickerQRedisContext
{
    public Task<TResult[]> GetOrSetArrayAsync<TResult>(
        string cacheKey,
        Func<CancellationToken, Task<TResult[]>> factory,
        TimeSpan? expiration = null,
        CancellationToken cancellationToken = default) where TResult : class;
}