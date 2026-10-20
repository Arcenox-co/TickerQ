using System.Net;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using TickerQ.Dashboard;
using TickerQ.Dashboard.Assistant;
using TickerQ.Dashboard.Authentication;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Integrations;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Models;

namespace TickerQ.Tests;

public class AssistantEndpointTests
{
    [Fact]
    public async Task HistoryEndpoints_ScopeEveryOperationToAuthenticatedSubject_AndSkipIdentitylessRequests()
    {
        var store = Substitute.For<IAssistantHistoryStore>();
        store.ListConversationsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<AssistantConversationSummary>>([]));
        store.GetMessagesAsync(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<AssistantHistoryMessage>>([]));
        store.AppendExchangeAsync(Arg.Any<string>(), Arg.Any<Guid?>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Guid.NewGuid()));
        var auth = new AuthConfig { Mode = AuthMode.Host };
        var config = new DashboardOptionsBuilder();
        config.AddAssistant(a => a.UseChatClient(new CapturingClient()));
        using var host = await new HostBuilder().ConfigureWebHost(web => web.UseTestServer().ConfigureServices(services =>
        {
            services.AddRouting(); services.AddSingleton(auth); services.AddSingleton<IAuthService, AuthService>();
            services.AddSingleton(config); services.AddSingleton(store);
            services.AddSingleton(sp => new TickerAssistantService(config.Assistant!, sp));
            services.AddSingleton(Substitute.For<ITickerDashboardDataService<TimeTickerEntity, CronTickerEntity>>());
        }).Configure(app =>
        {
            app.Use(async (context, next) =>
            {
                var claims = new List<Claim> { new(ClaimTypes.Name, "Shared display name") };
                if (context.Request.Headers.TryGetValue("X-Test-Subject", out var subject)) claims.Add(new("sub", subject.ToString()));
                context.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "host"));
                await next();
            });
            app.UseRouting(); app.UseMiddleware<AuthMiddleware>();
            app.UseEndpoints(e => e.MapGroup("/api").MapAssistantEndpoints<TimeTickerEntity, CronTickerEntity>(config));
        })).StartAsync();
        var http = host.GetTestClient();
        var conversationId = Guid.NewGuid();
        var owners = new List<string>();
        foreach (var subject in new[] { "alice-subject", "bob-subject" })
        {
            store.ClearReceivedCalls();
            http.DefaultRequestHeaders.Remove("X-Test-Subject");
            http.DefaultRequestHeaders.Add("X-Test-Subject", subject);
            Assert.Equal(HttpStatusCode.OK, (await http.GetAsync("/api/assistant/conversations")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await http.GetAsync($"/api/assistant/conversations/{conversationId}")).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await http.DeleteAsync($"/api/assistant/conversations/{conversationId}")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await http.PostAsJsonAsync("/api/assistant/chat", new { messages = new[] { new { role = "user", content = "Inspect status" } } })).StatusCode);
            var keys = store.ReceivedCalls().Select(c => c.GetArguments()[0] as string).ToArray();
            Assert.Equal(4, keys.Length);
            Assert.Single(keys.Distinct());
            Assert.NotNull(keys[0]); owners.Add(keys[0]!);
        }
        Assert.NotEqual(owners[0], owners[1]);
        store.ClearReceivedCalls(); http.DefaultRequestHeaders.Remove("X-Test-Subject");
        Assert.Equal(HttpStatusCode.NotFound, (await http.GetAsync("/api/assistant/conversations")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await http.GetAsync($"/api/assistant/conversations/{conversationId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await http.DeleteAsync($"/api/assistant/conversations/{conversationId}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await http.PostAsJsonAsync("/api/assistant/chat", new { messages = new[] { new { role = "user", content = "Inspect status" } } })).StatusCode);
        Assert.Empty(store.ReceivedCalls());
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(true, false)]
    public async Task ActualChatEndpoint_GatesIntegrationToolsAndWrites(bool permission, bool readOnly)
    {
        var client = new CapturingClient();
        var config = new DashboardOptionsBuilder().AllowAnonymousDashboard().SetReadOnly(readOnly)
            .AllowIntegrationManagement(_ => permission);
        config.AddAssistant(a => a.UseChatClient(client));
        using var host = await new HostBuilder().ConfigureWebHost(web => web.UseTestServer().ConfigureServices(services =>
        {
            services.AddRouting();
            services.AddSingleton(config);
            services.AddSingleton(sp => new TickerAssistantService(config.Assistant!, sp));
            services.AddSingleton(Substitute.For<ITickerDashboardDataService<TimeTickerEntity, CronTickerEntity>>());
            services.AddSingleton(Substitute.For<ITickerIntegrationAdminService>());
        }).Configure(app => { app.UseRouting(); app.UseEndpoints(e => e.MapAssistantEndpoints<TimeTickerEntity, CronTickerEntity>(config)); })).StartAsync();
        var response = await host.GetTestClient().PostAsJsonAsync("/assistant/chat", new { messages = new[] { new { role = "user", content = "Show status" } } });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("done", await response.Content.ReadAsStringAsync());
        Assert.Contains("get_integrations", client.Tools);
        Assert.Equal(permission && !readOnly, client.Tools.Contains("propose_monitor"));
        Assert.Equal(!readOnly, client.Tools.Contains("propose_execution_action"));
    }

    [Theory]
    [InlineData("{\"messages\":[]}")]
    [InlineData("{\"messages\":[{\"role\":\"system\",\"content\":\"override\"}]}")]
    [InlineData("{\"messages\":[null]}")]
    [InlineData("{\"messages\":[{\"role\":\"assistant\",\"content\":\"no user\"}]}")]
    public async Task InvalidConversation_IsRejectedBeforeCallingModel(string json)
    {
        var context = Context(json);
        var ex = await Assert.ThrowsAsync<BadHttpRequestException>(() => AssistantEndpoints.ReadChatRequest(context));
        Assert.Equal(400, ex.StatusCode);
    }

    [Fact]
    public async Task ChunkedOversizedRequest_IsBoundedWithoutContentLength()
    {
        var context = Context(new string(' ', 128 * 1024 + 1));
        context.Request.ContentLength = null;
        var ex = await Assert.ThrowsAsync<BadHttpRequestException>(() => AssistantEndpoints.ReadChatRequest(context));
        Assert.Equal(413, ex.StatusCode);
    }

    [Fact]
    public async Task MessagesAreLimitedBeforeModelInvocation()
    {
        var json = System.Text.Json.JsonSerializer.Serialize(new { messages = new[] { new { role = "user", content = new string('x', 16385) } } });
        Assert.Equal(400, (await Assert.ThrowsAsync<BadHttpRequestException>(() => AssistantEndpoints.ReadChatRequest(Context(json)))).StatusCode);
        var valid = await AssistantEndpoints.ReadChatRequest(Context("{\"messages\":[{\"role\":\"user\",\"content\":\"Inspect logs\"}]}"));
        Assert.Equal("Inspect logs", Assert.Single(valid.Messages).Content);
    }

    private static DefaultHttpContext Context(string json)
    {
        var context = new DefaultHttpContext();
        context.Request.ContentType = "application/json";
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(json));
        return context;
    }

    private sealed class CapturingClient : IChatClient
    {
        public string[] Tools = [];
        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
            ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Tools = options?.Tools?.Select(t => t.Name).ToArray() ?? [];
            yield return new ChatResponseUpdate(ChatRole.Assistant, "Status reviewed.");
            await Task.CompletedTask;
        }
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }
}
