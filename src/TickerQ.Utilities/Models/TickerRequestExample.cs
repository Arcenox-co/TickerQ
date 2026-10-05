using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TickerQ.Utilities.Models
{
    /// <summary>
    /// A named, immutable request example. Examples are documentation/editor aids only;
    /// they never define server-side validity and must not be treated as default domain values.
    /// The payload is carried as an embedded <see cref="JsonElement"/> (<see cref="Value"/>) — not
    /// an escaped string — so it serializes as inline JSON.
    /// </summary>
    public sealed class TickerRequestExample : IEquatable<TickerRequestExample>
    {
        // Compact textual form → deterministic pre-canonical structural-equality key.
        private readonly string _compact;

        /// <summary>Canonical constructor (also used by System.Text.Json).</summary>
        [JsonConstructor]
        public TickerRequestExample(string key, string summary, JsonElement value)
        {
            if (string.IsNullOrWhiteSpace(key))
                throw new ArgumentException("Example key must be non-empty.", nameof(key));
            if (value.ValueKind == JsonValueKind.Undefined)
                throw new ArgumentException("Example value must be a defined JSON value.", nameof(value));

            Key = key;
            Summary = summary; // optional
            Value = value.Clone(); // detach from any backing document
            _compact = TickerJsonGuard.Compact(Value);
        }

        /// <summary>Convenience constructor accepting a JSON string; validated and parsed to <see cref="Value"/>.</summary>
        public TickerRequestExample(string key, string summary, string valueJson)
            : this(key, summary, ParseNonNull(valueJson))
        {
        }

        private static JsonElement ParseNonNull(string valueJson)
        {
            if (valueJson == null)
                throw new ArgumentException("Example value must be non-null valid JSON.", nameof(valueJson));
            return TickerJsonGuard.ParseValue(valueJson, nameof(valueJson));
        }

        /// <summary>Stable identifier for the example.</summary>
        public string Key { get; }

        /// <summary>Human-readable display text. Optional.</summary>
        public string Summary { get; }

        /// <summary>Embedded JSON payload for the example (any JSON value).</summary>
        public JsonElement Value { get; }

        public bool Equals(TickerRequestExample other)
        {
            if (other is null) return false;
            if (ReferenceEquals(this, other)) return true;
            return string.Equals(Key, other.Key, StringComparison.Ordinal)
                   && string.Equals(Summary, other.Summary, StringComparison.Ordinal)
                   // Genuinely structural: property order / formatting is ignored (pre-canonical).
                   && JsonElement.DeepEquals(Value, other.Value);
        }

        public override bool Equals(object obj) => Equals(obj as TickerRequestExample);

        public override int GetHashCode()
        {
            // Exclude noncanonical JSON text so the Equals/GetHashCode contract holds under
            // DeepEquals (equal-but-reordered payloads must hash the same). ValueKind is stable.
            var hash = new HashCode();
            hash.Add(Key, StringComparer.Ordinal);
            hash.Add(Summary, StringComparer.Ordinal);
            hash.Add(Value.ValueKind);
            return hash.ToHashCode();
        }

        public override string ToString() => $"{Key}: {_compact}";
    }
}
