using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace TickerQ.Utilities.Models
{
    /// <summary>
    /// Honest representation of how a request contract's schema was applied to a payload. Distinguishes
    /// "there is no schema to apply" from "there is a schema but the reflection-free core validator
    /// cannot express it, so no schema assertion was performed". A payload is never reported as
    /// schema-validated unless a schema was actually enforced against it.
    /// </summary>
    public enum TickerRequestSchemaValidationState
    {
        /// <summary>No schema was applicable — the contract carries no schema, or there was no payload to validate.</summary>
        NotApplicable = 0,

        /// <summary>A schema existed and was fully enforced against the payload by the core validator.</summary>
        Enforced = 1,

        /// <summary>
        /// A schema existed but used constructs outside the core validator's supported subset, so it was
        /// NOT enforced. The payload was still deserialized with the exact <c>JsonTypeInfo</c>.
        /// </summary>
        NotEnforced = 2
    }

    /// <summary>How CLR request binding was applied after payload/schema validation.</summary>
    public enum TickerRequestBindingValidationState
    {
        /// <summary>No payload required CLR binding.</summary>
        NotApplicable = 0,

        /// <summary>The payload was bound using the function's exact registered <c>JsonTypeInfo</c>.</summary>
        Enforced = 1,

        /// <summary>
        /// Local CLR metadata is unavailable (for example, for a remote function). Acceptance is safe
        /// only when the complete wire schema was enforced first.
        /// </summary>
        NotAvailable = 2
    }

    /// <summary>
    /// Machine-readable classification of a single request-payload validation failure. Stable values
    /// suitable for later Dashboard/Hub transport.
    /// </summary>
    public enum TickerRequestValidationErrorCode
    {
        /// <summary>The function identity is not present in the canonical descriptor registry.</summary>
        UnknownFunction = 0,

        /// <summary>The contract requires a payload but none was supplied.</summary>
        PayloadRequiredButMissing = 1,

        /// <summary>The function takes no request payload, but a payload was supplied.</summary>
        PayloadNotAllowed = 2,

        /// <summary>The payload bytes could not be decompressed/parsed into a well-formed JSON document.</summary>
        MalformedPayload = 3,

        /// <summary>The payload is well-formed JSON but violates the enforced schema.</summary>
        SchemaViolation = 4,

        /// <summary>The payload passed (or bypassed) schema validation but could not bind to the CLR request type.</summary>
        DeserializationFailed = 5,

        /// <summary>No local <c>JsonTypeInfo</c> is registered for the function, so the payload cannot be bound under Native AOT.</summary>
        MissingSerializerMetadata = 6,

        /// <summary>The registered request contract contains a schema that could not be evaluated.</summary>
        ContractSchemaInvalid = 7
    }

    /// <summary>
    /// One immutable, machine-readable validation error. <see cref="Location"/> is a JSON-pointer-style
    /// path into the payload when the error is location-specific, otherwise null.
    /// </summary>
    public sealed class TickerRequestValidationError : IEquatable<TickerRequestValidationError>
    {
        public TickerRequestValidationError(TickerRequestValidationErrorCode code, string message, string location = null)
        {
            if (string.IsNullOrWhiteSpace(message))
                throw new ArgumentException("Validation error message must be non-empty.", nameof(message));

            Code = code;
            Message = message;
            Location = location;
        }

        public TickerRequestValidationErrorCode Code { get; }

        public string Message { get; }

        public string Location { get; }

        public bool Equals(TickerRequestValidationError other)
        {
            if (other is null) return false;
            if (ReferenceEquals(this, other)) return true;
            return Code == other.Code
                   && string.Equals(Message, other.Message, StringComparison.Ordinal)
                   && string.Equals(Location, other.Location, StringComparison.Ordinal);
        }

        public override bool Equals(object obj) => Equals(obj as TickerRequestValidationError);

        public override int GetHashCode()
        {
            var hash = new HashCode();
            hash.Add(Code);
            hash.Add(Message, StringComparer.Ordinal);
            hash.Add(Location, StringComparer.Ordinal);
            return hash.ToHashCode();
        }

        public override string ToString()
            => Location == null ? $"{Code}: {Message}" : $"{Code} at {Location}: {Message}";
    }

    /// <summary>
    /// Immutable outcome of validating and deserializing a stored/raw request payload against a
    /// function's canonical contract. On success <see cref="Value"/> holds the deserialized request
    /// object (or null for request-less/optional-absent payloads); on failure <see cref="Errors"/>
    /// carries one or more machine-readable errors and <see cref="Value"/> is null. The service returns
    /// this rather than throwing for ordinary user payload invalidity.
    /// </summary>
    public sealed class TickerRequestValidationResult
    {
        private static readonly ReadOnlyCollection<TickerRequestValidationError> NoErrors =
            new ReadOnlyCollection<TickerRequestValidationError>(Array.Empty<TickerRequestValidationError>());

        private TickerRequestValidationResult(
            string functionName,
            bool isValid,
            object value,
            TickerRequestSchemaValidationState schemaValidation,
            TickerRequestBindingValidationState bindingValidation,
            int? acceptedContractVersion,
            string acceptedContractFingerprint,
            ReadOnlyCollection<TickerRequestValidationError> errors)
        {
            FunctionName = functionName;
            IsValid = isValid;
            Value = value;
            SchemaValidation = schemaValidation;
            BindingValidation = bindingValidation;
            AcceptedContractVersion = acceptedContractVersion;
            AcceptedContractFingerprint = acceptedContractFingerprint;
            Errors = errors;
        }

        /// <summary>
        /// Whether the payload was accepted by every applicable authoritative check. For remote descriptors,
        /// schema enforcement can authorize a payload while <see cref="BindingValidation"/> is NotAvailable.
        /// </summary>
        public bool IsValid { get; }

        /// <summary>The function identity this result is for.</summary>
        public string FunctionName { get; }

        /// <summary>
        /// The deserialized request object on success, or null for a request-less function, an absent
        /// optional payload, or any failure.
        /// </summary>
        public object Value { get; }

        /// <summary>Contract version from the immutable registry snapshot that authorized this payload.</summary>
        public int? AcceptedContractVersion { get; }

        /// <summary>Schema fingerprint from the same snapshot that authorized this payload.</summary>
        public string AcceptedContractFingerprint { get; }

        /// <summary>How the contract schema was applied to the payload.</summary>
        public TickerRequestSchemaValidationState SchemaValidation { get; }

        /// <summary>How function-specific CLR binding was applied to the payload.</summary>
        public TickerRequestBindingValidationState BindingValidation { get; }

        /// <summary>Machine-readable errors; empty when <see cref="IsValid"/> is true.</summary>
        public IReadOnlyList<TickerRequestValidationError> Errors { get; }

        internal static TickerRequestValidationResult Valid(
            string functionName,
            object value,
            TickerRequestSchemaValidationState schemaValidation,
            TickerRequestBindingValidationState bindingValidation = TickerRequestBindingValidationState.NotApplicable,
            int? acceptedContractVersion = null,
            string acceptedContractFingerprint = null)
            => new TickerRequestValidationResult(
                functionName, true, value, schemaValidation, bindingValidation,
                acceptedContractVersion, acceptedContractFingerprint, NoErrors);

        internal static TickerRequestValidationResult Invalid(
            string functionName,
            TickerRequestSchemaValidationState schemaValidation,
            params TickerRequestValidationError[] errors)
        {
            if (errors == null || errors.Length == 0)
                throw new ArgumentException("An invalid result requires at least one error.", nameof(errors));
            return new TickerRequestValidationResult(
                functionName, false, null, schemaValidation, TickerRequestBindingValidationState.NotApplicable,
                null, null, new ReadOnlyCollection<TickerRequestValidationError>((TickerRequestValidationError[])errors.Clone()));
        }

        internal static TickerRequestValidationResult Invalid(
            string functionName,
            TickerRequestSchemaValidationState schemaValidation,
            IReadOnlyList<TickerRequestValidationError> errors)
        {
            if (errors == null || errors.Count == 0)
                throw new ArgumentException("An invalid result requires at least one error.", nameof(errors));
            var copy = new TickerRequestValidationError[errors.Count];
            for (var i = 0; i < errors.Count; i++) copy[i] = errors[i];
            return new TickerRequestValidationResult(
                functionName, false, null, schemaValidation, TickerRequestBindingValidationState.NotApplicable,
                null, null, new ReadOnlyCollection<TickerRequestValidationError>(copy));
        }

        public override string ToString()
            => IsValid
                ? $"{FunctionName}: valid (schema={SchemaValidation}, binding={BindingValidation})"
                : $"{FunctionName}: invalid (schema={SchemaValidation}, binding={BindingValidation}, errors={Errors.Count})";
    }
}
