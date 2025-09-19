using System.Buffers;
using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using TickerQ.Utilities.Interfaces;

namespace TickerQ.Caching.StackExchangeRedis;

public class TickerQRedisContext : ITickerQRedisContext
{
    private readonly IDistributedCache _cache;

    public TickerQRedisContext([FromKeyedServices("tickerq")] IDistributedCache cache)
    {
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
    }

    public async Task<TResult[]> GetOrSetArrayAsync<TResult>(string cacheKey,
        Func<CancellationToken, Task<TResult[]>> factory, TimeSpan? expiration = null,
        CancellationToken cancellationToken = default) where TResult : class
    {
        try
        {
            var cachedBytes = await _cache.GetAsync(cacheKey, cancellationToken);
            if (cachedBytes?.Length > 0)
            {
                ReadOnlySpan<byte> cachedSpan = cachedBytes.AsSpan();
                var cached = JsonSerializer.Deserialize<TResult[]>(cachedSpan);

                if (cached != null)
                    return cached;
            }
        }
        catch (Exception ex)
        {
        }

        var result = await factory(cancellationToken);

        if (result == null)
            return null;

        try
        {
            var bufferWriter = new ArrayBufferWriter<byte>();
            await using var writer = new Utf8JsonWriter(bufferWriter);

            JsonSerializer.Serialize(writer, result);
            await writer.FlushAsync(cancellationToken);

            await _cache.SetAsync(cacheKey, bufferWriter.WrittenMemory.ToArray(), cancellationToken);
        }
        catch (Exception ex) { }
        return null;
    }
}