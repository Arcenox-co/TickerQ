using System;

namespace TickerQ.SourceGenerator.Generation.Schema
{
    /// <summary>
    /// Signals that a request type's wire shape cannot be inferred at compile time. Callers translate
    /// this into a generator diagnostic and emit a descriptor without a schema — never a misleading one.
    /// </summary>
    internal sealed class UnsupportedSchemaException : Exception
    {
        public UnsupportedSchemaException(string reason, string converterType = null)
            : base(reason)
        {
            Reason = reason;
            ConverterType = converterType;
        }

        /// <summary>Human-readable reason the wire shape is unsupported.</summary>
        public string Reason { get; }

        /// <summary>Custom converter type that changed the wire shape, if applicable; otherwise null.</summary>
        public string ConverterType { get; }
    }
}
