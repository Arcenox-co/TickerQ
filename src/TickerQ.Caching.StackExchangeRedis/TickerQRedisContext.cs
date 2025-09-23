using System.Buffers;
using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using TickerQ.Caching.StackExchangeRedis.DependencyInjection;
using TickerQ.Utilities;
using TickerQ.Utilities.Interfaces;

namespace TickerQ.Caching.StackExchangeRedis;

internal class TickerQRedisContext : ITickerQRedisContext
{
    private readonly IDistributedCache _cache;
    private readonly SchedulerOptionsBuilder _schedulerOptions;
    private readonly ServiceExtension.TickerQRedisOptionBuilder _tickerQRedisOptionBuilder;
    private readonly ITickerQNotificationHubSender _notificationHubSender;

    public TickerQRedisContext([FromKeyedServices("tickerq")] IDistributedCache cache, SchedulerOptionsBuilder schedulerOptions, ServiceExtension.TickerQRedisOptionBuilder tickerQRedisOptionBuilder, ITickerQNotificationHubSender notificationHubSender)
    {
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _schedulerOptions = schedulerOptions ??  throw new ArgumentNullException(nameof(schedulerOptions));
        _tickerQRedisOptionBuilder = tickerQRedisOptionBuilder;
        _notificationHubSender = notificationHubSender;
    }

    public async Task NotifyNodeAliveAsync()
    {
        var node = _schedulerOptions.NodeIdentifier;
        var key  = $"hb:{node}";

        var payload = new
        {
            ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), node
        };

        await _notificationHubSender.UpdateNodeHeartBeatAsync(payload);

        var interval = _tickerQRedisOptionBuilder.NodeHeartbeatInterval; 
        var ttl      = TimeSpan.FromSeconds(interval.TotalSeconds * 3); // 3 × interval

        await _cache.SetStringAsync(key, JsonSerializer.Serialize(payload),
            new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = ttl
            });
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
            // ignored
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