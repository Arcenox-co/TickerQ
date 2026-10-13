namespace TickerQ.SourceGenerator.Generation.Schema
{
    /// <summary>
    /// Outcome of compile-time schema emission for a single request type. When
    /// <see cref="Supported"/> is false, <see cref="SchemaJson"/>/<see cref="ExampleJson"/> are null
    /// and the caller must report a diagnostic and emit a descriptor without a schema — never a
    /// misleading one.
    /// </summary>
    internal sealed class SchemaEmissionResult
    {
        private SchemaEmissionResult(bool supported, string schemaJson, string exampleJson, string reason, string converterType)
        {
            Supported = supported;
            SchemaJson = schemaJson;
            ExampleJson = exampleJson;
            Reason = reason;
            ConverterType = converterType;
        }

        public bool Supported { get; }

        /// <summary>Emitted JSON Schema 2020-12 document (compact, canonical), or null when unsupported.</summary>
        public string SchemaJson { get; }

        /// <summary>One deterministic default example JSON value, or null when unsupported.</summary>
        public string ExampleJson { get; }

        /// <summary>Human-readable reason a schema could not be produced (unsupported case only).</summary>
        public string Reason { get; }

        /// <summary>Custom converter type name that changed the wire shape, when applicable.</summary>
        public string ConverterType { get; }

        public static SchemaEmissionResult ForSupported(string schemaJson, string exampleJson)
            => new SchemaEmissionResult(true, schemaJson, exampleJson, null, null);

        public static SchemaEmissionResult ForUnsupported(string reason, string converterType)
            => new SchemaEmissionResult(false, null, null, reason, converterType);
    }
}
