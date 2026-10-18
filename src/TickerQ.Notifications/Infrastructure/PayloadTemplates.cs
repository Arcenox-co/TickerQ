using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace TickerQ.Notifications.Infrastructure;

internal static partial class PayloadTemplates
{
    internal static readonly string[] Tokens = ["eventId", "application", "environment", "event", "function", "reason", "tickerId", "tickerType", "node", "retryCount", "retries", "occurredAtUtc", "dashboardUrl"];

    [GeneratedRegex(@"\{\{\s*([a-zA-Z][a-zA-Z0-9]*)\s*\}\}")]
    private static partial Regex Placeholder();

    internal static JsonObject Render(string template, NotificationMessage message)
    {
        if (template.Length > 16000) throw new ArgumentException("Payload template must be at most 16,000 characters.");
        JsonObject root;
        try { root = JsonNode.Parse(template, documentOptions: new JsonDocumentOptions { MaxDepth = 16 }) as JsonObject
            ?? throw new ArgumentException("Payload template must be a JSON object."); }
        catch (JsonException) { throw new ArgumentException("Payload template must be valid JSON (maximum depth 16)."); }
        var e = message.Event;
        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["eventId"] = message.EventId.ToString(), ["application"] = message.Application, ["environment"] = message.Environment,
            ["event"] = e.Kind ?? "", ["function"] = e.Function ?? "", ["reason"] = e.Reason ?? "", ["tickerId"] = e.TickerId.ToString(),
            ["tickerType"] = e.TickerType ?? "", ["node"] = e.Node ?? "", ["retryCount"] = e.RetryCount.ToString(CultureInfo.InvariantCulture),
            ["retries"] = e.Retries.ToString(CultureInfo.InvariantCulture), ["occurredAtUtc"] = e.OccurredAtUtc.ToString("O", CultureInfo.InvariantCulture),
            ["dashboardUrl"] = message.DashboardUrl
        };
        string Substitute(string text)
        {
            // Validate the original template, never scan substituted event text for tokens.
            if (Placeholder().Replace(text, "").Contains("{{", StringComparison.Ordinal))
                throw new ArgumentException("Malformed payload placeholder. Use {{function}}, for example.");
            return Placeholder().Replace(text, m => values.TryGetValue(m.Groups[1].Value, out var value) ? value
                : throw new ArgumentException($"Unknown payload placeholder: {m.Groups[1].Value}."));
        }
        void Visit(JsonNode node)
        {
            if (node is JsonObject obj)
            {
                foreach (var (key, value) in obj.ToArray())
                {
                    if (key.Contains("{{", StringComparison.Ordinal)) throw new ArgumentException("Placeholders are allowed in JSON string values, not property names.");
                    if (value is JsonValue v && v.TryGetValue<string>(out var text)) obj[key] = Substitute(text);
                    else if (value != null) Visit(value);
                }
            }
            else if (node is JsonArray array)
                for (var i = 0; i < array.Count; i++)
                    if (array[i] is JsonValue v && v.TryGetValue<string>(out var text)) array[i] = Substitute(text);
                    else if (array[i] is { } child) Visit(child);
        }
        Visit(root);
        return root;
    }
}
