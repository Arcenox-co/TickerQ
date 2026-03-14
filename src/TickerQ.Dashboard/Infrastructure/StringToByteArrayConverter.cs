using System;
using System.Text.Json;
using System.Text.Json.Serialization;
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
                
                // Serialize the JSON string to UTF-8 bytes using the current options
                // (which have the source-gen resolver chain), avoiding reflection
                var serialized = JsonSerializer.SerializeToUtf8Bytes(
                    JsonDocument.Parse(stringValue).RootElement, options);
                // Pass pre-serialized bytes to CreateTickerRequest for compression handling
                return TickerHelper.CreateTickerRequest(serialized);
            }
            
            if (reader.TokenType == JsonTokenType.StartArray)
            {
                // Handle if it's already a byte array (number array from frontend)
                // Don't double-compress - just deserialize the byte array directly
                var bytes = JsonSerializer.Deserialize<byte[]>(ref reader, options);
                return bytes;
            }
            
            return null;
        }

        public override void Write(Utf8JsonWriter writer, byte[] value, JsonSerializerOptions options)
        {
            // For serialization, convert bytes back to string if needed
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
                // If can't deserialize, write as byte array
                JsonSerializer.Serialize(writer, value, options);
            }
        }
    }
}
