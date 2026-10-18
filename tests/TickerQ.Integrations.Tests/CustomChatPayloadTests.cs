using System.Net;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using TickerQ.Integrations.Infrastructure;
using TickerQ.Utilities.Integrations;

namespace TickerQ.Integrations.Tests;

public class CustomChatPayloadTests
{
    private const string SlackTemplate = """
        {"text":"Custom: {{function}}","blocks":[{"type":"section","text":{"type":"plain_text","text":"{{reason}}"}}],"unfurl_links":false}
        """;
    private const string TeamsTemplate = """
        {"type":"message","attachments":[{"contentType":"application/vnd.microsoft.card.adaptive","content":{"type":"AdaptiveCard","version":"1.2","body":[{"type":"TextBlock","text":"Custom: {{function}}","wrap":true},{"type":"TextBlock","text":"{{reason}}","wrap":true}]}}]}
        """;

    [Theory]
    [InlineData("slack")]
    [InlineData("teams")]
    public async Task SavedCustomPayload_IsUsedByPreviewAndDelivery_AndCanReturnToDefault(string provider)
    {
        var configuration = IntegrationTests.CreateConfiguration(new());
        var input = IntegrationTests.ValidConfiguration();
        var destination = input.Destinations[0];
        destination.Provider = provider;
        destination.PayloadTemplate = Template(provider);
        await configuration.SaveAsync(input, default);
        var handler = new IntegrationTests.StubHandler(HttpStatusCode.OK);
        using var engine = new IntegrationEngine(new(), configuration,
            new(new IntegrationTests.ClientFactory(handler), new()), NullLogger<IntegrationEngine>.Instance);

        var saved = (await engine.GetAsync()).Configuration;
        Assert.Equal(Template(provider), saved.Destinations[0].PayloadTemplate);
        Assert.Null(saved.Destinations[0].Url);
        var preview = engine.Preview(new() { Provider = provider, PayloadTemplate = saved.Destinations[0].PayloadTemplate });
        Assert.Equal(0, handler.Calls);
        Assert.Contains("Custom: Example.ProcessOrders", preview.Payload);
        Assert.Equal("Sent", (await engine.TestAsync(destination.Id)).Outcome);
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(preview.Payload), JsonNode.Parse(handler.Body)));

        var jobMessage = IntegrationEngine.Sample(new());
        jobMessage.Event.Function = "Orders.Process";
        jobMessage.Event.Reason = "Custom dispatch reason";
        await engine.DispatchAsync(jobMessage, default);
        Assert.Equal(2, handler.Calls);
        Assert.Contains("Custom: Orders.Process", handler.Body);
        Assert.Contains("Custom dispatch reason", handler.Body);

        saved.Destinations[0].PayloadTemplate = null;
        await configuration.SaveAsync(saved, default);
        Assert.Equal("Sent", (await engine.TestAsync(destination.Id)).Outcome);
        Assert.DoesNotContain("Custom:", handler.Body);
        Assert.Contains("Function: Example.ProcessOrders", handler.Body);
        Assert.Equal(destination.Url, (await configuration.LoadAsync(default)).Destinations[0].Url);
    }

    [Theory]
    [InlineData("slack")]
    [InlineData("teams")]
    public void CustomLayouts_SubstituteValuesWithoutInjectingJsonOrRescanningTokens(string provider)
    {
        IIntegrationPayloadFormatter formatter = provider == "slack" ? new SlackFormatter() : new TeamsFormatter();
        var message = IntegrationEngine.Sample(new());
        message.Event.Reason = "\"},\"injected\":true\n{{function}}";
        var payload = formatter.Format(new() { PayloadTemplate = Template(provider) }, message);
        var reason = provider == "slack"
            ? payload["blocks"]![0]!["text"]!["text"]
            : payload["attachments"]![0]!["content"]!["body"]![1]!["text"];
        Assert.Equal(message.Event.Reason, reason!.GetValue<string>());
        Assert.False(payload.ContainsKey("injected"));
    }

    [Theory]
    [InlineData("slack", "{}")]
    [InlineData("slack", "{\"text\":12}")]
    [InlineData("slack", "{\"text\":null}")]
    [InlineData("slack", "{\"blocks\":[null]}")]
    [InlineData("slack", "{\"text\":\"Alert\",\"blocks\":{}}")]
    [InlineData("slack", "{\"text\":\"{{unknown}}\"}")]
    [InlineData("slack", "{broken")]
    [InlineData("teams", "{\"text\":\"Wrong provider format\"}")]
    [InlineData("teams", "{\"type\":\"message\",\"attachments\":[]}")]
    [InlineData("teams", "{\"type\":\"message\",\"attachments\":[null]}")]
    [InlineData("teams", "{\"type\":\"message\",\"attachments\":[{\"contentType\":\"application/vnd.microsoft.card.adaptive\",\"content\":{\"type\":\"AdaptiveCard\",\"version\":\"1.2\",\"body\":{}}}]}")]
    [InlineData("teams", "{\"type\":\"{{unknown}}\"}")]
    [InlineData("teams", "{broken")]
    public async Task InvalidPayload_IsRejectedBeforeSavingOrSending(string provider, string template)
    {
        var store = new IntegrationTests.MemorySettings();
        var configuration = IntegrationTests.CreateConfiguration(store);
        var input = IntegrationTests.ValidConfiguration();
        input.Destinations[0].Provider = provider;
        input.Destinations[0].PayloadTemplate = template;
        await Assert.ThrowsAsync<ArgumentException>(() => configuration.SaveAsync(input, default));
        Assert.Null(store.Record);
        var handler = new IntegrationTests.StubHandler(HttpStatusCode.OK);
        using var engine = new IntegrationEngine(new(), configuration,
            new(new IntegrationTests.ClientFactory(handler), new()), NullLogger<IntegrationEngine>.Instance);
        Assert.Throws<ArgumentException>(() => engine.Preview(new() { Provider = provider, PayloadTemplate = template }));
        Assert.Equal(0, handler.Calls);
    }

    [Theory]
    [InlineData("{\"text\":\"Alert\"}")]
    [InlineData("{\"blocks\":[{\"type\":\"divider\"}]}")]
    [InlineData("{\"attachments\":[{\"text\":\"Alert\"}]}")]
    public void Slack_AcceptsTextOrBlocksOrAttachments(string template)
    {
        var result = new SlackFormatter().Format(new() { PayloadTemplate = template }, IntegrationEngine.Sample(new()));
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(template), result));
    }

    private static string Template(string provider) => provider == "slack" ? SlackTemplate : TeamsTemplate;
}
