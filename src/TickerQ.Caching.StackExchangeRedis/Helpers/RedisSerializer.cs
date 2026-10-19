#nullable disable
using System.Text.Json;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace TickerQ.Caching.StackExchangeRedis.Helpers;

internal sealed class RedisSerializer
{
    private readonly IDatabase _db;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly ILogger _logger;

    internal RedisSerializer(IDatabase db, JsonSerializerOptions jsonOptions, ILogger logger)
    {
        _db = db;
        _jsonOptions = jsonOptions;
        _logger = logger;
    }

    internal async Task<T> GetAsync<T>(string key) where T : class
    {
        var val = await _db.StringGetAsync(key).ConfigureAwait(false);
        if (val.IsNullOrEmpty) return null;
        try
        {
            return (T)JsonSerializer.Deserialize((string)val, GetTypeInfo<T>());
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to deserialize Redis key '{Key}' as {Type}. Data may be corrupted.", key, typeof(T).Name);
            return null;
        }
    }

    internal string Serialize<T>(T value) where T : class
        => JsonSerializer.Serialize(value, GetTypeInfo<T>());

    internal Task SetAsync<T>(string key, T value) where T : class
        => _db.StringSetAsync(key, Serialize(value));

    internal T DeserializeOrNull<T>(string json) where T : class
    {
        if (string.IsNullOrEmpty(json)) return null;
        try
        {
            return (T)JsonSerializer.Deserialize(json, GetTypeInfo<T>());
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to deserialize JSON payload as {Type}.", typeof(T).Name);
            return null;
        }
    }

    internal const int ReadBatchSize = 128;

    internal async IAsyncEnumerable<T> ScanSetAsync<T>(string setKey, Func<Guid, string> keyBuilder,
        [EnumeratorCancellation] CancellationToken cancellationToken) where T : class
    {
        cancellationToken.ThrowIfCancellationRequested();
        var keys = new List<RedisKey>(ReadBatchSize);
        // SSCAN can repeat members during rehashing. Preserve the old set semantics
        // without retaining full documents: only this O(ID count) metadata grows.
        var seenIds = new HashSet<Guid>();
        await foreach (var member in _db.SetScanAsync(setKey, pageSize: ReadBatchSize)
                           .WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Guid.TryParse(member.ToString(), out var id) || !seenIds.Add(id)) continue;
            keys.Add(keyBuilder(id));
            if (keys.Count < ReadBatchSize) continue;
            await foreach (var row in ReadBatchAsync<T>(keys.ToArray(), cancellationToken).ConfigureAwait(false))
                yield return row;
            keys.Clear();
        }
        if (keys.Count > 0)
            await foreach (var row in ReadBatchAsync<T>(keys.ToArray(), cancellationToken).ConfigureAwait(false))
                yield return row;
    }

    private async IAsyncEnumerable<T> ReadBatchAsync<T>(RedisKey[] keys,
        [EnumeratorCancellation] CancellationToken cancellationToken) where T : class
    {
        cancellationToken.ThrowIfCancellationRequested();
        var values = await _db.StringGetAsync(keys).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        for (var i = 0; i < values.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var value = values[i];
            values[i] = RedisValue.Null;
            if (value.IsNullOrEmpty) continue;
            var row = DeserializeOrNull<T>((string)value);
            if (row != null) yield return row;
        }
    }

    internal async Task<List<T>> LoadAllFromSetAsync<T>(string setKey, Func<Guid, string> keyBuilder,
        CancellationToken cancellationToken, Func<T, bool> predicate = null) where T : class
    {
        var rows = new List<T>();
        await foreach (var row in ScanSetAsync<T>(setKey, keyBuilder, cancellationToken).ConfigureAwait(false))
            if (predicate == null || predicate(row)) rows.Add(row);
        return rows;
    }

    internal async Task<List<T>> LoadByIdsAsync<T>(Guid[] ids, Func<Guid, string> keyBuilder,
        CancellationToken cancellationToken) where T : class
    {
        cancellationToken.ThrowIfCancellationRequested();
        var rows = new List<T>();
        for (var offset = 0; offset < ids.Length; offset += ReadBatchSize)
        {
            var keys = new RedisKey[Math.Min(ReadBatchSize, ids.Length - offset)];
            for (var i = 0; i < keys.Length; i++) keys[i] = keyBuilder(ids[offset + i]);
            await foreach (var row in ReadBatchAsync<T>(keys, cancellationToken).ConfigureAwait(false)) rows.Add(row);
        }
        return rows;
    }

    private JsonTypeInfo GetTypeInfo<T>() => _jsonOptions.GetTypeInfo(typeof(T));
}
