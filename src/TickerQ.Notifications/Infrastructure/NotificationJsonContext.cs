using System.Text.Json.Serialization;
using TickerQ.Utilities.Notifications;

namespace TickerQ.Notifications.Infrastructure;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(NotificationConfiguration))]
internal partial class NotificationJsonContext : JsonSerializerContext;
