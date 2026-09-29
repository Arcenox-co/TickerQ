using System;
using System.Text.Json.Serialization.Metadata;

namespace TickerQ.Utilities.Models
{
    /// <summary>
    /// Internal runtime execution metadata for a function's request: the CLR <see cref="Type"/> and
    /// resolved <see cref="JsonTypeInfo"/> used for execution. This is deliberately kept OUT of the
    /// wire models (<see cref="TickerFunctionDescriptor"/> / <see cref="TickerRequestContract"/>) so
    /// no runtime/serializer metadata leaks onto the transport. The nullable constructor value exists
    /// only during transactional Build staging; every published typed registration has metadata.
    /// </summary>
    internal sealed record TickerRuntimeRequestMetadata(Type RequestType, JsonTypeInfo JsonTypeInfo = null);
}
