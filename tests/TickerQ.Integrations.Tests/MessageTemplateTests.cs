using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using TickerQ.Integrations.Infrastructure;
using TickerQ.Utilities.Integrations;

namespace TickerQ.Integrations.Tests;

public class MessageTemplateTests
{
    private static IIntegrationPayloadFormatter Formatter(string provider) => provider switch
    { "slack" => new SlackFormatter(), "teams" => new TeamsFormatter(), _ => new WebhookFormatter() };
    private static string Template(string provider, string marker) => provider == "teams"
        ? $$$$"""{"type":"message","attachments":[{"contentType":"application/vnd.microsoft.card.adaptive","content":{"type":"AdaptiveCard","version":"1.2","body":[{"type":"TextBlock","text":"{{{{marker}}}} {{title}} {{monitorName}}"}]}}]}"""
        : $$$$"""{"text":"{{{{marker}}}} {{title}} {{monitorName}}"}""";

    [Theory]
    [InlineData("slack")]
    [InlineData("teams")]
    [InlineData("webhook")]
    public async Task Categories_PersistAndRouteIndependently_IncludingMonitorDeliveries(string provider)
    {
        var configuration = IntegrationTests.CreateConfiguration(new());
        var input = IntegrationTests.ValidConfiguration();
        var destination = input.Destinations[0];
        destination.Provider = provider;
        destination.MessageTemplates = new()
        {
            JobAlert = Template(provider, "JOB-TEMPLATE"),
            MonitoringAlert = Template(provider, "ALERT-TEMPLATE"),
            Recovery = Template(provider, "RECOVERY-TEMPLATE")
        };
        await configuration.SaveAsync(input, default);
        var saved = (await configuration.LoadAsync(default)).Destinations[0];
        Assert.Equal(destination.MessageTemplates.Recovery, saved.MessageTemplates!.Recovery);
        var handler = new IntegrationTests.StubHandler(HttpStatusCode.OK);
        using var engine = new IntegrationEngine(new(), configuration, new(new IntegrationTests.ClientFactory(handler), new()), NullLogger<IntegrationEngine>.Instance);
        foreach (var (category, marker) in new[] { ("job_alert", "JOB-TEMPLATE"), ("monitoring_alert", "ALERT-TEMPLATE"), ("recovery", "RECOVERY-TEMPLATE") })
        {
            var message = IntegrationEngine.Sample(new(), category);
            if (category == "job_alert") await engine.DispatchAsync(message, default);
            else await engine.SendMonitorAsync(saved, message, default);
            Assert.Contains(marker, handler.Body);
            var preview = engine.Preview(new() { Provider = provider, MessageKind = category, PayloadTemplate = MessageContent.Template(saved, message) });
            Assert.True(JsonNode.DeepEquals(JsonNode.Parse(handler.Body), JsonNode.Parse(preview.Payload)));
        }
        Assert.Equal(3, handler.Calls); // previews never invoke transport
    }

    [Theory]
    [InlineData("slack")]
    [InlineData("teams")]
    [InlineData("webhook")]
    public void LegacySharedTemplate_RemainsShared_UntilCategoriesAreExplicit(string provider)
    {
        var destination = new IntegrationDestination { PayloadTemplate = Template(provider, "LEGACY") };
        foreach (var category in MessageContent.Categories)
            Assert.Contains("LEGACY", Formatter(provider).Format(destination, IntegrationEngine.Sample(new(), category)).ToJsonString());
        destination.MessageTemplates = new() { MonitoringAlert = Template(provider, "MONITOR-ONLY") };
        Assert.DoesNotContain("LEGACY", Formatter(provider).Format(destination, IntegrationEngine.Sample(new())).ToJsonString());
        Assert.DoesNotContain("MONITOR-ONLY", Formatter(provider).Format(destination, IntegrationEngine.Sample(new(), "recovery")).ToJsonString());
        Assert.Contains("MONITOR-ONLY", Formatter(provider).Format(destination, IntegrationEngine.Sample(new(), "monitoring_alert")).ToJsonString());
    }

    [Theory]
    [InlineData("slack")]
    [InlineData("teams")]
    [InlineData("webhook")]
    public async Task InvalidRecoveryTemplate_PreventsEntireSave(string provider)
    {
        var store = new IntegrationTests.MemorySettings();
        var configuration = IntegrationTests.CreateConfiguration(store);
        var input = IntegrationTests.ValidConfiguration();
        input.Destinations[0].Provider = provider;
        input.Destinations[0].MessageTemplates = new() { Recovery = Template(provider, "{{unknown}}") };
        var error = await Assert.ThrowsAsync<ArgumentException>(() => configuration.SaveAsync(input, default));
        Assert.Contains("recovery template", error.Message);
        Assert.Null(store.Record);
    }

    [Theory]
    [InlineData("slack", "job_alert", "long_running")]
    [InlineData("slack", "monitoring_alert", "long_running")]
    [InlineData("slack", "monitoring_alert", "deadline")]
    [InlineData("slack", "recovery", "consecutive_failures")]
    [InlineData("teams", "job_alert", "long_running")]
    [InlineData("teams", "monitoring_alert", "consecutive_failures")]
    [InlineData("teams", "recovery", "deadline")]
    public void DefaultStarterTemplate_RendersTheSameLayoutAsDelivery(string provider, string category, string kind)
    {
        var message = IntegrationEngine.Sample(new() { ApplicationName = "Orders", EnvironmentName = "Production", DashboardUrl = "https://example.com/tickerq/dashboard" }, category, kind);
        var formatter = Formatter(provider);
        var original = formatter.Format(new(), message);
        var template = BuiltInPayloads.Create(provider, message, template: true).ToJsonString();
        var rendered = formatter.Format(new() { PayloadTemplate = template }, message);
        Assert.True(JsonNode.DeepEquals(original, rendered));
        Assert.Contains("Open dashboard", original.ToJsonString());
        Assert.DoesNotContain("00000000-0000-0000-0000-000000000000", original.ToJsonString());
    }

    [Fact]
    public void Webhooks_PreserveNumericMetadata_AndUnavailableValuesAreNull()
    {
        var formatter = new WebhookFormatter();
        var message = IntegrationEngine.Sample(new(), "monitoring_alert", "long_running");
        var payload = formatter.Format(new(), message);
        Assert.Equal(30, payload["thresholdSeconds"]!.GetValue<int>());
        Assert.Equal(95, payload["observedDurationSeconds"]!.GetValue<long>());
        Assert.Null(payload["failureCount"]);
        var recovery = formatter.Format(new(), IntegrationEngine.Sample(new(), "recovery", "long_running"));
        Assert.Null(recovery["observedDurationSeconds"]);
        Assert.Equal("success", recovery["severity"]!.GetValue<string>());
        var reason = "\"},\"injected\":true <@everyone> {{function}}";
        message.Event.Reason = reason;
        var custom = PayloadTemplates.Render("""{"reason":"{{reason}}","threshold":"{{thresholdSeconds}}","missing":"{{deadlineUtc}}"}""", message);
        Assert.Equal(reason, custom["reason"]!.GetValue<string>());
        Assert.Equal("30", custom["threshold"]!.GetValue<string>());
        Assert.Equal("", custom["missing"]!.GetValue<string>());
    }

    [Fact]
    public void Defaults_DoNotInventRetryOrJobDetails_ForFunctionMonitors()
    {
        foreach (var provider in new[] { "slack", "teams" })
        {
            var alert = Formatter(provider).Format(new(), IntegrationEngine.Sample(new(), "monitoring_alert", "deadline")).ToJsonString();
            Assert.DoesNotContain("Retries", alert);
            Assert.DoesNotContain("Worker", alert);
            Assert.DoesNotContain("00000000-0000-0000-0000-000000000000", alert);
            Assert.Contains("Daily deadline missed", alert);
            Assert.DoesNotContain("Open dashboard", alert); // no URL configured
        }
    }

    [Fact]
    public void Preview_RejectsUnknownCategoriesAndConditions()
    {
        using var engine = new IntegrationEngine(new(), IntegrationTests.CreateConfiguration(new()),
            new(new IntegrationTests.ClientFactory(new IntegrationTests.StubHandler(HttpStatusCode.OK)), new()), NullLogger<IntegrationEngine>.Instance);
        Assert.Throws<ArgumentException>(() => engine.Preview(new() { MessageKind = "unknown" }));
        Assert.Throws<ArgumentException>(() => engine.Preview(new() { MonitorKind = "unknown" }));
    }
}
