using System;
using System.Text.Json.Serialization.Metadata;

namespace TickerQ.Utilities.Models
{
    /// <summary>Local-only CLR and source-generated serializer metadata for a declared result.</summary>
    internal sealed record TickerRuntimeResultMetadata(Type ResultType, JsonTypeInfo JsonTypeInfo);
}
