using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace TickerQ.Integrations.Infrastructure;

internal static partial class PayloadTemplates
{
    internal static readonly string[] Tokens = ["eventId", "application", "environment", "event", "function", "reason", "tickerId", "tickerType", "node", "retryCount", "retries", "occurredAtUtc", "dashboardUrl", "monitorId", "monitorName", "incidentId",
        "title", "severity", "statusIcon", "statusColor", "condition", "threshold", "observed", "occurredAt",
        "monitorKind", "thresholdSeconds", "observedDurationSeconds", "failureCount", "failureThreshold", "deadlineUtc", "timeZoneId", "graceMinutes"];

    [GeneratedRegex(@"\{\{\s*([a-zA-Z][a-zA-Z0-9]*)\s*\}\}")]
    private static partial Regex Placeholder();

    internal static JsonObject RenderSlack(string template, IntegrationMessage message)
    {
        var payload = Render(template, message);
        if (payload.ContainsKey("text") && StringValue(payload["text"]) == null)
            throw new ArgumentException("Slack payload text must be a string.");
        foreach (var name in new[] { "blocks", "attachments" })
            if (payload.ContainsKey(name) && (payload[name] is not JsonArray items || items.Any(item => item is not JsonObject)))
                throw new ArgumentException($"Slack payload {name} must be an array of objects.");
        if (string.IsNullOrWhiteSpace(StringValue(payload["text"]))
            && payload["blocks"] is not JsonArray { Count: > 0 }
            && payload["attachments"] is not JsonArray { Count: > 0 })
            throw new ArgumentException("Slack payload needs text, blocks, or attachments. Start with the Slack template.");
        return payload;
    }

    internal static JsonObject RenderTeams(string template, IntegrationMessage message)
    {
        var payload = Render(template, message);
        if (StringValue(payload["type"]) != "message" || payload["attachments"] is not JsonArray { Count: > 0 } attachments)
            throw new ArgumentException("Teams payload needs type \"message\" and an attachments array. Start with the Teams template.");
        foreach (var attachment in attachments)
        {
            if (attachment is not JsonObject item
                || StringValue(item["contentType"]) != "application/vnd.microsoft.card.adaptive"
                || item["content"] is not JsonObject card
                || StringValue(card["type"]) != "AdaptiveCard"
                || string.IsNullOrWhiteSpace(StringValue(card["version"]))
                || (card.ContainsKey("body") && card["body"] is not JsonArray))
                throw new ArgumentException("Each Teams attachment needs contentType \"application/vnd.microsoft.card.adaptive\" and AdaptiveCard content with a version and, when present, a body array.");
        }
        return payload;
    }

    private static string? StringValue(JsonNode? node)
        => node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    internal static JsonObject Render(string template, IntegrationMessage message)
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
            ["dashboardUrl"] = message.DashboardUrl, ["monitorId"] = message.MonitorId?.ToString() ?? "",
            ["monitorName"] = message.MonitorName ?? "", ["incidentId"] = message.IncidentId?.ToString() ?? ""
        };
        foreach (var (key, value) in MessageContent.Values(message)) values[key] = value;
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
