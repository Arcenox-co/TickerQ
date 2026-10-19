using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using TickerQ.DependencyInjection;
using TickerQ.Dashboard.DependencyInjection;
using TickerQ.Integrations.DependencyInjection;
using TickerQ.Utilities.Entities;
using TickerQ.Integrations.Infrastructure;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Models;
using TickerQ.Utilities.Integrations;

namespace TickerQ.Integrations.Tests;

public class IntegrationTests
{
    [Fact]
    public void Templates_EscapeEventData_AndPreserveJsonTypes()
    {
        var message = IntegrationEngine.Sample(new());
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
        => Assert.Throws<ArgumentException>(() => PayloadTemplates.Render(template, IntegrationEngine.Sample(new())));

    [Theory]
    [InlineData("Payments.Capture", true)]
    [InlineData("Payments.TestCapture", false)]
    [InlineData("Orders.Capture", false)]
    [InlineData("payments.Capture", false)]
    public void Rules_IncludePatterns_ExclusionsWin(string function, bool matches)
    {
        var rule = new IntegrationRule { IncludeFunctions = ["Payments.*"], ExcludeFunctions = ["*.Test*"], Events = ["failed"] };
        Assert.Equal(matches, IntegrationConfigurationService.Matches(rule, new() { Function = function, Kind = "failed" }));
        Assert.False(IntegrationConfigurationService.Matches(rule, new() { Function = function, Kind = "timeout_cancelled" }));
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
        var redacted = IntegrationConfigurationService.Redact(await service.LoadAsync(default));
        Assert.Null(redacted.Destinations[0].Url); Assert.Null(redacted.Destinations[0].Headers);
        Assert.True(redacted.Destinations[0].HasHeaders);
        redacted.Destinations[0].Name = "Renamed";
        await service.SaveAsync(redacted, default);
        var saved = await service.LoadAsync(default);
        Assert.Equal("https://example.com/secret-path", saved.Destinations[0].Url);
        Assert.Equal("secret-header", saved.Destinations[0].Headers!["X-Token"]);
        await Assert.ThrowsAsync<IntegrationConfigurationConflictException>(() => service.SaveAsync(redacted, default));
        saved.Destinations[0].Headers = [];
        await service.SaveAsync(saved, default);
        Assert.Empty((await service.LoadAsync(default)).Destinations[0].Headers!);
    }

    [Fact]
    public async Task Rename_ReprotectsPreviouslyEncryptedDestinationsAndRules()
    {
        const string storedJson = """
            {"destinations":[{"id":"c68c0085-3af9-4945-833f-4a89fc40ab1d","name":"Engineering","provider":"slack","enabled":true,"url":"https://example.com/saved-hook","headers":{"X-Token":"saved-token"}}],"rules":[{"id":"af77e985-287d-4d4b-a5ec-8187024a3b31","name":"Failures","enabled":true,"destinationIds":["c68c0085-3af9-4945-833f-4a89fc40ab1d"],"events":["failed"],"includeFunctions":["Orders.*"],"excludeFunctions":[],"cooldownSeconds":60}]}
            """;
        var protection = new EphemeralDataProtectionProvider();
        var originalVersion = Guid.NewGuid();
        var store = new MemorySettings
        {
            Record = new(originalVersion,
                protection.CreateProtector("TickerQ.Notifications.Configuration.v1").Protect(storedJson))
        };
        var service = new IntegrationConfigurationService(store, protection, new(), [new SlackFormatter()]);

        var loaded = await service.LoadAsync(default);
        Assert.NotEqual(originalVersion, loaded.Version);
        Assert.Equal(storedJson, protection.CreateProtector("TickerQ.Integrations.Configuration.v1").Unprotect(store.Record!.ProtectedConfiguration));
        var migrated = store.Record;
        await service.LoadAsync(default);
        Assert.Equal(migrated, store.Record);
        Assert.Equal("https://example.com/saved-hook", loaded.Destinations[0].Url);
        Assert.Equal("saved-token", loaded.Destinations[0].Headers!["X-Token"]);
        Assert.Equal("Orders.*", Assert.Single(loaded.Rules[0].IncludeFunctions));

        var edit = IntegrationConfigurationService.Redact(loaded);
        edit.Destinations[0].Name = "Renamed integration";
        await service.SaveAsync(edit, default);

        var saved = await service.LoadAsync(default);
        Assert.NotEqual(originalVersion, saved.Version);
        Assert.Equal("Renamed integration", saved.Destinations[0].Name);
        Assert.Equal("https://example.com/saved-hook", saved.Destinations[0].Url);
        Assert.Equal("saved-token", saved.Destinations[0].Headers!["X-Token"]);
        Assert.Single(saved.Rules);
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
        using var engine = new IntegrationEngine(new(), configService, new(new ClientFactory(handler), new()), NullLogger<IntegrationEngine>.Instance);
        var message = IntegrationEngine.Sample(new());
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
            var transport = new IntegrationTransport(new ClientFactory(handler), new());
            var result = await transport.SendAsync(ValidConfiguration().Destinations[0], "{}", default);
            Assert.Equal("Failed", result.Outcome); Assert.Equal(1, handler.Calls);
        }
        var retryHandler = new StubHandler(HttpStatusCode.TooManyRequests, HttpStatusCode.OK);
        var retried = await new IntegrationTransport(new ClientFactory(retryHandler), new()).SendAsync(ValidConfiguration().Destinations[0], "{}", default);
        Assert.Equal("Sent", retried.Outcome); Assert.Equal(2, retryHandler.Calls);
    }

    [Fact]
    public async Task NativeProviders_GenerateDifferentPayloads_AndAreExtensible()
    {
        var message = IntegrationEngine.Sample(new() { DashboardUrl = "https://example.com/tickerq/dashboard" });
        Assert.NotNull(new SlackFormatter().Format(new(), message)["blocks"]);
        Assert.Equal("application/vnd.microsoft.card.adaptive", new TeamsFormatter().Format(new(), message)["attachments"]![0]!["contentType"]!.GetValue<string>());
        var configService = new IntegrationConfigurationService(new MemorySettings(), new EphemeralDataProtectionProvider(), new(), [new CustomFormatter()]);
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
            options.UseApplicationNamespace("integration-tests");
            void AddIntegrations() => Assert.Same(options, options.AddIntegrations(integrations =>
            {
                integrations.ApplicationName = "Orders";
                integrations.EnvironmentName = "Production";
                integrations.DashboardUrl = "https://example.com/tickerq/dashboard";
            }));
            if (first) AddIntegrations();
            options.AddDashboard(dashboard => dashboard.AllowAnonymousDashboard());
            if (!first) AddIntegrations();
        });
        using var provider = services.BuildServiceProvider();
        Assert.IsType<IntegrationEngine>(provider.GetRequiredService<ITickerQFailureNotifier>());
        Assert.Same(provider.GetRequiredService<ITickerQFailureNotifier>(), provider.GetRequiredService<ITickerIntegrationAdminService>());
        Assert.Same(provider.GetRequiredService<ITickerPersistenceProvider<TimeTickerEntity, CronTickerEntity>>(),
            provider.GetRequiredService<ITickerIntegrationSettingsStore>());
        var configured = provider.GetRequiredService<IntegrationOptions>();
        Assert.Equal("Orders", configured.ApplicationName);
        Assert.Equal("Production", configured.EnvironmentName);
        Assert.Equal("https://example.com/tickerq/dashboard", configured.DashboardUrl);
    }

    [Fact]
    public void Registration_SupportsDefaultsAndCustomTickerEntities()
    {
        var services = new ServiceCollection();
        services.AddTickerQ<CustomTimeTicker, CustomCronTicker>(options =>
            options.UseApplicationNamespace("integration-custom-tests").AddIntegrations());
        using var provider = services.BuildServiceProvider();
        Assert.Equal("TickerQ", provider.GetRequiredService<IntegrationOptions>().ApplicationName);
        Assert.IsType<IntegrationEngine>(provider.GetRequiredService<ITickerQFailureNotifier>());
        Assert.Same(provider.GetRequiredService<ITickerPersistenceProvider<CustomTimeTicker, CustomCronTicker>>(),
            provider.GetRequiredService<ITickerIntegrationSettingsStore>());
    }

    [Fact]
    public void Registration_StaysOptional()
    {
        var services = new ServiceCollection();
        services.AddTickerQ(options => options.UseApplicationNamespace("integration-disabled-tests"));
        using var provider = services.BuildServiceProvider();
        Assert.Null(provider.GetService<IntegrationOptions>());
        Assert.Null(provider.GetService<ITickerIntegrationAdminService>());
    }

    [Fact]
    public void Registration_RejectsDuplicateBuilderCalls()
    {
        var services = new ServiceCollection();
        var exception = Assert.Throws<InvalidOperationException>(() => services.AddTickerQ(options =>
            options.UseApplicationNamespace("integration-duplicate-tests").AddIntegrations().AddIntegrations()));
        Assert.Contains("already registered", exception.Message);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Registration_RejectsMixingLegacyAndDynamicIntegrations(bool first)
    {
        var services = new ServiceCollection();
        var exception = Assert.Throws<InvalidOperationException>(() => services.AddTickerQ(options =>
        {
            options.UseApplicationNamespace("integration-tests");
            if (first) options.AddIntegrations();
            options.NotifyFailuresViaWebhook("https://example.com/legacy");
            if (!first) options.AddIntegrations();
        }));
        Assert.Contains("Use either AddIntegrations or NotifyFailuresViaWebhook", exception.Message);
    }

    public sealed class CustomTimeTicker : TimeTickerEntity<CustomTimeTicker>;
    public sealed class CustomCronTicker : CronTickerEntity;

    [Fact]
    public async Task InMemoryStore_IsolatesNamespaces_AndSharesSettingsWithinNamespace()
    {
        static ServiceProvider Build(string name)
        {
            var services = new ServiceCollection();
            services.AddTickerQ(o => o.UseApplicationNamespace(name));
            return services.BuildServiceProvider();
        }
        var name = "integrations-" + Guid.NewGuid();
        using var first = Build(name); using var same = Build(name); using var other = Build(name + "-other");
        var store = first.GetRequiredService<ITickerIntegrationSettingsStore>();
        Assert.False(store.IntegrationSettingsArePersistent);
        var row = new IntegrationSettingsRecord(Guid.NewGuid(), "encrypted-settings");
        Assert.True(await store.SaveIntegrationSettingsAsync(row, Guid.Empty));
        Assert.Equal(row, await same.GetRequiredService<ITickerIntegrationSettingsStore>().ReadIntegrationSettingsAsync());
        Assert.Null(await other.GetRequiredService<ITickerIntegrationSettingsStore>().ReadIntegrationSettingsAsync());
        Assert.False(await store.SaveIntegrationSettingsAsync(row, Guid.Empty));
        Assert.True(store.SupportsIntegrationMonitoring);
        Assert.True(await store.SaveIntegrationMonitoringAsync(row, Guid.Empty));
        Assert.Equal(row, await same.GetRequiredService<ITickerIntegrationSettingsStore>().ReadIntegrationMonitoringAsync());
        Assert.Null(await other.GetRequiredService<ITickerIntegrationSettingsStore>().ReadIntegrationMonitoringAsync());
        Assert.False(await store.SaveIntegrationMonitoringAsync(row, Guid.Empty));
    }

    [Fact]
    public async Task ChangingProvider_DoesNotReuseSavedCredentials()
    {
        var service = CreateConfiguration(new());
        await service.SaveAsync(ValidConfiguration(), default);
        var edit = IntegrationConfigurationService.Redact(await service.LoadAsync(default));
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
        var options = new IntegrationOptions { QueueCapacity = 1, ReasonSanitizer = _ => "safe reason" };
        using var engine = new IntegrationEngine(options, configService, new(new ClientFactory(handler), options), NullLogger<IntegrationEngine>.Instance);
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
        var options = new IntegrationOptions { ReasonSanitizer = _ => throws ? throw new InvalidOperationException("sensitive value") : null! };
        using var engine = new IntegrationEngine(options, configuration, new(new ClientFactory(handler), options), NullLogger<IntegrationEngine>.Instance);
        engine.Notify(new() { Kind = "failed", Function = "Example", Reason = "sensitive value" });
        await engine.StartAsync(default);
        await handler.Received.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await engine.StopAsync(default);
        Assert.DoesNotContain("sensitive value", handler.Body);
        using var body = JsonDocument.Parse(handler.Body);
        Assert.Equal(throws ? "Failure reason unavailable: configured redaction failed." : "", body.RootElement.GetProperty("reason").GetString());
    }

    internal static IntegrationConfiguration ValidConfiguration()
    {
        var destination = new IntegrationDestination { Name = "Engineering", Url = "https://example.com/secret-path", Headers = new() { ["X-Token"] = "secret-header" } };
        return new() { Destinations = [destination], Rules = [new() { Name = "Failures", DestinationIds = [destination.Id] }] };
    }
    internal static IntegrationConfigurationService CreateConfiguration(MemorySettings store) => new(store, new EphemeralDataProtectionProvider(), new(), [new WebhookFormatter(), new SlackFormatter(), new TeamsFormatter()]);
    internal sealed class MemorySettings : ITickerIntegrationSettingsStore
    {
        public IntegrationSettingsRecord? Record;
        public Task<IntegrationSettingsRecord?> ReadIntegrationSettingsAsync(CancellationToken cancellationToken = default) { lock (this) return Task.FromResult(Record); }
        public Task<bool> SaveIntegrationSettingsAsync(IntegrationSettingsRecord record, Guid expectedVersion, CancellationToken cancellationToken = default)
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
    private sealed class CustomFormatter : IIntegrationPayloadFormatter
    {
        public string Name => "custom-app";
        public System.Text.Json.Nodes.JsonObject Format(IntegrationDestination destination, IntegrationMessage message) => new() { ["event"] = message.Event.Kind };
    }
}
