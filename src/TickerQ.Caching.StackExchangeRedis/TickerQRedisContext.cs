using System.Buffers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using TickerQ.Caching.StackExchangeRedis.DependencyInjection;
using TickerQ.Utilities;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Interfaces;

namespace TickerQ.Caching.StackExchangeRedis;

internal class TickerQRedisContext : ITickerQRedisContext
{
    private readonly IDistributedCache _cache;
    private readonly SchedulerOptionsBuilder _schedulerOptions;
    private readonly ServiceExtension.TickerQRedisOptionBuilder _tickerQRedisOptionBuilder;
    private readonly ITickerQNotificationHubSender _notificationHubSender;
    public IDistributedCache DistributedCache { get; }
    public bool HasRedisConnection => true;

    public TickerQRedisContext([FromKeyedServices("tickerq")] IDistributedCache cache, SchedulerOptionsBuilder schedulerOptions, ServiceExtension.TickerQRedisOptionBuilder tickerQRedisOptionBuilder, ITickerQNotificationHubSender notificationHubSender)
    {
        DistributedCache = cache;
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _schedulerOptions = schedulerOptions ??  throw new ArgumentNullException(nameof(schedulerOptions));
        _tickerQRedisOptionBuilder = tickerQRedisOptionBuilder;
        _notificationHubSender = notificationHubSender;
    }

    public async Task NotifyNodeAliveAsync()
    {
        var node = _schedulerOptions.NodeIdentifier;
        var key  = $"hb:{node}";

        var payload = new NodeHeartbeatPayload
        {
            Ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), Node = node
        };

        await _notificationHubSender.UpdateNodeHeartBeatAsync(payload);

        var interval = _tickerQRedisOptionBuilder.NodeHeartbeatInterval;
        var ttl      = TimeSpan.FromSeconds(interval.TotalSeconds + 20);

        await _cache.SetStringAsync(key, JsonSerializer.Serialize(payload, RedisContextJsonSerializerContext.Default.NodeHeartbeatPayload),
            new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = ttl
            });

        await AddNodeToRegistryAsync(node);
    }
    
    public async Task<string[]> GetDeadNodesAsync()
    {
        // Get all registered nodes
        var nodesJson = await _cache.GetStringAsync("nodes:registry");
        if (string.IsNullOrEmpty(nodesJson)) return [];
    
        var allNodes = JsonSerializer.Deserialize(nodesJson, RedisContextJsonSerializerContext.Default.HashSetString);
        var deadNodes = new HashSet<string>();

        // Check which ones are dead
        foreach (var node in allNodes)
        {
            var heartbeat = await _cache.GetStringAsync($"hb:{node}");
            if (string.IsNullOrEmpty(heartbeat))
            {
                deadNodes.Add(node);
            }
        }
        
        if (deadNodes.Count != 0)
            await RemoveNodesFromRegistryAsync(deadNodes);
        
        //if(deadNodes.Count != 0)
            //Todo notification
        return deadNodes.ToArray();
    }
    
    private async Task RemoveNodesFromRegistryAsync(HashSet<string> nodes)
    {
        var nodesJson = await _cache.GetStringAsync("nodes:registry");
        var nodesList = string.IsNullOrEmpty(nodesJson) 
            ? []
            : JsonSerializer.Deserialize(nodesJson, RedisContextJsonSerializerContext.Default.HashSetString);
        
            nodesList.RemoveWhere(nodes.Contains);
            
            await _cache.SetStringAsync("nodes:registry", JsonSerializer.Serialize(nodesList, RedisContextJsonSerializerContext.Default.HashSetString),
                new DistributedCacheEntryOptions { SlidingExpiration = TimeSpan.FromDays(30) });
    }
    
    private async Task AddNodeToRegistryAsync(string node)
    {
        var nodesJson = await _cache.GetStringAsync("nodes:registry");
        var nodesList = string.IsNullOrEmpty(nodesJson) 
            ? []
            : JsonSerializer.Deserialize(nodesJson, RedisContextJsonSerializerContext.Default.HashSetString);

        if (nodesList.Add(node))
        {
            await _cache.SetStringAsync("nodes:registry", JsonSerializer.Serialize(nodesList, RedisContextJsonSerializerContext.Default.HashSetString),
                new DistributedCacheEntryOptions { SlidingExpiration = TimeSpan.FromDays(30) });
        }
    }
    
    public async Task<TResult[]> GetOrSetArrayAsync<TResult>(string cacheKey,
        Func<CancellationToken, Task<TResult[]>> factory, TimeSpan? expiration = null,
        CancellationToken cancellationToken = default) where TResult : class
    {
        var typeInfo = RedisContextJsonSerializerContext.Default.GetTypeInfo(typeof(TResult[]));

        try
        {
            var cachedBytes = await _cache.GetAsync(cacheKey, cancellationToken);
            if (cachedBytes?.Length > 0)
            {
                var cached = (TResult[])JsonSerializer.Deserialize(cachedBytes, typeInfo);

                if (cached != null)
                    return cached;
            }
        }
        catch (Exception)
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

            JsonSerializer.Serialize(writer, result, typeInfo);
            await writer.FlushAsync(cancellationToken);

            await _cache.SetAsync(cacheKey, bufferWriter.WrittenMemory.ToArray(), cancellationToken);
        }
        catch (Exception)
        {
            // ignored
        }

        return result;
    }
}

internal sealed class NodeHeartbeatPayload
{
    public long Ts { get; set; }
    public string Node { get; set; }
}

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
// Redis context types
[JsonSerializable(typeof(NodeHeartbeatPayload))]
[JsonSerializable(typeof(HashSet<string>))]
// Entity types
[JsonSerializable(typeof(TimeTickerEntity))]
[JsonSerializable(typeof(TimeTickerEntity[]))]
[JsonSerializable(typeof(List<TimeTickerEntity>))]
[JsonSerializable(typeof(CronTickerEntity))]
[JsonSerializable(typeof(CronTickerEntity[]))]
[JsonSerializable(typeof(CronTickerOccurrenceEntity<CronTickerEntity>))]
[JsonSerializable(typeof(CronTickerOccurrenceEntity<CronTickerEntity>[]))]
// Enums
[JsonSerializable(typeof(TickerStatus))]
[JsonSerializable(typeof(RunCondition))]
[JsonSerializable(typeof(RunCondition?))]
// Primitives used in entities
[JsonSerializable(typeof(Guid))]
[JsonSerializable(typeof(Guid?))]
[JsonSerializable(typeof(DateTime))]
[JsonSerializable(typeof(DateTime?))]
[JsonSerializable(typeof(byte[]))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(int))]
[JsonSerializable(typeof(int[]))]
[JsonSerializable(typeof(long))]
[JsonSerializable(typeof(bool))]
internal partial class RedisContextJsonSerializerContext : JsonSerializerContext;