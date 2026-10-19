using System.Text.Json;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Integrations;

namespace TickerQ.Caching.StackExchangeRedis.Infrastructure;

internal struct RedisMonitoringMetadata
{
    internal Guid Id, CronTickerId;
    internal bool FunctionMatches;
    internal TickerStatus Status;
    internal DateTime? ExecutedAt;
    internal long ElapsedTime;
    internal Guid? AcquisitionToken, ChainGeneration, ChainRootId;

    internal readonly TickerMonitoringExecution Execution(bool isCron) => new()
    {
        Id = Id, IsCron = isCron, Status = Status, CompletedUtc = ExecutedAt,
        ElapsedMilliseconds = ElapsedTime, Generation = AcquisitionToken ?? (isCron ? null : ChainGeneration)
    };
}

/// <summary>Reads only monitoring fields; requests, results, exceptions and custom fields are skipped.</summary>
internal static class RedisMonitoringReader
{
    internal static void Visit(ReadOnlySpan<byte> json, string function, bool includeChildren,
        Action<RedisMonitoringMetadata> visit, CancellationToken cancellationToken)
    {
        var reader = new Utf8JsonReader(json);
        if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
            throw new JsonException("Expected a stored ticker object.");
        ReadObject(ref reader, function, includeChildren, visit, cancellationToken);
        if (reader.Read()) throw new JsonException("Unexpected data after stored ticker.");
    }

    private static void ReadObject(ref Utf8JsonReader reader, string function, bool includeChildren,
        Action<RedisMonitoringMetadata> visit, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var row = new RedisMonitoringMetadata();
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject) { visit(row); return; }
            if (reader.TokenType != JsonTokenType.PropertyName) throw new JsonException("Expected a ticker property.");
            // Providers and Lua scripts use PascalCase or camelCase names.
            var field = reader.GetString();
            if (!reader.Read()) throw new JsonException("Missing ticker property value.");
            switch (field)
            {
                case "Id": case "id": row.Id = reader.GetGuid(); break;
                case "Function": case "function": row.FunctionMatches = reader.TokenType == JsonTokenType.String && reader.ValueTextEquals(function); break;
                case "Status": case "status": row.Status = (TickerStatus)reader.GetInt32(); break;
                case "ExecutedAt": case "executedAt": row.ExecutedAt = reader.TokenType == JsonTokenType.Null ? null : reader.GetDateTime(); break;
                case "ElapsedTime": case "elapsedTime": row.ElapsedTime = reader.GetInt64(); break;
                case "AcquisitionToken": case "acquisitionToken": row.AcquisitionToken = NullableGuid(ref reader); break;
                case "ChainGeneration": case "chainGeneration": row.ChainGeneration = NullableGuid(ref reader); break;
                case "ChainRootId": case "chainRootId": row.ChainRootId = NullableGuid(ref reader); break;
                case "CronTickerId": case "cronTickerId": row.CronTickerId = reader.GetGuid(); break;
                case "Children": case "children":
                    if (includeChildren && reader.TokenType == JsonTokenType.StartArray)
                    {
                        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                        {
                            if (reader.TokenType != JsonTokenType.StartObject) throw new JsonException("Expected a child ticker object.");
                            ReadObject(ref reader, function, true, visit, cancellationToken);
                        }
                    }
                    else reader.Skip(); // Includes cjson's empty-array representation: {}.
                    break;
                default: reader.Skip(); break;
            }
        }
        throw new JsonException("Incomplete stored ticker.");
    }

    private static Guid? NullableGuid(ref Utf8JsonReader reader) => reader.TokenType == JsonTokenType.Null ? null : reader.GetGuid();
}
