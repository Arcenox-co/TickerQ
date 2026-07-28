using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using TickerQ.Utilities.Serialization;

namespace TickerQ.Utilities.Models
{
    /// <summary>
    /// Immutable wire description of an optional function result. Absence is represented by no
    /// result envelope; a present payload containing JSON null remains a distinct published result.
    /// Runtime CLR/JsonTypeInfo metadata is registered separately.
    /// </summary>
    public sealed class TickerResultContract : IEquatable<TickerResultContract>
    {
        [JsonConstructor]
        public TickerResultContract(
            string typeName,
            string mediaType,
            int contractVersion,
            string schemaDialect,
            JsonElement? schema)
        {
            if (string.IsNullOrWhiteSpace(typeName))
                throw new ArgumentException("Result type name must be non-empty.", nameof(typeName));
            if (string.IsNullOrWhiteSpace(mediaType))
                throw new ArgumentException("Result media type must be non-empty.", nameof(mediaType));
            if (contractVersion <= 0)
                throw new ArgumentOutOfRangeException(nameof(contractVersion), contractVersion, "Result contract version must be positive.");
            if (!string.Equals(schemaDialect, TickerRequestContractConstants.SchemaDialect2020_12, StringComparison.Ordinal))
                throw new ArgumentException($"Unsupported schema dialect '{schemaDialect}'.", nameof(schemaDialect));

            TypeName = typeName;
            MediaType = mediaType;
            ContractVersion = contractVersion;
            SchemaDialect = schemaDialect;
            if (schema.HasValue)
            {
                var schemaObject = TickerJsonGuard.RequireObject(schema.Value, nameof(schema));
                if (schemaObject.TryGetProperty("$schema", out var embeddedDialect))
                {
                    if (embeddedDialect.ValueKind != JsonValueKind.String)
                        throw new ArgumentException(
                            "Embedded $schema must be a string containing the supported JSON Schema dialect URI.",
                            nameof(schema));
                    if (!string.Equals(
                            embeddedDialect.GetString(),
                            TickerRequestContractConstants.SchemaDialect2020_12,
                            StringComparison.Ordinal))
                        throw new ArgumentException(
                            $"Embedded $schema '{embeddedDialect.GetString()}' conflicts with the supported " +
                            $"'{TickerRequestContractConstants.SchemaDialect2020_12}' (JSON Schema Draft 2020-12) dialect.",
                            nameof(schema));
                }
                var canonicalUtf8 = JsonSchemaCanonicalizer.CanonicalizeToUtf8(schemaObject);
                using var document = JsonDocument.Parse(canonicalUtf8);
                Schema = document.RootElement.Clone();
                // Result payloads are optional at the function boundary. A published JSON null is
                // still present; optionality only describes whether an envelope exists.
                Fingerprint = TickerRequestContractFingerprint.Compute(
                    canonicalUtf8, MediaType, required: false, ContractVersion);
                ContractId = Fingerprint;
            }
        }

        public TickerResultContract(
            string typeName,
            string mediaType = TickerRequestContractConstants.DefaultMediaType,
            int contractVersion = TickerRequestContractConstants.InitialContractVersion,
            string schemaDialect = TickerRequestContractConstants.SchemaDialect2020_12,
            string schemaJson = null)
            : this(typeName, mediaType, contractVersion, schemaDialect,
                schemaJson == null ? (JsonElement?)null : TickerJsonGuard.ParseObject(schemaJson, nameof(schemaJson)))
        {
        }

        public string ContractId { get; }
        public string TypeName { get; }
        public string MediaType { get; }
        public int ContractVersion { get; }
        public string SchemaDialect { get; }
        public JsonElement? Schema { get; }
        public string Fingerprint { get; }

        internal TickerResultContract WithContractVersion(int contractVersion)
        {
            if (ContractVersion == contractVersion)
                return this;

            return new TickerResultContract(
                TypeName,
                MediaType,
                contractVersion,
                SchemaDialect,
                Schema);
        }

        public bool Equals(TickerResultContract other)
        {
            if (other is null) return false;
            if (ReferenceEquals(this, other)) return true;
            return string.Equals(ContractId, other.ContractId, StringComparison.Ordinal)
                && string.Equals(TypeName, other.TypeName, StringComparison.Ordinal)
                && string.Equals(MediaType, other.MediaType, StringComparison.Ordinal)
                && ContractVersion == other.ContractVersion
                && string.Equals(SchemaDialect, other.SchemaDialect, StringComparison.Ordinal)
                && string.Equals(Fingerprint, other.Fingerprint, StringComparison.Ordinal);
        }

        public override bool Equals(object obj) => Equals(obj as TickerResultContract);

        public override int GetHashCode()
            => HashCode.Combine(ContractId, TypeName, MediaType, ContractVersion, SchemaDialect, Fingerprint);

        public override string ToString()
            => $"{ContractId} ({TypeName}) v{ContractVersion} fingerprint={Fingerprint ?? "null"}";
    }
}
