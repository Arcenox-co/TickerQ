using System.Text.Json.Serialization;
using TickerQ.Utilities.Integrations;

namespace TickerQ.Integrations.Infrastructure;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(IntegrationConfiguration))]
internal partial class IntegrationJsonContext : JsonSerializerContext;
