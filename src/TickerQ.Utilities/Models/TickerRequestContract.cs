using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using TickerQ.Utilities.Serialization;

namespace TickerQ.Utilities.Models
{
    /// <summary>
    /// Immutable, transport-neutral description of a function's request payload.
    /// This is the <b>wire</b> contract only: it deliberately carries no CLR <see cref="Type"/>
    /// or <c>JsonTypeInfo</c>. Local runtime type/serializer metadata lives in a separate
    /// execution registry (see <see cref="TickerQ.Utilities.TickerFunctionProvider"/>).
    /// The schema is carried as an embedded, object-only <see cref="JsonElement"/> (<see cref="Schema"/>).
    /// </summary>
    public sealed class TickerRequestContract : IEquatable<TickerRequestContract>
    {
        private readonly TickerRequestExample[] _examples;
        private readonly ReadOnlyCollection<TickerRequestExample> _examplesView;

        /// <summary>Canonical constructor (also used by System.Text.Json).</summary>
        /// <remarks>
        /// <see cref="Fingerprint"/> is <b>derived</b>: it is never accepted from the wire but computed
        /// from the canonicalized <paramref name="schema"/>, <paramref name="mediaType"/>,
        /// <paramref name="required"/>, and the enclosing descriptor's contract version. A standalone
        /// request contract uses the initial version until attached to a descriptor. A null schema
        /// yields a null fingerprint; a non-null schema is
        /// stored in canonical form and always yields a non-null fingerprint. This makes the value
        /// tamper-evident and stable across property-order/whitespace differences.
        /// </remarks>
        [JsonConstructor]
        public TickerRequestContract(
            string typeName,
            string mediaType,
            bool required,
            string schemaDialect,
            JsonElement? schema,
            IReadOnlyList<TickerRequestExample> examples)
        {
            if (string.IsNullOrWhiteSpace(typeName))
                throw new ArgumentException("Request type name must be non-empty.", nameof(typeName));
            if (string.IsNullOrWhiteSpace(mediaType))
                throw new ArgumentException("Media type must be non-empty.", nameof(mediaType));
            if (string.IsNullOrWhiteSpace(schemaDialect))
                throw new ArgumentException("Schema dialect must be non-empty.", nameof(schemaDialect));
            // Draft 2020-12 is the sole supported dialect policy. Reject any other declared dialect at the
            // boundary so it can never reach descriptor publication, validation, or identity computation.
            if (!string.Equals(schemaDialect, TickerRequestContractConstants.SchemaDialect2020_12, StringComparison.Ordinal))
                throw new ArgumentException(
                    $"Unsupported schema dialect '{schemaDialect}'. Only '{TickerRequestContractConstants.SchemaDialect2020_12}' " +
                    "(JSON Schema Draft 2020-12) is supported.",
                    nameof(schemaDialect));

            TypeName = typeName;
            MediaType = mediaType;
            Required = required;
            SchemaDialect = schemaDialect;

            if (schema.HasValue)
            {
                // Reject non-object roots at the contract boundary, then store the schema in canonical
                // form and derive the fingerprint over the same canonical UTF-8 bytes.
                var schemaObject = TickerJsonGuard.RequireObject(schema.Value, nameof(schema));

                // An embedded $schema that contradicts the supported dialect is rejected before publication:
                // it would otherwise silently steer the evaluator to a different, unsupported dialect.
                if (schemaObject.TryGetProperty("$schema", out var embeddedDialect))
                {
                    if (embeddedDialect.ValueKind != JsonValueKind.String)
                        throw new ArgumentException(
                            "Embedded $schema must be a string containing the supported JSON Schema dialect URI.",
                            nameof(schema));

                    if (!string.Equals(embeddedDialect.GetString(),
                            TickerRequestContractConstants.SchemaDialect2020_12, StringComparison.Ordinal))
                        throw new ArgumentException(
                            $"Embedded $schema '{embeddedDialect.GetString()}' conflicts with the supported " +
                            $"'{TickerRequestContractConstants.SchemaDialect2020_12}' (JSON Schema Draft 2020-12) dialect.",
                            nameof(schema));
                }

                var canonicalUtf8 = JsonSchemaCanonicalizer.CanonicalizeToUtf8(schemaObject);
                Schema = ParseCanonical(canonicalUtf8);
                Fingerprint = TickerRequestContractFingerprint.Compute(canonicalUtf8, MediaType, Required);
            }

            // Defensive copy + read-only view → the exposed collection can never be cast back to
            // a mutable array/list and altered.
            _examples = Materialize(examples);
            _examplesView = new ReadOnlyCollection<TickerRequestExample>(_examples);
        }

        /// <summary>Convenience string-authoring constructor with sensible wire defaults.</summary>
        public TickerRequestContract(
            string typeName,
            string mediaType = TickerRequestContractConstants.DefaultMediaType,
            bool required = true,
            string schemaDialect = TickerRequestContractConstants.SchemaDialect2020_12,
            string schemaJson = null,
            IReadOnlyList<TickerRequestExample> examples = null)
            : this(typeName, mediaType, required, schemaDialect, ParseSchema(schemaJson), examples)
        {
        }

        private static JsonElement? ParseSchema(string schemaJson)
            => schemaJson == null ? (JsonElement?)null : TickerJsonGuard.ParseObject(schemaJson, nameof(schemaJson));

        private static JsonElement ParseCanonical(byte[] canonicalUtf8)
        {
            using var doc = JsonDocument.Parse(canonicalUtf8);
            return doc.RootElement.Clone(); // detach from the backing document
        }

        /// <summary>Wire type name (e.g. the request type's full name). Not a CLR type handle.</summary>
        public string TypeName { get; }

        public string MediaType { get; }

        /// <summary>Whether a payload is required. Empty strings never represent absence.</summary>
        public bool Required { get; }

        public string SchemaDialect { get; }

        /// <summary>
        /// Embedded, object-only JSON schema in <b>canonical</b> form (property names sorted ordinally,
        /// whitespace stripped, array order preserved), or null when no schema is attached.
        /// </summary>
        public JsonElement? Schema { get; }

        /// <summary>
        /// Deterministic <c>sha256:</c>-prefixed lowercase-hex fingerprint derived from the canonical
        /// schema, media type, and requiredness. Non-null whenever <see cref="Schema"/> is non-null;
        /// null when <see cref="Schema"/> is null.
        /// </summary>
        public string Fingerprint { get; }

        internal TickerRequestContract WithContractVersion(int contractVersion)
        {
            if (!Schema.HasValue) return this;

            var canonicalUtf8 = JsonSchemaCanonicalizer.CanonicalizeToUtf8(Schema.Value);
            var fingerprint = TickerRequestContractFingerprint.Compute(
                canonicalUtf8, MediaType, Required, contractVersion);
            if (string.Equals(Fingerprint, fingerprint, StringComparison.Ordinal)) return this;

            return new TickerRequestContract(this, fingerprint);
        }

        private TickerRequestContract(TickerRequestContract source, string fingerprint)
        {
            TypeName = source.TypeName;
            MediaType = source.MediaType;
            Required = source.Required;
            SchemaDialect = source.SchemaDialect;
            Schema = source.Schema;
            Fingerprint = fingerprint;
            _examples = source._examples;
            _examplesView = source._examplesView;
        }

        /// <summary>Named, ordered, immutable examples.</summary>
        public IReadOnlyList<TickerRequestExample> Examples => _examplesView;

        private static TickerRequestExample[] Materialize(IReadOnlyList<TickerRequestExample> examples)
        {
            if (examples == null || examples.Count == 0) return Array.Empty<TickerRequestExample>();

            var list = new List<TickerRequestExample>(examples.Count);
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var example in examples)
            {
                if (example == null) throw new ArgumentException("Examples cannot contain null entries.", nameof(examples));
                if (!keys.Add(example.Key))
                    throw new ArgumentException($"Duplicate example key '{example.Key}'.", nameof(examples));
                list.Add(example);
            }

            return list.ToArray();
        }

        public bool Equals(TickerRequestContract other)
        {
            if (other is null) return false;
            if (ReferenceEquals(this, other)) return true;

            if (!string.Equals(TypeName, other.TypeName, StringComparison.Ordinal)
                || !string.Equals(MediaType, other.MediaType, StringComparison.Ordinal)
                || Required != other.Required
                || !string.Equals(SchemaDialect, other.SchemaDialect, StringComparison.Ordinal)
                || !SchemaEquals(Schema, other.Schema)
                || !string.Equals(Fingerprint, other.Fingerprint, StringComparison.Ordinal)
                || _examples.Length != other._examples.Length)
                return false;

            for (var i = 0; i < _examples.Length; i++)
            {
                if (!_examples[i].Equals(other._examples[i])) return false;
            }

            return true;
        }

        public override bool Equals(object obj) => Equals(obj as TickerRequestContract);

        // Structural schema comparison over the stored canonical elements. Property order / formatting
        // are already normalized at construction; the derived Fingerprint below fully corroborates it.
        private static bool SchemaEquals(JsonElement? a, JsonElement? b)
        {
            if (!a.HasValue) return !b.HasValue;
            if (!b.HasValue) return false;
            return JsonElement.DeepEquals(a.Value, b.Value);
        }

        public override int GetHashCode()
        {
            // Exclude noncanonical JSON text so equal-but-reordered schemas hash the same. ValueKind
            // is stable regardless of property order; examples hash via their own ValueKind-safe hash.
            var hash = new HashCode();
            hash.Add(TypeName, StringComparer.Ordinal);
            hash.Add(MediaType, StringComparer.Ordinal);
            hash.Add(Required);
            hash.Add(SchemaDialect, StringComparer.Ordinal);
            hash.Add(Schema?.ValueKind);
            hash.Add(Fingerprint, StringComparer.Ordinal);
            foreach (var example in _examples) hash.Add(example);
            return hash.ToHashCode();
        }

        public override string ToString()
            => $"{TypeName} ({MediaType}, required={Required}, examples={_examples.Length})";
    }
}
