using System.Text.Json.Nodes;
using TickerQ.Utilities.Integrations;

namespace TickerQ.Integrations.Infrastructure;

internal sealed class WebhookFormatter : IIntegrationPayloadFormatter
{
    public string Name => "webhook";
    public JsonObject Format(IntegrationDestination destination, IntegrationMessage message)
        => MessageContent.Template(destination, message) is { } template && !string.IsNullOrWhiteSpace(template)
            ? PayloadTemplates.Render(template, message) : BuiltInPayloads.Create(Name, message);
}

internal sealed class SlackFormatter : IIntegrationPayloadFormatter
{
    public string Name => "slack";
    public JsonObject Format(IntegrationDestination destination, IntegrationMessage message)
        => MessageContent.Template(destination, message) is { } template && !string.IsNullOrWhiteSpace(template)
            ? PayloadTemplates.RenderSlack(template, message) : BuiltInPayloads.Create(Name, message);
}

internal sealed class TeamsFormatter : IIntegrationPayloadFormatter
{
    public string Name => "teams";
    public JsonObject Format(IntegrationDestination destination, IntegrationMessage message)
        => MessageContent.Template(destination, message) is { } template && !string.IsNullOrWhiteSpace(template)
            ? PayloadTemplates.RenderTeams(template, message) : BuiltInPayloads.Create(Name, message);
}

/// <summary>The starter JSON and default delivery use the same layout.</summary>
internal static class BuiltInPayloads
{
    internal static JsonObject Create(string provider, IntegrationMessage m, bool template = false)
    {
        var values = MessageContent.Values(m);
        string V(string key, string? value = null, int max = 1800) => template ? "{{" + key + "}}"
            : Limit(value ?? values.GetValueOrDefault(key, ""), max);
        var monitoring = MessageContent.Category(m) != "job_alert";
        var fields = new List<(string Label, string Value)>
        {
            ("Function", V("function", m.Event.Function)),
            ("Application", V("application", m.Application))
        };
        if (!string.IsNullOrWhiteSpace(m.Environment)) fields.Add(("Environment", V("environment", m.Environment)));
        if (monitoring)
        {
            fields.Add(("Monitor", V("monitorName", m.MonitorName)));
            fields.Add(("Condition", V("condition")));
            if (values["threshold"].Length > 0) fields.Add(("Threshold", V("threshold")));
            if (values["observed"].Length > 0) fields.Add(("Observed", V("observed")));
            if (!string.IsNullOrWhiteSpace(m.TimeZoneId)) fields.Add(("Time zone", V("timeZoneId")));
            if (m.GraceMinutes != null) fields.Add(("Grace period", V("graceMinutes") + " minutes"));
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(m.Event.Node)) fields.Add(("Worker", V("node", m.Event.Node)));
            fields.Add(("Retries", V("retryCount", m.Event.RetryCount.ToString()) + " / " + V("retries", m.Event.Retries.ToString())));
        }
        var heading = V("statusIcon") + " " + V("title");
        var context = "TickerQ · " + V("occurredAt");
        if (m.IncidentId != null) context += " · Incident " + V("incidentId", m.IncidentId.ToString());
        else if (m.Event.TickerId != Guid.Empty) context += " · Job " + V("tickerId", m.Event.TickerId.ToString());
        if (provider == "slack")
        {
            JsonObject Text(string text) => new() { ["type"] = "plain_text", ["text"] = text, ["emoji"] = true };
            var blocks = new JsonArray(
                new JsonObject { ["type"] = "header", ["text"] = Text(heading) },
                new JsonObject { ["type"] = "section", ["fields"] = new JsonArray(fields.Select(f => (JsonNode)new JsonObject
                    { ["type"] = "plain_text", ["text"] = f.Label + "\n" + f.Value }).ToArray()) });
            if (!string.IsNullOrWhiteSpace(m.Event.Reason)) blocks.Add((JsonNode)new JsonObject { ["type"] = "section", ["text"] = Text(V("reason", m.Event.Reason, 2900)) });
            blocks.Add((JsonNode)new JsonObject { ["type"] = "context", ["elements"] = new JsonArray(Text(context)) });
            if (!string.IsNullOrEmpty(m.DashboardUrl)) blocks.Add((JsonNode)new JsonObject
            {
                ["type"] = "actions", ["elements"] = new JsonArray(new JsonObject
                {
                    ["type"] = "button", ["text"] = Text("Open dashboard"), ["url"] = V("dashboardUrl", m.DashboardUrl, 3000)
                })
            });
            return new() { ["text"] = V("title") + " · " + V("function", m.Event.Function), ["blocks"] = blocks,
                ["mrkdwn"] = false, ["unfurl_links"] = false, ["unfurl_media"] = false };
        }
        if (provider == "teams")
        {
            var body = new JsonArray(
                new JsonObject { ["type"] = "TextBlock", ["text"] = heading, ["size"] = "Medium", ["weight"] = "Bolder", ["color"] = V("statusColor"), ["wrap"] = true },
                new JsonObject { ["type"] = "FactSet", ["facts"] = new JsonArray(fields.Select(f => (JsonNode)new JsonObject { ["title"] = f.Label, ["value"] = f.Value }).ToArray()) });
            if (!string.IsNullOrWhiteSpace(m.Event.Reason)) body.Add((JsonNode)new JsonObject { ["type"] = "TextBlock", ["text"] = V("reason", m.Event.Reason), ["wrap"] = true, ["separator"] = true });
            body.Add((JsonNode)new JsonObject { ["type"] = "TextBlock", ["text"] = context, ["size"] = "Small", ["isSubtle"] = true, ["wrap"] = true });
            var card = new JsonObject { ["type"] = "AdaptiveCard", ["version"] = "1.2", ["$schema"] = "http://adaptivecards.io/schemas/adaptive-card.json", ["body"] = body };
            if (!string.IsNullOrEmpty(m.DashboardUrl)) card["actions"] = new JsonArray(new JsonObject { ["type"] = "Action.OpenUrl", ["title"] = "Open dashboard", ["url"] = V("dashboardUrl", m.DashboardUrl, 3000) });
            return new() { ["type"] = "message", ["attachments"] = new JsonArray(new JsonObject { ["contentType"] = "application/vnd.microsoft.card.adaptive", ["content"] = card }) };
        }
        // Preserve the webhook's existing event envelope; new context is additive.
        var payload = new JsonObject
        {
            ["eventId"] = V("eventId", m.EventId.ToString()), ["application"] = V("application", m.Application), ["environment"] = V("environment", m.Environment),
            ["event"] = V("event", m.Event.Kind), ["function"] = V("function", m.Event.Function), ["reason"] = V("reason", m.Event.Reason, 8000),
            ["tickerId"] = V("tickerId", m.Event.TickerId.ToString()), ["tickerType"] = V("tickerType", m.Event.TickerType),
            ["retryCount"] = template ? JsonValue.Create("{{retryCount}}") : JsonValue.Create(m.Event.RetryCount),
            ["retries"] = template ? JsonValue.Create("{{retries}}") : JsonValue.Create(m.Event.Retries), ["node"] = V("node", m.Event.Node),
            ["occurredAtUtc"] = V("occurredAtUtc", m.Event.OccurredAtUtc.ToString("O")), ["dashboardUrl"] = V("dashboardUrl", m.DashboardUrl, 3000),
            ["monitorId"] = template ? "{{monitorId}}" : m.MonitorId?.ToString(), ["monitorName"] = template ? "{{monitorName}}" : m.MonitorName,
            ["incidentId"] = template ? "{{incidentId}}" : m.IncidentId?.ToString(), ["title"] = V("title"), ["severity"] = V("severity"),
            ["monitorKind"] = template ? "{{monitorKind}}" : m.MonitorKind,
            ["thresholdSeconds"] = template ? JsonValue.Create("{{thresholdSeconds}}") : JsonValue.Create(m.ThresholdSeconds),
            ["observedDurationSeconds"] = template ? JsonValue.Create("{{observedDurationSeconds}}") : JsonValue.Create(m.ObservedDurationSeconds),
            ["failureCount"] = template ? JsonValue.Create("{{failureCount}}") : JsonValue.Create(m.FailureCount),
            ["failureThreshold"] = template ? JsonValue.Create("{{failureThreshold}}") : JsonValue.Create(m.FailureThreshold),
            ["deadlineUtc"] = template ? "{{deadlineUtc}}" : m.DeadlineUtc?.ToString("O"),
            ["timeZoneId"] = template ? "{{timeZoneId}}" : m.TimeZoneId,
            ["graceMinutes"] = template ? JsonValue.Create("{{graceMinutes}}") : JsonValue.Create(m.GraceMinutes)
        };
        return payload;
    }
    private static string Limit(string value, int max) => value.Length <= max ? value : value[..(max - 1)] + "…";
}
