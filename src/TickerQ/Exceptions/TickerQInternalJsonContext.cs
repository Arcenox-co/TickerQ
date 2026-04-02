using System.Text.Json.Serialization;

namespace TickerQ.Exceptions
{
    [JsonSerializable(typeof(ExceptionDetailClassForSerialization))]
    internal partial class TickerQInternalJsonContext : JsonSerializerContext { }
}
