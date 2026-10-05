using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using TickerQ.Utilities;

namespace TickerQ.Dashboard.Infrastructure
{
    public class StringToByteArrayConverter : JsonConverter<byte[]>
    {
        public override byte[] Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.Null:
                    return null;

                case JsonTokenType.String:
                {
                    var stringValue = reader.GetString();
                    if (string.IsNullOrEmpty(stringValue))
                        return null;

                    // The browser ships request payloads as UTF-8 Base64 of the raw JSON
                    // text. Legacy rows/clients stored the JSON verbatim, so accept raw
                    // JSON first, then fall back to Base64-decoding. Either way the bytes
                    // must be valid JSON before we hand them to the TickerHelper store path.
                    var jsonBytes = TryGetRawJsonBytes(stringValue) ?? DecodeBase64JsonBytes(stringValue);
                    return TickerHelper.CreateTickerRequest(jsonBytes);
                }

                case JsonTokenType.StartArray:
                    // Legacy transport encoded the request as a JSON array of raw byte
                    // values. Decode it directly (see ReadLegacyNumericByteArray) rather
                    // than re-entering JsonSerializer for byte[], which resolves back to
                    // this same converter and would recurse until the stack overflows.
                    return ReadLegacyNumericByteArray(ref reader);

                default:
                    // Objects and any other unexpected token are not a valid request
                    // payload shape. Fail with a controlled JsonException so the caller
                    // surfaces a 400 malformed-body rather than silently storing null.
                    throw new JsonException(
                        $"Unsupported token '{reader.TokenType}' for a ticker request payload.");
            }
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
                // Emit the stored request JSON as UTF-8 Base64 so the browser can
                // decode it back to editable text (symmetric with Read).
                var stringValue = TickerHelper.ReadTickerRequestAsString(value);
                var base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(stringValue));
                writer.WriteStringValue(base64);
            }
            catch
            {
                // The stored bytes could not be read back as a request string (e.g. an
                // unexpected on-disk encoding). Emit the raw bytes as a JSON numeric
                // array directly. This must NOT route through JsonSerializer for byte[],
                // which resolves back to this converter and would recurse infinitely.
                WriteLegacyNumericByteArray(writer, value);
            }
        }

        /// <summary>
        /// Returns the UTF-8 bytes when <paramref name="value"/> is already raw JSON
        /// (legacy transport), or null when it is not valid JSON on its own.
        /// </summary>
        private static byte[] TryGetRawJsonBytes(string value)
        {
            try
            {
                using var _ = JsonDocument.Parse(value);
                return Encoding.UTF8.GetBytes(value);
            }
            catch (JsonException)
            {
                return null;
            }
        }

        /// <summary>
        /// Base64-decodes <paramref name="value"/> to UTF-8 bytes and requires the
        /// result to be valid JSON. Throws <see cref="JsonException"/> otherwise so
        /// the caller surfaces a malformed body rather than storing garbage.
        /// </summary>
        private static byte[] DecodeBase64JsonBytes(string value)
        {
            byte[] decoded;
            try
            {
                decoded = Convert.FromBase64String(value);
            }
            catch (FormatException)
            {
                throw new JsonException("Request payload is neither valid JSON nor Base64.");
            }

            using var _ = JsonDocument.Parse(decoded);
            return decoded;
        }

        /// <summary>
        /// Reads a legacy request payload encoded as a JSON array of raw byte values,
        /// advancing <paramref name="reader"/> to the matching end-array token. The
        /// decoded bytes must be valid JSON; a non-byte number, a non-numeric element,
        /// an unterminated array, or invalid JSON all surface a controlled
        /// <see cref="JsonException"/>. Never recurses back through the serializer.
        /// </summary>
        private static byte[] ReadLegacyNumericByteArray(ref Utf8JsonReader reader)
        {
            var bytes = new List<byte>();

            while (reader.Read())
            {
                switch (reader.TokenType)
                {
                    case JsonTokenType.EndArray:
                        var decoded = bytes.ToArray();
                        // Validate the decoded bytes are JSON before storing them.
                        using (JsonDocument.Parse(decoded)) { }
                        return TickerHelper.CreateTickerRequest(decoded);

                    case JsonTokenType.Number:
                        if (!reader.TryGetByte(out var b))
                            throw new JsonException(
                                "Legacy request array contained a value outside the 0-255 byte range.");
                        bytes.Add(b);
                        break;

                    default:
                        throw new JsonException(
                            $"Unexpected token '{reader.TokenType}' inside a legacy request byte array.");
                }
            }

            throw new JsonException("Unterminated legacy request byte array.");
        }

        /// <summary>
        /// Writes <paramref name="value"/> as a JSON array of raw byte values without
        /// re-entering the serializer for byte[] (which would recurse into this
        /// converter). Used only as a defensive fallback in <see cref="Write"/>.
        /// </summary>
        private static void WriteLegacyNumericByteArray(Utf8JsonWriter writer, byte[] value)
        {
            writer.WriteStartArray();
            foreach (var b in value)
                writer.WriteNumberValue(b);
            writer.WriteEndArray();
        }
    }
}
