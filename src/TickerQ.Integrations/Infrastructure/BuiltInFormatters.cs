using System.Text.Json.Nodes;
using TickerQ.Utilities.Integrations;

namespace TickerQ.Integrations.Infrastructure;

internal sealed class WebhookFormatter : IIntegrationPayloadFormatter
{
    public string Name => "webhook";
    public JsonObject Format(IntegrationDestination destination, IntegrationMessage m) => !string.IsNullOrWhiteSpace(destination.PayloadTemplate)
        ? PayloadTemplates.Render(destination.PayloadTemplate, m)
        : new JsonObject
        {
            ["eventId"] = m.EventId.ToString(), ["application"] = m.Application, ["environment"] = m.Environment,
            ["event"] = m.Event.Kind, ["function"] = m.Event.Function, ["reason"] = m.Event.Reason,
            ["tickerId"] = m.Event.TickerId.ToString(), ["tickerType"] = m.Event.TickerType,
            ["retryCount"] = m.Event.RetryCount, ["retries"] = m.Event.Retries, ["node"] = m.Event.Node,
            ["occurredAtUtc"] = m.Event.OccurredAtUtc.ToString("O"), ["dashboardUrl"] = m.DashboardUrl
        };
}

internal sealed class SlackFormatter : IIntegrationPayloadFormatter
{
    public string Name => "slack";
    public JsonObject Format(IntegrationDestination destination, IntegrationMessage m)
    {
        if (!string.IsNullOrWhiteSpace(destination.PayloadTemplate))
            return PayloadTemplates.RenderSlack(destination.PayloadTemplate, m);
        var title = $"{m.Application} · {m.Environment} · {m.Event.Kind}";
        var summary = $"Function: {m.Event.Function}\nNode: {m.Event.Node}\nRetries: {m.Event.RetryCount}/{m.Event.Retries}\n{m.Event.Reason}";
        var blocks = new JsonArray(
            new JsonObject { ["type"] = "section", ["text"] = new JsonObject { ["type"] = "plain_text", ["text"] = title + "\n" + summary } }
        );
        if (m.DashboardUrl.Length > 0) blocks.Add((JsonNode)new JsonObject
        {
            ["type"] = "actions", ["elements"] = new JsonArray(new JsonObject
            {
                ["type"] = "button", ["text"] = new JsonObject { ["type"] = "plain_text", ["text"] = "Open dashboard" }, ["url"] = m.DashboardUrl
            })
        });
        return new JsonObject { ["text"] = title, ["blocks"] = blocks, ["unfurl_links"] = false, ["unfurl_media"] = false };
    }
}

internal sealed class TeamsFormatter : IIntegrationPayloadFormatter
{
    public string Name => "teams";
    public JsonObject Format(IntegrationDestination destination, IntegrationMessage m)
    {
        if (!string.IsNullOrWhiteSpace(destination.PayloadTemplate))
            return PayloadTemplates.RenderTeams(destination.PayloadTemplate, m);
        var card = new JsonObject
        {
            ["type"] = "AdaptiveCard", ["version"] = "1.2", ["$schema"] = "http://adaptivecards.io/schemas/adaptive-card.json",
            ["body"] = new JsonArray(
                new JsonObject { ["type"] = "TextBlock", ["text"] = $"{m.Application} · {m.Environment} · {m.Event.Kind}", ["weight"] = "Bolder", ["wrap"] = true },
                new JsonObject { ["type"] = "TextBlock", ["text"] = $"Function: {m.Event.Function}\nNode: {m.Event.Node}\nRetries: {m.Event.RetryCount}/{m.Event.Retries}", ["wrap"] = true },
                new JsonObject { ["type"] = "TextBlock", ["text"] = m.Event.Reason, ["wrap"] = true })
        };
        if (m.DashboardUrl.Length > 0) card["actions"] = new JsonArray(new JsonObject { ["type"] = "Action.OpenUrl", ["title"] = "Open dashboard", ["url"] = m.DashboardUrl });
        return new JsonObject { ["type"] = "message", ["attachments"] = new JsonArray(new JsonObject { ["contentType"] = "application/vnd.microsoft.card.adaptive", ["content"] = card }) };
    }
}
