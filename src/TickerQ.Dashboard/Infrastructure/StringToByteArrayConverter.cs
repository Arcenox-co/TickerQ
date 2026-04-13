using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using TickerQ.Utilities;

namespace TickerQ.Dashboard.Infrastructure
{
    public class StringToByteArrayConverter : JsonConverter<byte[]>
    {
        public override byte[] Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null)
            {
                return null;
            }

            if (reader.TokenType == JsonTokenType.String)
            {
                var stringValue = reader.GetString();
                if (string.IsNullOrEmpty(stringValue))
                    return null;

                var serialized = JsonSerializer.SerializeToUtf8Bytes(
                    JsonDocument.Parse(stringValue).RootElement, options.GetTypeInfo(typeof(JsonElement)));
                return TickerHelper.CreateTickerRequest(serialized);
            }

            if (reader.TokenType == JsonTokenType.StartArray)
            {
                var bytes = (byte[])JsonSerializer.Deserialize(ref reader, options.GetTypeInfo(typeof(byte[])));
                return bytes;
            }

            return null;
        }

        public override void Write(Utf8JsonWriter writer, byte[] value, JsonSerializerOptions options)
        {
            if (value == null || value.Length == 0)
            {
                writer.WriteStringValue(string.Empty);
                return;
            }

            try
            {
                var stringValue = TickerHelper.ReadTickerRequestAsString(value);
                writer.WriteStringValue(stringValue);
            }
            catch
            {
                JsonSerializer.Serialize(writer, value, options.GetTypeInfo(typeof(byte[])));
            }
        }
    }
}
