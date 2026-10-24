using System.Text.Json;
using System.Text.Json.Serialization;
using TickerQ.Utilities.Models;

namespace TickerQ.Utilities.Infrastructure;

public static class SchedulePolicySerializer
{
    public static string Serialize(SchedulePolicy policy)
        => JsonSerializer.Serialize(policy, SchedulePolicyJsonContext.Default.SchedulePolicy);

    public static SchedulePolicy Deserialize(string json)
        => string.IsNullOrWhiteSpace(json) ? null
            : JsonSerializer.Deserialize(json, SchedulePolicyJsonContext.Default.SchedulePolicy);
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(SchedulePolicy))]
[JsonSerializable(typeof(ScheduleTimeWindow))]
[JsonSerializable(typeof(ScheduleAnnualDate))]
internal partial class SchedulePolicyJsonContext : JsonSerializerContext { }
