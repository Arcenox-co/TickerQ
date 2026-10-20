using System;
using System.Collections.Generic;
using System.Text.Json;
using Json.Schema;
using TickerQ.Utilities.Models;

namespace TickerQ.Utilities.Serialization
{
    /// <summary>
    /// Native-AOT-compatible Draft 2020-12 evaluator for registered request contracts.
    /// JsonSchema.Net evaluates directly over <see cref="JsonElement"/> and supports the complete
    /// keyword vocabulary emitted by the source generator.
    /// </summary>
    internal static class TickerJsonSchemaValidator
    {
        private static readonly EvaluationOptions Options = new EvaluationOptions
        {
            OutputFormat = OutputFormat.List,
            RequireFormatValidation = true
        };

        public static IReadOnlyList<TickerRequestValidationError> Validate(
            JsonElement schemaElement,
            JsonElement instance)
        {
            // Building registers the root and its embedded resources. The default registry is
            // process-global, so rebuilding a schema without $id would retain a new random-URI
            // graph for every submitted request. Keep those registrations local to this validation.
            var defaults = BuildOptions.Default;
            var defaultRegistry = defaults.SchemaRegistry;
            var registry = new SchemaRegistry();
            if (!ReferenceEquals(defaultRegistry, SchemaRegistry.Global))
            {
                // Preserve explicitly configured external schemas/fetchers without publishing the
                // request schema into the caller's registry. JsonSchema.Net checks Global before
                // Fetch, so a custom registry shadowing a global URI does not override that order.
                registry.Fetch = (uri, _) => defaultRegistry.Get(uri);
            }

            var schema = JsonSchema.Build(schemaElement, new BuildOptions
            {
                SchemaRegistry = registry,
                VocabularyRegistry = defaults.VocabularyRegistry,
                DialectRegistry = defaults.DialectRegistry,
                Dialect = defaults.Dialect
            });
            var evaluation = schema.Evaluate(instance, Options);
            if (evaluation.IsValid)
                return Array.Empty<TickerRequestValidationError>();

            var errors = new List<TickerRequestValidationError>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            CollectErrors(evaluation, errors, seen);

            if (errors.Count == 0)
            {
                errors.Add(new TickerRequestValidationError(
                    TickerRequestValidationErrorCode.SchemaViolation,
                    "Payload does not satisfy the registered request schema."));
            }

            return errors;
        }

        public static bool IsSchemaException(Exception exception)
            => exception is JsonException
               || exception is JsonSchemaException
               || exception is RefResolutionException
               || exception is InvalidOperationException
               || exception is NotSupportedException;

        private static void CollectErrors(
            EvaluationResults result,
            List<TickerRequestValidationError> errors,
            HashSet<string> seen)
        {
            if (!result.IsValid && result.Errors != null)
            {
                var location = result.InstanceLocation.ToString();
                if (string.IsNullOrEmpty(location)) location = null;

                foreach (var error in result.Errors)
                {
                    var message = string.IsNullOrWhiteSpace(error.Value)
                        ? $"Payload violates schema keyword '{error.Key}'."
                        : error.Value;
                    var identity = error.Key + "\n" + location + "\n" + message;
                    if (!seen.Add(identity)) continue;

                    errors.Add(new TickerRequestValidationError(
                        TickerRequestValidationErrorCode.SchemaViolation,
                        message,
                        location));
                }
            }

            if (result.Details == null) return;

            foreach (var detail in result.Details)
                CollectErrors(detail, errors, seen);
        }
    }
}
