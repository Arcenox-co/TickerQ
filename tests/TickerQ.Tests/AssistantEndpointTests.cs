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
using TickerQ.Utilities;
using TickerQ.Dashboard;
using TickerQ.Dashboard.Assistant;
using TickerQ.Dashboard.Authentication;
using TickerQ.Utilities.DashboardDtos;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Integrations;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Models;

namespace TickerQ.Tests;

public class AssistantEndpointTests
{
    [Fact]
    public async Task InvestigationEndpoints_scope_records_to_the_application_namespace()
    {
        var id = Guid.NewGuid();
        var executionId = Guid.NewGuid();
        var persistedReviewState = AssistantInvestigationValues.New;
        var store = Substitute.For<IAssistantInvestigationStore>();
        var data = Substitute.For<ITickerDashboardDataService<TimeTickerEntity, CronTickerEntity>>();
        data.GetTimeTickerByIdAsync(executionId, Arg.Any<CancellationToken>())
            .Returns(new TimeTickerFlatDto
            {
                Id = executionId,
                FunctionName = "Invoices.Charge",
                Status = TickerStatus.Failed,
                RetryCount = 2,
                Retries = 2,
                ExceptionMessage = "Payment gateway rejected the charge.",
            });
        store.GetOverviewAsync("orders-production", Arg.Any<CancellationToken>())
            .Returns(new AssistantInvestigationOverview { Total = 1, AwaitingReview = 1 });
        store.ListAsync("orders-production", Arg.Any<AssistantInvestigationQuery>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var query = call.ArgAt<AssistantInvestigationQuery>(1);
                return new AssistantInvestigationPage
                {
                    Items = query.State == AssistantInvestigationValues.Completed
                        ? [new AssistantInvestigationRecord
                        {
                            Id = id,
                            ApplicationNamespace = "orders-production",
                            ExecutionId = executionId,
                            FunctionName = "Invoices.Charge",
                            ExecutionStatus = "Failed",
                            Classification = "Application error",
                            Summary = "The payment gateway rejected the charge after all retries.",
                            State = AssistantInvestigationValues.Completed,
                            ReviewState = AssistantInvestigationValues.New,
                        }]
                        : [],
                    TotalCount = 1,
                    PageNumber = 1,
                    PageSize = query.PageSize,
                };
            });
        store.GetAsync("orders-production", id, Arg.Any<CancellationToken>())
            .Returns(_ => new AssistantInvestigationRecord
            {
                Id = id,
                ApplicationNamespace = "orders-production",
                ExecutionId = executionId,
                FunctionName = "Invoices.Charge",
                ExecutionStatus = "Failed",
                Classification = "Application error",
                Summary = "The payment gateway rejected the charge after all retries.",
                State = AssistantInvestigationValues.Completed,
                ReviewState = persistedReviewState,
            });
        store.SetReviewStateAsync("orders-production", id, AssistantInvestigationValues.Reviewed,
                Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                persistedReviewState = AssistantInvestigationValues.Reviewed;
                return true;
            });
        store.DeleteAsync("orders-production", id, Arg.Any<CancellationToken>()).Returns(true);
        store.DeleteByFunctionAsync("orders-production", "Invoices.Charge", Arg.Any<CancellationToken>()).Returns(2);

        var client = new CapturingClient();
        var config = new DashboardOptionsBuilder().AllowAnonymousDashboard();
        config.AddAssistant(a => a.UseChatClient(client));
        var scheduler = new SchedulerOptionsBuilder();
        scheduler.BindRuntimeActivationScope("orders-production", 1, schedulerEnabled: true);
        using var host = await new HostBuilder().ConfigureWebHost(web => web.UseTestServer().ConfigureServices(services =>
        {
            services.AddRouting();
            services.AddSingleton(config);
            services.AddSingleton(scheduler);
            services.AddSingleton(store);
            services.AddSingleton(data);
            services.AddSingleton(sp => new TickerAssistantService(config.Assistant!, sp));
        }).Configure(app =>
        {
            app.UseRouting();
            app.UseEndpoints(e => e.MapAssistantEndpoints<TimeTickerEntity, CronTickerEntity>(config));
        })).StartAsync();
        var http = host.GetTestClient();

        Assert.Equal(HttpStatusCode.OK, (await http.GetAsync("/assistant/overview")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await http.GetAsync("/assistant/investigations?reviewState=New&groupByFunction=true&page=1")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await http.GetAsync($"/assistant/investigations/{id}")).StatusCode);
        var reviewResponse = await http.PatchAsJsonAsync(
            $"/assistant/investigations/{id}/review", new { reviewState = "Reviewed" });
        Assert.Equal(HttpStatusCode.OK, reviewResponse.StatusCode);
        var reviewed = await reviewResponse.Content.ReadFromJsonAsync<AssistantInvestigationRecord>();
        Assert.Equal(AssistantInvestigationValues.Reviewed, reviewed?.ReviewState);
        Assert.Equal(HttpStatusCode.NoContent,
            (await http.DeleteAsync($"/assistant/investigations/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent,
            (await http.DeleteAsync("/assistant/investigations?functionName=Invoices.Charge")).StatusCode);

        var general = await http.PostAsJsonAsync("/assistant/chat", new
        {
            messages = new[] { new { role = "user", content = "Why are my jobs failing?" } },
        });
        Assert.Equal(HttpStatusCode.OK, general.StatusCode);
        var generalBody = await general.Content.ReadAsStringAsync();
        Assert.Contains("\"type\":\"investigations\"", generalBody, StringComparison.Ordinal);
        Assert.Contains(id.ToString(), generalBody, StringComparison.Ordinal);
        Assert.DoesNotContain(client.Messages, message =>
            message.Contains(executionId.ToString(), StringComparison.Ordinal));
        Assert.DoesNotContain(client.Messages, message =>
            message.Contains("payment gateway rejected", StringComparison.Ordinal));

        var continued = await http.PostAsJsonAsync("/assistant/chat", new
        {
            investigationId = id,
            messages = new[] { new { role = "user", content = "What should I do next?" } },
        });
        Assert.Equal(HttpStatusCode.OK, continued.StatusCode);
        Assert.Contains(client.Messages, message => message.Contains(executionId.ToString(), StringComparison.Ordinal));
        Assert.Contains(client.Messages, message => message.Contains("payment gateway rejected", StringComparison.Ordinal));
        Assert.Contains(client.Messages, message => message.Contains("fresh-investigation-evidence", StringComparison.Ordinal));
        Assert.Contains(client.Messages, message => message.Contains("retries=2/2", StringComparison.Ordinal));

        var recheck = await http.PostAsJsonAsync("/assistant/chat", new
        {
            investigationId = id,
            messages = new[]
            {
                new { role = "user", content = "Give me more context." },
                new { role = "assistant", content = "Here is the additional execution evidence." },
                new { role = "user", content = "Re-check." },
            },
        });
        Assert.Equal(HttpStatusCode.OK, recheck.StatusCode);
        Assert.Contains(client.Messages, message =>
            message.Contains("No changes detected since the previous check", StringComparison.Ordinal));
        Assert.Contains(client.Messages, message =>
            message.Contains("describe only the change", StringComparison.Ordinal));
        await data.Received(2).GetTimeTickerByIdAsync(executionId, Arg.Any<CancellationToken>());
        await store.DidNotReceive().TryStartAsync(Arg.Any<AssistantInvestigationRecord>(), Arg.Any<CancellationToken>());

        var missing = await http.PostAsJsonAsync("/assistant/chat", new
        {
            investigationId = Guid.NewGuid(),
            messages = new[] { new { role = "user", content = "Continue" } },
        });
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);

        await store.Received(1).GetOverviewAsync("orders-production", Arg.Any<CancellationToken>());
        await store.Received(1).ListAsync("orders-production",
            Arg.Is<AssistantInvestigationQuery>(query =>
                query.ReviewState == AssistantInvestigationValues.New && query.GroupByFunction),
            Arg.Any<CancellationToken>());
        await store.Received(1).ListAsync("orders-production",
            Arg.Is<AssistantInvestigationQuery>(query =>
                query.State == AssistantInvestigationValues.Completed && query.PageSize == 3),
            Arg.Any<CancellationToken>());
        await store.Received(1).SetReviewStateAsync("orders-production", id,
            AssistantInvestigationValues.Reviewed, Arg.Any<string>(), Arg.Any<CancellationToken>());
        await store.Received(1).DeleteAsync("orders-production", id, Arg.Any<CancellationToken>());
        await store.Received(1).DeleteByFunctionAsync("orders-production", "Invoices.Charge",
            Arg.Any<CancellationToken>());
        await store.DidNotReceive().GetOverviewAsync("Legacy global", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Investigation_deletion_is_blocked_in_read_only_mode()
    {
        var store = Substitute.For<IAssistantInvestigationStore>();
        var config = new DashboardOptionsBuilder().AllowAnonymousDashboard().SetReadOnly();
        config.AddAssistant(options => options.UseChatClient(new CapturingClient()));
        using var host = await new HostBuilder().ConfigureWebHost(web => web.UseTestServer().ConfigureServices(services =>
        {
            services.AddRouting();
            services.AddSingleton(config);
            services.AddSingleton(store);
            services.AddSingleton(new SchedulerOptionsBuilder());
            services.AddSingleton(sp => new TickerAssistantService(config.Assistant!, sp));
            services.AddSingleton(Substitute.For<ITickerDashboardDataService<TimeTickerEntity, CronTickerEntity>>());
        }).Configure(app =>
        {
            app.UseRouting();
            app.UseEndpoints(e => e.MapAssistantEndpoints<TimeTickerEntity, CronTickerEntity>(config));
        })).StartAsync();

        var id = Guid.NewGuid();
        var http = host.GetTestClient();
        Assert.Equal(HttpStatusCode.Forbidden,
            (await http.DeleteAsync($"/assistant/investigations/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await http.DeleteAsync("/assistant/investigations?functionName=Invoices.Charge")).StatusCode);
        await store.DidNotReceive().DeleteAsync(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await store.DidNotReceive().DeleteByFunctionAsync(Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

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

    [Fact]
    public async Task ProposalStateEndpoint_ScopesTheUpdateToAuthenticatedSubject()
    {
        var proposalId = Guid.NewGuid();
        var store = Substitute.For<IAssistantProposalHistoryStore>();
        store.SetProposalStateAsync(Arg.Any<string>(), proposalId, "applied",
                Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(true);
        var auth = new AuthConfig { Mode = AuthMode.Host };
        var config = new DashboardOptionsBuilder();
        config.AddAssistant(options => options.UseChatClient(new CapturingClient()));
        using var host = await new HostBuilder().ConfigureWebHost(web => web.UseTestServer()
            .ConfigureServices(services =>
            {
                services.AddRouting();
                services.AddSingleton(auth);
                services.AddSingleton<IAuthService, AuthService>();
                services.AddSingleton(config);
                services.AddSingleton(store);
            })
            .Configure(app =>
            {
                app.Use(async (context, next) =>
                {
                    if (context.Request.Headers.TryGetValue("X-Test-Subject", out var subject))
                        context.User = new ClaimsPrincipal(new ClaimsIdentity(
                            [new Claim("sub", subject.ToString())], "host"));
                    await next();
                });
                app.UseRouting();
                app.UseMiddleware<AuthMiddleware>();
                app.UseEndpoints(endpoints => endpoints.MapGroup("/api")
                    .MapAssistantEndpoints<TimeTickerEntity, CronTickerEntity>(config));
            })).StartAsync();

        var http = host.GetTestClient();
        http.DefaultRequestHeaders.Add("X-Test-Subject", "alice-subject");
        var response = await http.PatchAsJsonAsync($"/api/assistant/proposals/{proposalId}", new
        {
            status = "applied",
            resultJson = "{\"message\":\"Job draft saved\"}",
        });
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        await store.Received(1).SetProposalStateAsync(
            Arg.Is<string>(owner => !string.IsNullOrWhiteSpace(owner)), proposalId, "applied",
            "{\"message\":\"Job draft saved\"}", Arg.Any<CancellationToken>());

        http.DefaultRequestHeaders.Remove("X-Test-Subject");
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await http.PatchAsJsonAsync($"/api/assistant/proposals/{proposalId}",
                new { status = "dismissed", resultJson = "" })).StatusCode);
        await store.Received(1).SetProposalStateAsync(Arg.Any<string>(), proposalId,
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
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

    [Theory]
    [InlineData("Please create me a job", "", true)]
    [InlineData("Schedule another ticker", "", true)]
    [InlineData("another one", "Previous TickerQ proposal card: Demo.Progress", true)]
    [InlineData("same again", "I prepared a draft for Demo.Progress", true)]
    [InlineData("another one", "The scheduler is healthy.", false)]
    [InlineData("Which jobs failed?", "", false)]
    public void Proposal_requests_and_contextual_followups_are_recognized(
        string prompt, string priorAssistant, bool expected)
    {
        var messages = new List<ChatTurn>();
        if (!string.IsNullOrEmpty(priorAssistant))
            messages.Add(new ChatTurn { Role = "assistant", Content = priorAssistant });
        messages.Add(new ChatTurn { Role = "user", Content = prompt });

        Assert.Equal(expected, AssistantEndpoints.ShouldReinforceProposalTool(messages));
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
        public string[] Messages = [];
        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
            ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Messages = messages.Select(message => message.Text ?? "").ToArray();
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
