using System;
using System.Text.Json;
using TickerQ.Utilities.Models;
using TickerQ.Utilities.Serialization;

namespace TickerQ.Utilities
{
    /// <summary>
    /// Server-authoritative request payload validation and deserialization service. Given a function
    /// identity and the stored/raw <c>byte[]</c> payload it resolves the canonical
    /// <see cref="TickerFunctionDescriptor"/> and the function-specific registered <c>JsonTypeInfo</c>,
    /// enforces request-less vs required payload semantics, decompresses/parses through the existing
    /// <see cref="TickerHelper"/> conventions, validates against the contract's Draft 2020-12 schema when
    /// one exists through the Native-AOT-compatible Draft 2020-12 evaluator, then deserializes with the
    /// exact <c>JsonTypeInfo</c> so schema-valid-but-CLR-invalid payloads also fail.
    /// </summary>
    /// <remarks>
    /// <para>
    /// It is fully reflection-free and Native-AOT safe: binding always goes through the registered
    /// source-generated/context <c>JsonTypeInfo</c>, never the legacy reflection fallback. A remote
    /// descriptor with no local <c>JsonTypeInfo</c> may still be accepted when its full schema was enforced;
    /// the result reports binding as unavailable rather than silently falling back to reflection.
    /// </para>
    /// <para>
    /// It never throws for ordinary user payload invalidity — those outcomes are returned as a structured
    /// <see cref="TickerRequestValidationResult"/> with machine-readable errors. It throws only for a
    /// null <paramref name="functionName"/>, which is a caller programming error.
    /// </para>
    /// </remarks>
    public static class TickerRequestPayloadValidator
    {
        /// <summary>
        /// Validates and deserializes a request payload for the named function against its canonical
        /// contract. See the type remarks for semantics. Returns a structured result; does not throw for
        /// user payload invalidity.
        /// </summary>
        public static TickerRequestValidationResult Validate(string functionName, byte[] payload)
        {
            if (functionName == null)
                throw new ArgumentNullException(nameof(functionName));

            return Validate(TickerFunctionProvider.Snapshot, functionName, payload);
        }

        internal static TickerRequestValidationResult Validate(
            TickerFunctionRegistrySnapshot snapshot,
            string functionName,
            byte[] payload)
        {
            if (snapshot == null)
                throw new ArgumentNullException(nameof(snapshot));
            if (functionName == null)
                throw new ArgumentNullException(nameof(functionName));

            if (!snapshot.Descriptors.TryGetValue(functionName, out var descriptor))
                return TickerRequestValidationResult.Invalid(
                    functionName, TickerRequestSchemaValidationState.NotApplicable,
                    new TickerRequestValidationError(
                        TickerRequestValidationErrorCode.UnknownFunction,
                        $"No request contract is registered for function '{functionName}'."));

            var hasPayload = payload != null && payload.Length > 0;
            var contract = descriptor.Request;

            // ---- Request-less semantics: a payload is never permitted; absence is success.
            if (contract == null)
            {
                if (hasPayload)
                    return TickerRequestValidationResult.Invalid(
                        functionName, TickerRequestSchemaValidationState.NotApplicable,
                        new TickerRequestValidationError(
                            TickerRequestValidationErrorCode.PayloadNotAllowed,
                            $"Function '{functionName}' does not accept a request payload."));

                return TickerRequestValidationResult.Valid(
                    functionName, null, TickerRequestSchemaValidationState.NotApplicable,
                    acceptedContractVersion: descriptor.ContractVersion);
            }

            // ---- Required vs optional emptiness.
            if (!hasPayload)
            {
                if (contract.Required)
                    return TickerRequestValidationResult.Invalid(
                        functionName, TickerRequestSchemaValidationState.NotApplicable,
                        new TickerRequestValidationError(
                            TickerRequestValidationErrorCode.PayloadRequiredButMissing,
                            $"Function '{functionName}' requires a request payload."));

                return TickerRequestValidationResult.Valid(
                    functionName, null, TickerRequestSchemaValidationState.NotApplicable,
                    acceptedContractVersion: descriptor.ContractVersion,
                    acceptedContractFingerprint: contract.Fingerprint);
            }

            // ---- Decompress/parse through the existing TickerHelper conventions.
            string json;
            try
            {
                json = TickerHelper.ReadTickerRequestAsString(payload);
            }
            catch (Exception exception)
            {
                return TickerRequestValidationResult.Invalid(
                    functionName, TickerRequestSchemaValidationState.NotApplicable,
                    new TickerRequestValidationError(
                        TickerRequestValidationErrorCode.MalformedPayload,
                        $"Payload could not be decompressed: {exception.Message}"));
            }

            if (string.IsNullOrWhiteSpace(json))
                return TickerRequestValidationResult.Invalid(
                    functionName, TickerRequestSchemaValidationState.NotApplicable,
                    new TickerRequestValidationError(
                        TickerRequestValidationErrorCode.MalformedPayload,
                        "Payload decompressed to an empty document."));

            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(json);
            }
            catch (JsonException exception)
            {
                return TickerRequestValidationResult.Invalid(
                    functionName, TickerRequestSchemaValidationState.NotApplicable,
                    new TickerRequestValidationError(
                        TickerRequestValidationErrorCode.MalformedPayload,
                        $"Payload is not well-formed JSON: {exception.Message}"));
            }

            using (document)
            {
                var instance = document.RootElement;

                // ---- Full Draft 2020-12 schema validation when a schema exists.
                var schemaState = TickerRequestSchemaValidationState.NotApplicable;
                if (contract.Schema.HasValue)
                {
                    try
                    {
                        schemaState = TickerRequestSchemaValidationState.Enforced;
                        var violations = TickerJsonSchemaValidator.Validate(contract.Schema.Value, instance);
                        if (violations.Count > 0)
                            return TickerRequestValidationResult.Invalid(functionName, schemaState, violations);
                    }
                    catch (Exception exception) when (TickerJsonSchemaValidator.IsSchemaException(exception))
                    {
                        schemaState = TickerRequestSchemaValidationState.NotEnforced;
                        return TickerRequestValidationResult.Invalid(
                            functionName, schemaState,
                            new TickerRequestValidationError(
                                TickerRequestValidationErrorCode.ContractSchemaInvalid,
                                $"The registered request schema for '{functionName}' could not be evaluated: {exception.Message}"));
                    }
                }

                // ---- Deserialize with the exact function-specific JsonTypeInfo (never reflection).
                if (!snapshot.RuntimeRequests.TryGetValue(functionName, out var runtime)
                    || runtime.JsonTypeInfo == null)
                {
                    if (schemaState == TickerRequestSchemaValidationState.Enforced)
                        return TickerRequestValidationResult.Valid(
                            functionName, null, schemaState,
                            TickerRequestBindingValidationState.NotAvailable,
                            descriptor.ContractVersion, contract.Fingerprint);

                    return TickerRequestValidationResult.Invalid(
                        functionName, schemaState,
                        new TickerRequestValidationError(
                            TickerRequestValidationErrorCode.MissingSerializerMetadata,
                            $"No JsonTypeInfo is registered for function '{functionName}'; the payload cannot be bound under Native AOT."));
                }

                object value;
                try
                {
                    value = JsonSerializer.Deserialize(json, runtime.JsonTypeInfo);
                }
                catch (Exception exception) when (exception is JsonException || exception is NotSupportedException)
                {
                    return TickerRequestValidationResult.Invalid(
                        functionName, schemaState,
                        new TickerRequestValidationError(
                            TickerRequestValidationErrorCode.DeserializationFailed,
                            $"Payload could not be bound to '{contract.TypeName}': {exception.Message}"));
                }

                return TickerRequestValidationResult.Valid(
                    functionName, value, schemaState, TickerRequestBindingValidationState.Enforced,
                    descriptor.ContractVersion, contract.Fingerprint);
            }
        }
    }
}
