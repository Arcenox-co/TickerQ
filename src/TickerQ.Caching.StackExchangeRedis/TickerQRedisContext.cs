using System.Buffers;
using StackExchange.Redis;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TickerQ.Caching.StackExchangeRedis.DependencyInjection;
using TickerQ.Caching.StackExchangeRedis.Infrastructure;
using TickerQ.Caching.StackExchangeRedis.Helpers;
using TickerQ.Utilities;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Models;

namespace TickerQ.Caching.StackExchangeRedis;

internal class TickerQRedisContext : ITickerQRedisContext
{
    private readonly IDistributedCache _cache;
    private readonly IDatabase _database;
    private readonly InvalidatingDistributedCache _invalidatingCache;
    private Task _heartbeatNotification = Task.CompletedTask;
    private readonly object _notificationLock = new();
    private static readonly string AcknowledgeDeadNodeScript = LuaScriptLoader.Load("AcknowledgeDeadNode");
    private readonly SchedulerOptionsBuilder _schedulerOptions;
    private readonly ServiceExtension.TickerQRedisOptionBuilder _tickerQRedisOptionBuilder;
    private readonly ITickerQNotificationHubSender _notificationHubSender;
    private readonly RedisKeyBuilder _keys;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<TickerQRedisContext> _logger;
    internal static readonly TimeSpan DefaultArrayCacheLifetime = TimeSpan.FromMinutes(10);
    private const int ArrayCacheFormat = 1;
    public IDistributedCache DistributedCache { get; }
    public bool HasRedisConnection => true;

    public TickerQRedisContext([FromKeyedServices("tickerq")] IDistributedCache cache, SchedulerOptionsBuilder schedulerOptions, ServiceExtension.TickerQRedisOptionBuilder tickerQRedisOptionBuilder, ITickerQNotificationHubSender notificationHubSender,
        ILogger<TickerQRedisContext> logger = null, TimeProvider timeProvider = null,
        [FromKeyedServices("tickerq")] IDatabase database = null)
    {
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        DistributedCache = _invalidatingCache = new InvalidatingDistributedCache(_cache);
        _database = database;
        _logger = logger ?? NullLogger<TickerQRedisContext>.Instance;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _schedulerOptions = schedulerOptions ??  throw new ArgumentNullException(nameof(schedulerOptions));
        _keys = new RedisKeyBuilder(_schedulerOptions.RuntimePartition ?? TickerQ.Utilities.Models.TickerQRuntimePartition.LegacyGlobal);
        _tickerQRedisOptionBuilder = tickerQRedisOptionBuilder;
        _notificationHubSender = notificationHubSender;
    }

    private string PhysicalKey(string key) => $"{_tickerQRedisOptionBuilder.InstanceName}{key}";

    public async Task NotifyNodeAliveAsync(CancellationToken cancellationToken = default)
    {
        var owner = _schedulerOptions.ExecutionOwnerId;
        var payload = new NodeHeartbeatPayload
        {
            Ts = _timeProvider.GetUtcNow().ToUnixTimeMilliseconds(), Node = _schedulerOptions.NodeIdentifier
        };
        // Keep heartbeat values readable by earlier releases during a rolling upgrade.
        await _cache.SetStringAsync(_keys.Heartbeat(owner),
            JsonSerializer.Serialize(payload, RedisContextJsonSerializerContext.Default.NodeHeartbeatPayload),
            new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = _tickerQRedisOptionBuilder.NodeHeartbeatInterval + TimeSpan.FromSeconds(20)
            }, cancellationToken).ConfigureAwait(false);
        await _database.SetAddAsync(PhysicalKey(_keys.NodesRegistryV2), owner)
            .WaitAsync(cancellationToken).ConfigureAwait(false);

        // Never let a disconnected dashboard delay renewal. At most one notification may
        // be outstanding; its exceptions are observed even if the sender hangs or fails.
        lock (_notificationLock)
            if (_heartbeatNotification.IsCompleted)
                _heartbeatNotification = PublishHeartbeatAsync(payload);
    }

    private async Task PublishHeartbeatAsync(NodeHeartbeatPayload payload)
    {
        // Do not invoke even the synchronous part of a custom sender on the renewal loop.
        await Task.Yield();
        try
        {
            await _notificationHubSender.UpdateNodeHeartBeatAsync(
                JsonSerializer.SerializeToElement(payload, RedisContextJsonSerializerContext.Default.NodeHeartbeatPayload))
                .ConfigureAwait(false);
        }
        catch (Exception error)
        {
            _logger.LogWarning(error, "TickerQ dashboard heartbeat notification failed; node renewal is unaffected.");
        }
    }

    public async Task<string[]> GetDeadNodesAsync(CancellationToken cancellationToken = default)
    {
        var members = await _database.SetMembersAsync(PhysicalKey(_keys.NodesRegistryV2))
            .WaitAsync(cancellationToken).ConfigureAwait(false);
        var owners = members.Select(x => x.ToString()).ToHashSet(StringComparer.Ordinal);
        // Earlier binaries used a JSON registry. Read it without rewriting it so their
        // live owners remain visible and their unfinished work can still be recovered.
        var legacyJson = await _cache.GetStringAsync(_keys.NodesRegistry, cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrEmpty(legacyJson))
            owners.UnionWith(JsonSerializer.Deserialize(legacyJson, RedisContextJsonSerializerContext.Default.HashSetString) ?? []);
        var dead = new List<string>();
        foreach (var owner in owners)
            if (string.IsNullOrEmpty(await _cache.GetStringAsync(_keys.Heartbeat(owner), cancellationToken).ConfigureAwait(false)))
                dead.Add(owner);
        // Discovery is read-only. A failed recovery must remain discoverable next time.
        return dead.ToArray();
    }

    public async Task AcknowledgeDeadNodeAsync(string owner, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await _database.ScriptEvaluateAsync(AcknowledgeDeadNodeScript,
            [PhysicalKey(_keys.NodesRegistryV2), PhysicalKey(_keys.Heartbeat(owner)), PhysicalKey(_keys.NodesRegistry)],
            [owner]).WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task RetryPendingInvalidationsAsync(CancellationToken cancellationToken = default)
    {
        foreach (var key in _invalidatingCache.PendingKeys)
        {
            try { await _invalidatingCache.RetryAsync(key, cancellationToken).ConfigureAwait(false); }
            catch (Exception error)
            {
                cancellationToken.ThrowIfCancellationRequested();
                _logger.LogWarning(error, "TickerQ cache invalidation retry failed for {CacheKey}.", key);
            }
        }
    }

    public async Task<TResult[]> GetOrSetArrayAsync<TResult>(string cacheKey,
        Func<CancellationToken, Task<TResult[]>> factory, TimeSpan? expiration = null,
        CancellationToken cancellationToken = default) where TResult : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cacheKey);
        ArgumentNullException.ThrowIfNull(factory);
        cancellationToken.ThrowIfCancellationRequested();
        var lifetime = expiration ?? DefaultArrayCacheLifetime;
        var typeInfo = _tickerQRedisOptionBuilder?.JsonSerializerContext?.GetTypeInfo(typeof(TResult[]))
            ?? RedisContextJsonSerializerContext.Default.GetTypeInfo(typeof(TResult[]));
        if (lifetime <= TimeSpan.Zero || typeInfo == null)
        {
            var uncached = await factory(cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return uncached;
        }

        if (_invalidatingCache.IsPending(cacheKey))
        {
            try { await _invalidatingCache.RetryAsync(cacheKey, cancellationToken).ConfigureAwait(false); }
            catch (Exception error)
            {
                cancellationToken.ThrowIfCancellationRequested();
                _logger.LogWarning(error, "TickerQ cache invalidation pending for {CacheKey}; bypassing cached data.", cacheKey);
                var uncached = await factory(cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                return uncached;
            }
        }

        string generation = null;
        try
        {
            var generationKey = InvalidatingDistributedCache.GenerationKey(cacheKey);
            var currentGeneration = await _cache.GetStringAsync(generationKey, cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrEmpty(currentGeneration))
            {
                // A missing/evicted marker must never make a pre-invalidation payload valid
                // again. Concurrent initializers may cause an extra miss, but not a stale hit.
                currentGeneration = Guid.NewGuid().ToString("N");
                await _cache.SetStringAsync(generationKey, currentGeneration, cancellationToken).ConfigureAwait(false);
            }
            generation = currentGeneration;
            var cachedBytes = await _cache.GetAsync(cacheKey, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (cachedBytes?.Length > 0)
            {
                using var document = JsonDocument.Parse(cachedBytes);
                var entry = document.RootElement;
                // Old releases wrote raw arrays without expiry. Treat those as misses so
                // an upgrade repairs immortal entries on first read, without flushing Redis.
                if (entry.ValueKind == JsonValueKind.Object &&
                    entry.TryGetProperty("format", out var format) && format.TryGetInt32(out var version) && version == ArrayCacheFormat &&
                    entry.TryGetProperty("generation", out var cachedGeneration) && cachedGeneration.ValueKind == JsonValueKind.String &&
                    cachedGeneration.GetString() == generation &&
                    entry.TryGetProperty("expiresAtUtcTicks", out var expiry) && expiry.TryGetInt64(out var ticks) &&
                    ticks > _timeProvider.GetUtcNow().UtcTicks && ticks <= DateTime.MaxValue.Ticks &&
                    entry.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
                {
                    var cached = (TResult[])JsonSerializer.Deserialize(items, typeInfo);
                    if (cached != null) return cached;
                }
            }
        }
        catch (Exception error)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _logger.LogWarning(error, "TickerQ cache read failed for {CacheKey}; loading from persistence.", cacheKey);
        }

        cancellationToken.ThrowIfCancellationRequested();
        // Bound staleness from the start of the store read, even if a slow fill completes
        // after invalidation. The generation remains the one read BEFORE the factory ran.
        var expiresAt = _timeProvider.GetUtcNow().Add(lifetime);
        var result = await factory(cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        if (result == null || generation == null || expiresAt <= _timeProvider.GetUtcNow())
            return result;

        try
        {
            var bufferWriter = new ArrayBufferWriter<byte>();
            await using var writer = new Utf8JsonWriter(bufferWriter);
            writer.WriteStartObject();
            writer.WriteNumber("format", ArrayCacheFormat);
            writer.WriteString("generation", generation);
            writer.WriteNumber("expiresAtUtcTicks", expiresAt.UtcTicks);
            writer.WritePropertyName("items");
            JsonSerializer.Serialize(writer, result, typeInfo);
            writer.WriteEndObject();
            await writer.FlushAsync(cancellationToken).ConfigureAwait(false);

            await _cache.SetAsync(cacheKey, bufferWriter.WrittenMemory.ToArray(),
                new DistributedCacheEntryOptions { AbsoluteExpiration = expiresAt }, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
        }
        catch (Exception error)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _logger.LogWarning(error, "TickerQ cache write failed for {CacheKey}; returning persistence data without caching.", cacheKey);
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
[JsonSerializable(typeof(ICollection<TimeTickerEntity>))]
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
[JsonSerializable(typeof(RedisNodeFinalizationRecord))]
[JsonSerializable(typeof(SavedTickerDefinition))]
[JsonSerializable(typeof(SavedTickerDefinition[]))]
internal partial class RedisContextJsonSerializerContext : JsonSerializerContext;
