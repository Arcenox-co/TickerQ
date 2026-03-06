using System.Text.Json;
using System.Text.Json.Serialization;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Enums;

namespace TickerQ.Caching.StackExchangeRedis.Converter;

public class TimeTickerEntityConverter : JsonConverter<TimeTickerEntity>
{
    public override TimeTickerEntity Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
            throw new JsonException("Expected start of object.");

        var entity = new TimeTickerEntity();

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
                return entity;

            if (reader.TokenType != JsonTokenType.PropertyName)
                continue;

            var propName = reader.GetString() ?? string.Empty;
            var key = propName.ToLowerInvariant();

            // move to value
            reader.Read();

            switch (key)
            {
                case "id":
                    entity.Id = JsonSerializer.Deserialize<Guid>(ref reader, options);
                    break;
                case "function":
                    entity.Function = JsonSerializer.Deserialize<string>(ref reader, options);
                    break;
                case "description":
                    entity.Description = JsonSerializer.Deserialize<string>(ref reader, options);
                    break;
                case "initidentifier":
                    entity.InitIdentifier = JsonSerializer.Deserialize<string>(ref reader, options);
                    break;
                case "createdat":
                    entity.CreatedAt = JsonSerializer.Deserialize<DateTime>(ref reader, options);
                    break;
                case "updatedat":
                    entity.UpdatedAt = JsonSerializer.Deserialize<DateTime>(ref reader, options);
                    break;
                case "status":
                    entity.Status = JsonSerializer.Deserialize<TickerStatus>(ref reader, options);
                    break;
                case "lockholder":
                    entity.LockHolder = JsonSerializer.Deserialize<string>(ref reader, options);
                    break;
                case "request":
                    entity.Request = JsonSerializer.Deserialize<byte[]>(ref reader, options);
                    break;
                case "executiontime":
                    entity.ExecutionTime = JsonSerializer.Deserialize<DateTime?>(ref reader, options);
                    break;
                case "lockedat":
                    entity.LockedAt = JsonSerializer.Deserialize<DateTime?>(ref reader, options);
                    break;
                case "executedat":
                    entity.ExecutedAt = JsonSerializer.Deserialize<DateTime?>(ref reader, options);
                    break;
                case "exceptionmessage":
                    entity.ExceptionMessage = JsonSerializer.Deserialize<string>(ref reader, options);
                    break;
                case "skippedreason":
                    entity.SkippedReason = JsonSerializer.Deserialize<string>(ref reader, options);
                    break;
                case "elapsedtime":
                    entity.ElapsedTime = JsonSerializer.Deserialize<long>(ref reader, options);
                    break;
                case "retries":
                    entity.Retries = JsonSerializer.Deserialize<int>(ref reader, options);
                    break;
                case "retrycount":
                    entity.RetryCount = JsonSerializer.Deserialize<int>(ref reader, options);
                    break;
                case "retryintervals":
                    entity.RetryIntervals = JsonSerializer.Deserialize<int[]>(ref reader, options);
                    break;
                case "parentid":
                    entity.ParentId = JsonSerializer.Deserialize<Guid?>(ref reader, options);
                    break;
                case "children":
                    entity.Children = JsonSerializer.Deserialize<List<TimeTickerEntity>>(ref reader, options) ?? new List<TimeTickerEntity>();
                    break;
                case "runcondition":
                    entity.RunCondition = JsonSerializer.Deserialize<RunCondition?>(ref reader, options);
                    break;
                default:
                    // Unknown property: skip
                    JsonSerializer.Deserialize<JsonElement>(ref reader, options);
                    break;
            }
        }

        throw new JsonException("Unexpected end of JSON.");
    }

    public override void Write(Utf8JsonWriter writer, TimeTickerEntity value, JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteStartObject();

        void WritePropName(string name)
        {
            var propName = options?.PropertyNamingPolicy?.ConvertName(name) ?? name;
            writer.WritePropertyName(propName);
        }

        // Base properties
        WritePropName(nameof(value.Id));
        JsonSerializer.Serialize(writer, value.Id, options);

        if (value.Function is not null)
        {
            WritePropName(nameof(value.Function));
            JsonSerializer.Serialize(writer, value.Function, options);
        }

        if (value.Description is not null)
        {
            WritePropName(nameof(value.Description));
            JsonSerializer.Serialize(writer, value.Description, options);
        }

        if (value.InitIdentifier is not null)
        {
            WritePropName(nameof(value.InitIdentifier));
            JsonSerializer.Serialize(writer, value.InitIdentifier, options);
        }

        WritePropName(nameof(value.CreatedAt));
        JsonSerializer.Serialize(writer, value.CreatedAt, options);

        WritePropName(nameof(value.UpdatedAt));
        JsonSerializer.Serialize(writer, value.UpdatedAt, options);

        // TimeTicker specific
        WritePropName(nameof(value.Status));
        JsonSerializer.Serialize(writer, value.Status, options);

        if (value.LockHolder is not null)
        {
            WritePropName(nameof(value.LockHolder));
            JsonSerializer.Serialize(writer, value.LockHolder, options);
        }

        if (value.Request is not null)
        {
            WritePropName(nameof(value.Request));
            JsonSerializer.Serialize(writer, value.Request, options);
        }

        if (value.ExecutionTime.HasValue)
        {
            WritePropName(nameof(value.ExecutionTime));
            JsonSerializer.Serialize(writer, value.ExecutionTime, options);
        }

        if (value.LockedAt.HasValue)
        {
            WritePropName(nameof(value.LockedAt));
            JsonSerializer.Serialize(writer, value.LockedAt, options);
        }

        if (value.ExecutedAt.HasValue)
        {
            WritePropName(nameof(value.ExecutedAt));
            JsonSerializer.Serialize(writer, value.ExecutedAt, options);
        }

        if (value.ExceptionMessage is not null)
        {
            WritePropName(nameof(value.ExceptionMessage));
            JsonSerializer.Serialize(writer, value.ExceptionMessage, options);
        }

        if (value.SkippedReason is not null)
        {
            WritePropName(nameof(value.SkippedReason));
            JsonSerializer.Serialize(writer, value.SkippedReason, options);
        }

        WritePropName(nameof(value.ElapsedTime));
        JsonSerializer.Serialize(writer, value.ElapsedTime, options);

        WritePropName(nameof(value.Retries));
        JsonSerializer.Serialize(writer, value.Retries, options);

        WritePropName(nameof(value.RetryCount));
        JsonSerializer.Serialize(writer, value.RetryCount, options);

        if (value.RetryIntervals is not null)
        {
            WritePropName(nameof(value.RetryIntervals));
            JsonSerializer.Serialize(writer, value.RetryIntervals, options);
        }

        if (value.ParentId.HasValue)
        {
            WritePropName(nameof(value.ParentId));
            JsonSerializer.Serialize(writer, value.ParentId, options);
        }

        if (value.Children is not null && value.Children.Count > 0)
        {
            WritePropName(nameof(value.Children));
            JsonSerializer.Serialize(writer, value.Children, options);
        }

        if (value.RunCondition.HasValue)
        {
            WritePropName(nameof(value.RunCondition));
            JsonSerializer.Serialize(writer, value.RunCondition, options);
        }

        writer.WriteEndObject();
    }
}
