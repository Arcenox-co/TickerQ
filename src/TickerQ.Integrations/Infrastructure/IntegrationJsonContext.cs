using System.Text.Json.Serialization;
using TickerQ.Utilities.Integrations;
using TickerQ.Integrations.Monitoring;

namespace TickerQ.Integrations.Infrastructure;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(IntegrationConfiguration))]
[JsonSerializable(typeof(IntegrationMonitor))]
[JsonSerializable(typeof(MonitoringState))]
internal partial class IntegrationJsonContext : JsonSerializerContext;
