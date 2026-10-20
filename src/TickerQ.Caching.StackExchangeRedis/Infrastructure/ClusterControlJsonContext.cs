using System.Text.Json.Serialization;
using TickerQ.Utilities.Cluster;

namespace TickerQ.Caching.StackExchangeRedis.Infrastructure;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(TickerQNodeRecord))]
[JsonSerializable(typeof(TickerQControlCommand))]
internal partial class ClusterControlJsonContext : JsonSerializerContext;
