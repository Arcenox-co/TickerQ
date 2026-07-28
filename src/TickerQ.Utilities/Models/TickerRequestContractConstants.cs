namespace TickerQ.Utilities.Models
{
    /// <summary>
    /// Canonical constants for the versioned request-contract wire shape.
    /// Centralised so local, dashboard, Hub, and SDK paths agree on the same values.
    /// </summary>
    public static class TickerRequestContractConstants
    {
        /// <summary>First contract version. Contract versions start at 1.</summary>
        public const int InitialContractVersion = 1;

        /// <summary>Default request media type.</summary>
        public const string DefaultMediaType = "application/json";

        /// <summary>JSON Schema dialect used by contract version 1.</summary>
        public const string SchemaDialect2020_12 = "https://json-schema.org/draft/2020-12/schema";

        /// <summary>Conventional key for a single generated example.</summary>
        public const string DefaultExampleKey = "default";
    }
}
