using System;
using System.IO;
using System.Text;
using System.Text.Json;

namespace TickerQ.Utilities.Models
{
    /// <summary>
    /// Small structural JSON helpers for wire-contract value objects. These validate shape only
    /// (well-formedness / root kind) and produce safely detached <see cref="JsonElement"/> clones;
    /// they do not canonicalize or fingerprint (that is Task 3).
    /// </summary>
    internal static class TickerJsonGuard
    {
        /// <summary>
        /// Parses any well-formed JSON value and returns a clone detached from the backing document.
        /// Throws <see cref="ArgumentException"/> when not well-formed.
        /// </summary>
        public static JsonElement ParseValue(string json, string paramName)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                return doc.RootElement.Clone();
            }
            catch (JsonException e)
            {
                throw new ArgumentException($"Value must be valid JSON: {e.Message}", paramName);
            }
        }

        /// <summary>
        /// Parses a well-formed JSON object and returns a clone. Throws <see cref="ArgumentException"/>
        /// when not an object or not well-formed.
        /// </summary>
        public static JsonElement ParseObject(string json, string paramName)
        {
            JsonElement clone;
            try
            {
                using var doc = JsonDocument.Parse(json);
                clone = doc.RootElement.Clone();
            }
            catch (JsonException e)
            {
                throw new ArgumentException($"Value must be a valid JSON object: {e.Message}", paramName);
            }

            if (clone.ValueKind != JsonValueKind.Object)
                throw new ArgumentException($"Value must be a JSON object (root was {clone.ValueKind}).", paramName);

            return clone;
        }

        /// <summary>Requires that <paramref name="element"/> is a JSON object.</summary>
        public static JsonElement RequireObject(JsonElement element, string paramName)
        {
            if (element.ValueKind != JsonValueKind.Object)
                throw new ArgumentException($"Value must be a JSON object (root was {element.ValueKind}).", paramName);
            return element.Clone();
        }

        /// <summary>
        /// Compact (whitespace-free) textual form of a JsonElement, written without any reflection
        /// or dynamic-code paths. Used as a deterministic pre-canonical structural-equality key.
        /// </summary>
        public static string Compact(JsonElement element)
        {
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream))
            {
                element.WriteTo(writer);
            }
            return Encoding.UTF8.GetString(stream.ToArray());
        }
    }
}
