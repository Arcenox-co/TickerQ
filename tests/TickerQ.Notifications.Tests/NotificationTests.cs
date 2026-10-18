using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using TickerQ.DependencyInjection;
using TickerQ.Dashboard.DependencyInjection;
using TickerQ.Notifications.DependencyInjection;
using TickerQ.Utilities.Entities;
using TickerQ.Notifications.Infrastructure;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Models;
using TickerQ.Utilities.Notifications;

namespace TickerQ.Notifications.Tests;

public class NotificationTests
{
    [Fact]
    public void Templates_EscapeEventData_AndPreserveJsonTypes()
    {
        var message = NotificationEngine.Sample(new());
        message.Event.Reason = "\"},\"injected\":true\n{{function}}";
        var result = PayloadTemplates.Render("""{"reason":"{{reason}}","nested":["{{function}}"],"number":12,"enabled":true}""", message);
        using var json = JsonDocument.Parse(result.ToJsonString());
        Assert.Equal(message.Event.Reason, json.RootElement.GetProperty("reason").GetString());
        Assert.False(json.RootElement.TryGetProperty("injected", out _));
        Assert.Equal(12, json.RootElement.GetProperty("number").GetInt32());
        Assert.True(json.RootElement.GetProperty("enabled").GetBoolean());
    }

    [Theory]
    [InlineData("{\"x\":\"{{unknown}}\"}")]
    [InlineData("{\"{{function}}\":\"value\"}")]
    [InlineData("{\"x\":\"{{function\"}")]
    [InlineData("[]")]
    [InlineData("not json")]
    public void Templates_RejectUnsupportedExpressions(string template)
        => Assert.Throws<ArgumentException>(() => PayloadTemplates.Render(template, NotificationEngine.Sample(new())));

    [Theory]
    [InlineData("Payments.Capture", true)]
    [InlineData("Payments.TestCapture", false)]
    [InlineData("Orders.Capture", false)]
    [InlineData("payments.Capture", false)]
    public void Rules_IncludePatterns_ExclusionsWin(string function, bool matches)
    {
        var rule = new NotificationRule { IncludeFunctions = ["Payments.*"], ExcludeFunctions = ["*.Test*"], Events = ["failed"] };
        Assert.Equal(matches, NotificationConfigurationService.Matches(rule, new() { Function = function, Kind = "failed" }));
        Assert.False(NotificationConfigurationService.Matches(rule, new() { Function = function, Kind = "timeout_cancelled" }));
    }

    [Fact]
    public async Task Configuration_EncryptsSecrets_PreservesRedactedEdits_RejectsStaleSaves()
    {
        var store = new MemorySettings();
        var service = CreateConfiguration(store);
        var config = ValidConfiguration();
        await service.SaveAsync(config, default);
        Assert.DoesNotContain("secret-path", store.Record!.ProtectedConfiguration);
        Assert.DoesNotContain("secret-header", store.Record.ProtectedConfiguration);
        var redacted = NotificationConfigurationService.Redact(await service.LoadAsync(default));
        Assert.Null(redacted.Destinations[0].Url); Assert.Null(redacted.Destinations[0].Headers);
        Assert.True(redacted.Destinations[0].HasHeaders);
        redacted.Destinations[0].Name = "Renamed";
        await service.SaveAsync(redacted, default);
        var saved = await service.LoadAsync(default);
        Assert.Equal("https://example.com/secret-path", saved.Destinations[0].Url);
        Assert.Equal("secret-header", saved.Destinations[0].Headers!["X-Token"]);
        await Assert.ThrowsAsync<NotificationConfigurationConflictException>(() => service.SaveAsync(redacted, default));
        saved.Destinations[0].Headers = [];
        await service.SaveAsync(saved, default);
        Assert.Empty((await service.LoadAsync(default)).Destinations[0].Headers!);
    }

    [Theory]
    [InlineData("http://example.com/hook")]
    [InlineData("https://127.0.0.1/hook")]
    [InlineData("https://169.254.169.254/hook")]
    [InlineData("https://user:pass@example.com/hook")]
    public async Task Configuration_RejectsUnsafeUrls(string url)
    {
        var config = ValidConfiguration(); config.Destinations[0].Url = url;
        await Assert.ThrowsAsync<ArgumentException>(() => CreateConfiguration(new()).SaveAsync(config, default));
    }

    [Theory]
    [InlineData("127.0.0.1", false)]
    [InlineData("::ffff:127.0.0.1", false)]
    [InlineData("10.0.1.2", false)]
    [InlineData("172.20.0.1", false)]
    [InlineData("192.168.1.1", false)]
    [InlineData("100.64.0.1", false)]
    [InlineData("fc00::1", false)]
    [InlineData("fe80::1", false)]
    [InlineData("2002:7f00:0001::", false)]
    [InlineData("8.8.8.8", true)]
    [InlineData("2606:4700:4700::1111", true)]
    public void NetworkGuard_RejectsPrivateAndTransitionAddresses(string ip, bool allowed)
        => Assert.Equal(allowed, WebhookSecurity.IsPublic(IPAddress.Parse(ip)));

    [Fact]
    public async Task Dispatch_OverlappingRulesSendOnce_RespectCooldownAndDynamicDisable()
    {
        var store = new MemorySettings(); var configService = CreateConfiguration(store); var config = ValidConfiguration();
        config.Rules.Add(new() { Name = "Overlapping rule", DestinationIds = [config.Destinations[0].Id] });
        await configService.SaveAsync(config, default);
        var handler = new StubHandler(HttpStatusCode.OK);
        using var engine = new NotificationEngine(new(), configService, new(new ClientFactory(handler), new()), NullLogger<NotificationEngine>.Instance);
        var message = NotificationEngine.Sample(new());
        await engine.DispatchAsync(message, default);
        await engine.DispatchAsync(message, default);
        Assert.Equal(1, handler.Calls);
        Assert.Equal(1, (await engine.GetAsync()).Suppressed);
        var current = await configService.LoadAsync(default); current.Destinations[0].Enabled = false;
        await configService.SaveAsync(current, default);
        await engine.DispatchAsync(message, default);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task Transport_RetriesTransientErrors_ButNotBadRequestsOrRedirects()
    {
        foreach (var code in new[] { HttpStatusCode.BadRequest, HttpStatusCode.Found })
        {
            var handler = new StubHandler(code);
            var transport = new NotificationTransport(new ClientFactory(handler), new());
            var result = await transport.SendAsync(ValidConfiguration().Destinations[0], "{}", default);
            Assert.Equal("Failed", result.Outcome); Assert.Equal(1, handler.Calls);
        }
        var retryHandler = new StubHandler(HttpStatusCode.TooManyRequests, HttpStatusCode.OK);
        var retried = await new NotificationTransport(new ClientFactory(retryHandler), new()).SendAsync(ValidConfiguration().Destinations[0], "{}", default);
        Assert.Equal("Sent", retried.Outcome); Assert.Equal(2, retryHandler.Calls);
    }

    [Fact]
    public async Task NativeProviders_GenerateDifferentPayloads_AndAreExtensible()
    {
        var message = NotificationEngine.Sample(new() { DashboardUrl = "https://example.com/tickerq/dashboard" });
        Assert.NotNull(new SlackFormatter().Format(new(), message)["blocks"]);
        Assert.Equal("application/vnd.microsoft.card.adaptive", new TeamsFormatter().Format(new(), message)["attachments"]![0]!["contentType"]!.GetValue<string>());
        var configService = new NotificationConfigurationService(new MemorySettings(), new EphemeralDataProtectionProvider(), new(), [new CustomFormatter()]);
        var config = ValidConfiguration(); config.Destinations[0].Provider = "custom-app";
        await configService.SaveAsync(config, default);
        Assert.Contains("custom-app", configService.Formatters.Keys);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Registration_WorksInsideTickerQ_BeforeOrAfterDashboard(bool first)
    {
        var services = new ServiceCollection();
        services.AddTickerQ(options =>
        {
            options.UseDefinedCronApplicationNamespace("notification-tests");
            void AddNotifications() => Assert.Same(options, options.AddNotifications(notifications =>
            {
                notifications.ApplicationName = "Orders";
                notifications.EnvironmentName = "Production";
                notifications.DashboardUrl = "https://example.com/tickerq/dashboard";
            }));
            if (first) AddNotifications();
            options.AddDashboard(dashboard => dashboard.AllowAnonymousDashboard());
            if (!first) AddNotifications();
        });
        using var provider = services.BuildServiceProvider();
        Assert.IsType<NotificationEngine>(provider.GetRequiredService<ITickerQFailureNotifier>());
        Assert.Same(provider.GetRequiredService<ITickerQFailureNotifier>(), provider.GetRequiredService<ITickerNotificationAdminService>());
        Assert.Same(provider.GetRequiredService<ITickerPersistenceProvider<TimeTickerEntity, CronTickerEntity>>(),
            provider.GetRequiredService<ITickerNotificationSettingsStore>());
        var configured = provider.GetRequiredService<NotificationOptions>();
        Assert.Equal("Orders", configured.ApplicationName);
        Assert.Equal("Production", configured.EnvironmentName);
        Assert.Equal("https://example.com/tickerq/dashboard", configured.DashboardUrl);
    }

    [Fact]
    public void Registration_SupportsDefaultsAndCustomTickerEntities()
    {
        var services = new ServiceCollection();
        services.AddTickerQ<CustomTimeTicker, CustomCronTicker>(options =>
            options.UseDefinedCronApplicationNamespace("notification-custom-tests").AddNotifications());
        using var provider = services.BuildServiceProvider();
        Assert.Equal("TickerQ", provider.GetRequiredService<NotificationOptions>().ApplicationName);
        Assert.IsType<NotificationEngine>(provider.GetRequiredService<ITickerQFailureNotifier>());
        Assert.Same(provider.GetRequiredService<ITickerPersistenceProvider<CustomTimeTicker, CustomCronTicker>>(),
            provider.GetRequiredService<ITickerNotificationSettingsStore>());
    }

    [Fact]
    public void Registration_StaysOptional()
    {
        var services = new ServiceCollection();
        services.AddTickerQ(options => options.UseDefinedCronApplicationNamespace("notification-disabled-tests"));
        using var provider = services.BuildServiceProvider();
        Assert.Null(provider.GetService<NotificationOptions>());
        Assert.Null(provider.GetService<ITickerNotificationAdminService>());
    }

    [Fact]
    public void Registration_RejectsDuplicateBuilderCalls()
    {
        var services = new ServiceCollection();
        var exception = Assert.Throws<InvalidOperationException>(() => services.AddTickerQ(options =>
            options.UseDefinedCronApplicationNamespace("notification-duplicate-tests").AddNotifications().AddNotifications()));
        Assert.Contains("already registered", exception.Message);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Registration_RejectsMixingLegacyAndDynamicNotifications(bool first)
    {
        var services = new ServiceCollection();
        var exception = Assert.Throws<InvalidOperationException>(() => services.AddTickerQ(options =>
        {
            options.UseDefinedCronApplicationNamespace("notification-tests");
            if (first) options.AddNotifications();
            options.NotifyFailuresViaWebhook("https://example.com/legacy");
            if (!first) options.AddNotifications();
        }));
        Assert.Contains("Use either AddNotifications or NotifyFailuresViaWebhook", exception.Message);
    }

    public sealed class CustomTimeTicker : TimeTickerEntity<CustomTimeTicker>;
    public sealed class CustomCronTicker : CronTickerEntity;

    [Fact]
    public async Task InMemoryStore_IsolatesNamespaces_AndSharesSettingsWithinNamespace()
    {
        static ServiceProvider Build(string name)
        {
            var services = new ServiceCollection();
            services.AddTickerQ(o => o.UseDefinedCronApplicationNamespace(name));
            return services.BuildServiceProvider();
        }
        var name = "notifications-" + Guid.NewGuid();
        using var first = Build(name); using var same = Build(name); using var other = Build(name + "-other");
        var store = first.GetRequiredService<ITickerNotificationSettingsStore>();
        Assert.False(store.NotificationSettingsArePersistent);
        var row = new NotificationSettingsRecord(Guid.NewGuid(), "encrypted-settings");
        Assert.True(await store.SaveNotificationSettingsAsync(row, Guid.Empty));
        Assert.Equal(row, await same.GetRequiredService<ITickerNotificationSettingsStore>().ReadNotificationSettingsAsync());
        Assert.Null(await other.GetRequiredService<ITickerNotificationSettingsStore>().ReadNotificationSettingsAsync());
        Assert.False(await store.SaveNotificationSettingsAsync(row, Guid.Empty));
    }

    [Fact]
    public async Task ChangingProvider_DoesNotReuseSavedCredentials()
    {
        var service = CreateConfiguration(new());
        await service.SaveAsync(ValidConfiguration(), default);
        var edit = NotificationConfigurationService.Redact(await service.LoadAsync(default));
        edit.Destinations[0].Provider = "slack";
        await Assert.ThrowsAsync<ArgumentException>(() => service.SaveAsync(edit, default));
        edit.Destinations[0].Url = "https://example.com/new-slack-hook";
        await service.SaveAsync(edit, default);
        Assert.Empty((await service.LoadAsync(default)).Destinations[0].Headers!);
    }

    [Theory]
    [InlineData("Host", "example.com")]
    [InlineData("X-Token", "abc\r\ninjected: yes")]
    [InlineData("Proxy-Authorization", "secret")]
    public async Task Configuration_RejectsDangerousHeaders(string header, string value)
    {
        var config = ValidConfiguration(); config.Destinations[0].Headers = new() { [header] = value };
        await Assert.ThrowsAsync<ArgumentException>(() => CreateConfiguration(new()).SaveAsync(config, default));
    }

    [Fact]
    public async Task Worker_EnqueuesWithoutBlocking_DropsOverflow_AndRedactsReasons()
    {
        var configService = CreateConfiguration(new());
        await configService.SaveAsync(ValidConfiguration(), default);
        var handler = new StubHandler(HttpStatusCode.OK);
        var options = new NotificationOptions { QueueCapacity = 1, ReasonSanitizer = _ => "safe reason" };
        using var engine = new NotificationEngine(options, configService, new(new ClientFactory(handler), options), NullLogger<NotificationEngine>.Instance);
        engine.Notify(new() { Kind = "failed", Function = "ProcessOrders", Reason = "sensitive value" });
        engine.Notify(new() { Kind = "failed", Function = "Overflow" });
        Assert.Equal(1, (await engine.GetAsync()).Dropped);
        await engine.StartAsync(default);
        await handler.Received.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await engine.StopAsync(default);
        Assert.Contains("safe reason", handler.Body);
        Assert.DoesNotContain("sensitive value", handler.Body);
        Assert.Equal(1, handler.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Redaction_NullOrThrow_NeverFallsBackToOriginalReason(bool throws)
    {
        var configuration = CreateConfiguration(new());
        await configuration.SaveAsync(ValidConfiguration(), default);
        var handler = new StubHandler(HttpStatusCode.OK);
        var options = new NotificationOptions { ReasonSanitizer = _ => throws ? throw new InvalidOperationException("sensitive value") : null! };
        using var engine = new NotificationEngine(options, configuration, new(new ClientFactory(handler), options), NullLogger<NotificationEngine>.Instance);
        engine.Notify(new() { Kind = "failed", Function = "Example", Reason = "sensitive value" });
        await engine.StartAsync(default);
        await handler.Received.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await engine.StopAsync(default);
        Assert.DoesNotContain("sensitive value", handler.Body);
        using var body = JsonDocument.Parse(handler.Body);
        Assert.Equal(throws ? "Failure reason unavailable: configured redaction failed." : "", body.RootElement.GetProperty("reason").GetString());
    }

    internal static NotificationConfiguration ValidConfiguration()
    {
        var destination = new NotificationDestination { Name = "Engineering", Url = "https://example.com/secret-path", Headers = new() { ["X-Token"] = "secret-header" } };
        return new() { Destinations = [destination], Rules = [new() { Name = "Failures", DestinationIds = [destination.Id] }] };
    }
    internal static NotificationConfigurationService CreateConfiguration(MemorySettings store) => new(store, new EphemeralDataProtectionProvider(), new(), [new WebhookFormatter(), new SlackFormatter(), new TeamsFormatter()]);
    internal sealed class MemorySettings : ITickerNotificationSettingsStore
    {
        public NotificationSettingsRecord? Record;
        public Task<NotificationSettingsRecord?> ReadNotificationSettingsAsync(CancellationToken cancellationToken = default) { lock (this) return Task.FromResult(Record); }
        public Task<bool> SaveNotificationSettingsAsync(NotificationSettingsRecord record, Guid expectedVersion, CancellationToken cancellationToken = default)
        {
            lock (this) { if ((Record?.Version ?? Guid.Empty) != expectedVersion) return Task.FromResult(false); Record = record; return Task.FromResult(true); }
        }
    }
    internal sealed class ClientFactory(HttpMessageHandler handler) : IHttpClientFactory { public HttpClient CreateClient(string name) => new(handler, disposeHandler: false); }
    internal sealed class StubHandler(params HttpStatusCode[] codes) : HttpMessageHandler
    {
        public int Calls;
        public string Body = "";
        public TaskCompletionSource Received = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            var response = new HttpResponseMessage(codes[Math.Min(Interlocked.Increment(ref Calls) - 1, codes.Length - 1)]);
            Received.TrySetResult();
            return response;
        }
    }
    private sealed class CustomFormatter : INotificationPayloadFormatter
    {
        public string Name => "custom-app";
        public System.Text.Json.Nodes.JsonObject Format(NotificationDestination destination, NotificationMessage message) => new() { ["event"] = message.Event.Kind };
    }
}
