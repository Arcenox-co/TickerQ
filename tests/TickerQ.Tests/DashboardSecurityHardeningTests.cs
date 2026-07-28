using System.Reflection;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using TickerQ.BackgroundServices;
using TickerQ.Dashboard.Assistant;
using TickerQ.Dashboard.Authentication;
using TickerQ.Dashboard.Authentication.Endpoints;
using TickerQ.Dashboard.Authentication.Jwt;
using TickerQ.Dashboard.Authentication.Schemes;
using TickerQ.Dashboard.DependencyInjection;
using TickerQ.Utilities.Models;
using TickerQ.Utilities;
using TickerQ.Utilities.Entities;

namespace TickerQ.Tests;

public class DashboardSecurityHardeningTests : IDisposable
{
    public DashboardSecurityHardeningTests() => LoginRateLimiter.ResetForTests();
    public void Dispose() => LoginRateLimiter.ResetForTests();

    [Fact]
    public void LoginLimiter_BlocksAccountAcrossRotatingIpBuckets()
    {
        var account = LoginRateLimiter.AccountKey("Admin");
        for (var i = 0; i < LoginRateLimiter.MaxFailures; i++)
        {
            LoginRateLimiter.RecordFailure("ip:192.0.2." + i);
            LoginRateLimiter.RecordFailure(account);
        }

        Assert.True(LoginRateLimiter.IsBlocked(account, out var retryAfter));
        Assert.True(retryAfter > TimeSpan.Zero);
        Assert.False(LoginRateLimiter.IsBlocked("ip:198.51.100.1", out _));
    }

    [Fact]
    public void LoginLimiter_RemainsHardBoundedUnderFreshKeyFlood()
    {
        for (var i = 0; i < LoginRateLimiter.Capacity + 250; i++)
            LoginRateLimiter.RecordFailure("ip:" + i);

        Assert.InRange(LoginRateLimiter.CurrentCount, 1, LoginRateLimiter.Capacity);
    }

    [Fact]
    public async Task BasicAuth_ValidatesConfiguredCredentialAndRejectsMismatch()
    {
        var expected = Convert.ToBase64String(Encoding.UTF8.GetBytes("admin:correct"));
        var scheme = new BasicAuthScheme { Credentials = expected };
        var valid = new DefaultHttpContext();
        valid.Request.Headers.Authorization = "Basic " + expected;
        var invalid = new DefaultHttpContext();
        invalid.Request.Headers.Authorization = "Basic " +
            Convert.ToBase64String(Encoding.UTF8.GetBytes("admin:wrongxx"));

        Assert.True((await scheme.TryAuthenticateAsync(valid)).IsAuthenticated);
        Assert.False((await scheme.TryAuthenticateAsync(invalid)).IsAuthenticated);
    }

    [Fact]
    public async Task BasicAuth_RejectsQueryCredentialsOutsideWebSocketHubBoundary()
    {
        var expected = Convert.ToBase64String(Encoding.UTF8.GetBytes("admin:correct"));
        var scheme = new BasicAuthScheme { Credentials = expected };
        var context = new DefaultHttpContext();
        context.Request.Path = "/tickerq/api/tickers";
        context.Request.QueryString = new QueryString("?access_token=" + expected);

        Assert.False((await scheme.TryAuthenticateAsync(context)).IsAuthenticated);
    }

    [Fact]
    public async Task BasicAuth_AcceptsQueryCredentialsOnlyOnExactWebSocketHubPath()
    {
        var expected = Convert.ToBase64String(Encoding.UTF8.GetBytes("admin:correct"));
        var scheme = new BasicAuthScheme { Credentials = expected };
        var context = WebSocketContext("/tickerq-notification-hub", expected);

        Assert.True((await scheme.TryAuthenticateAsync(context)).IsAuthenticated);
    }

    [Theory]
    [InlineData("/tickerq-notification-hub-evil")]
    [InlineData("/hubs/tickerq-notification-hub")]
    public async Task BasicAuth_RejectsQueryCredentialsOnWebSocketNearMatchPaths(string path)
    {
        var expected = Convert.ToBase64String(Encoding.UTF8.GetBytes("admin:correct"));
        var scheme = new BasicAuthScheme { Credentials = expected };

        Assert.False((await scheme.TryAuthenticateAsync(WebSocketContext(path, expected))).IsAuthenticated);
    }

    [Theory]
    [InlineData("Jwt")]
    [InlineData("ApiKey")]
    [InlineData("Custom")]
    public async Task QueryCredentialSchemes_RejectCredentialsOutsideWebSocketHubBoundary(string schemeName)
    {
        var (scheme, token) = CreateQueryCredentialScheme(schemeName);
        var context = new DefaultHttpContext();
        context.Request.Path = "/tickerq/api/tickers";
        context.Request.QueryString = new QueryString("?access_token=" + token);

        Assert.False((await scheme.TryAuthenticateAsync(context)).IsAuthenticated);
    }

    [Theory]
    [InlineData("Jwt")]
    [InlineData("ApiKey")]
    [InlineData("Custom")]
    public async Task QueryCredentialSchemes_AcceptCredentialsOnlyOnExactWebSocketHubPath(string schemeName)
    {
        var (scheme, token) = CreateQueryCredentialScheme(schemeName);

        Assert.True((await scheme.TryAuthenticateAsync(
            WebSocketContext("/tickerq-notification-hub", token))).IsAuthenticated);
    }

    [Theory]
    [InlineData("Jwt", "/tickerq-notification-hub-evil")]
    [InlineData("Jwt", "/hubs/tickerq-notification-hub")]
    [InlineData("ApiKey", "/tickerq-notification-hub-evil")]
    [InlineData("Custom", "/hubs/tickerq-notification-hub")]
    public async Task QueryCredentialSchemes_RejectCredentialsOnWebSocketNearMatchPaths(
        string schemeName, string path)
    {
        var (scheme, token) = CreateQueryCredentialScheme(schemeName);

        Assert.False((await scheme.TryAuthenticateAsync(WebSocketContext(path, token))).IsAuthenticated);
    }

    private static (IAuthScheme Scheme, string Token) CreateQueryCredentialScheme(string schemeName)
    {
        const string token = "query-secret";
        return schemeName switch
        {
            "Jwt" => CreateJwtQueryScheme(),
            "ApiKey" => (new ApiKeyAuthScheme { ApiKey = token }, token),
            "Custom" => (new CustomAuthScheme { Validator = value => value == token }, token),
            _ => throw new ArgumentOutOfRangeException(nameof(schemeName))
        };
    }

    private static (IAuthScheme Scheme, string Token) CreateJwtQueryScheme()
    {
        var issuer = new JwtTokenIssuer(
            Enumerable.Range(0, 32).Select(i => (byte)i).ToArray(),
            "issuer", "audience", TimeSpan.FromMinutes(5));
        var users = new InMemoryUserStore();
        users.AddUser("admin", "password");
        return (new JwtBearerScheme { Issuer = issuer, Users = users }, issuer.IssueAccessToken("admin"));
    }

    private static DefaultHttpContext WebSocketContext(string path, string token)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        context.Request.QueryString = new QueryString("?access_token=" + token);
        var webSocketFeature = NSubstitute.Substitute.For<IHttpWebSocketFeature>();
        webSocketFeature.IsWebSocketRequest.Returns(true);
        context.Features.Set(webSocketFeature);
        return context;
    }

    [Fact]
    public void JwtIssuer_ExplicitSharedKeyValidatesAcrossInstancesAndRestart()
    {
        var key = Enumerable.Range(0, 32).Select(i => (byte)i).ToArray();
        var first = new JwtTokenIssuer(key, "issuer", "audience", TimeSpan.FromMinutes(5));
        var restarted = new JwtTokenIssuer(key, "issuer", "audience", TimeSpan.FromMinutes(5));

        var token = first.IssueAccessToken("admin");

        Assert.True(restarted.TryValidate(token, out var username));
        Assert.Equal("admin", username);
    }

    [Fact]
    public void JwtIssuer_MissingExplicitKeyFailsUnlessEphemeralWasAcknowledged()
    {
        var config = new AuthConfig { JwtBearerOptions = new JwtBearerOptions() };
        var services = new ServiceCollection().BuildServiceProvider();
        var build = typeof(ServiceExtensions).GetMethod(
            "BuildJwtIssuer", BindingFlags.NonPublic | BindingFlags.Static)!;

        var wrapper = Assert.Throws<TargetInvocationException>(() => build.Invoke(null, [services, config]));
        var exception = Assert.IsType<InvalidOperationException>(wrapper.InnerException);
        Assert.Contains("explicit shared JWT signing key", exception.Message);
    }

    [Fact]
    public void Assistant_MaxToolIterationsConfiguresFunctionInvocationClient()
    {
        var inner = NSubstitute.Substitute.For<IChatClient>();
        var options = new AssistantOptionsBuilder()
            .UseChatClient(inner)
            .WithMaxToolIterations(1);
        using var services = new ServiceCollection().BuildServiceProvider();
        using var assistant = new TickerAssistantService(options, services);

        var invoking = Assert.IsType<FunctionInvokingChatClient>(assistant.Client);
        Assert.Equal(1, invoking.MaximumIterationsPerRequest);
    }

    [Fact]
    public async Task Assistant_StreamingFailureLogsButDoesNotExposeExceptionMessage()
    {
        const string secret = "provider=https://secret.example token=do-not-leak";
        var context = new DefaultHttpContext { TraceIdentifier = "assistant-trace-123" };
        context.Response.Body = new MemoryStream();
        context.RequestServices = new ServiceCollection().AddLogging().BuildServiceProvider();

        var endpoints = typeof(TickerAssistantService).Assembly.GetType(
            "TickerQ.Dashboard.Assistant.AssistantEndpoints", throwOnError: true)!;
        var method = endpoints.GetMethod(
            "WriteStreamingFailure", BindingFlags.NonPublic | BindingFlags.Static)!;
        await (Task)method.Invoke(null, [context, new InvalidOperationException(secret)])!;

        context.Response.Body.Position = 0;
        var body = await new StreamReader(context.Response.Body).ReadToEndAsync();

        Assert.Contains("The assistant request failed unexpectedly", body);
        Assert.Contains(context.TraceIdentifier, body);
        Assert.DoesNotContain(secret, body);
    }

    [Fact]
    public async Task WebhookNotifier_CountsDroppedOldestAndSanitizesReason()
    {
        var options = new FailureWebhookOptions
        {
            Url = "https://example.test/hook",
            ReasonSanitizer = _ => "[redacted]",
        };
        var notifier = new WebhookFailureNotifier(
            options, NullLogger<WebhookFailureNotifier>.Instance);

        for (var i = 0; i < WebhookFailureNotifier.Capacity + 1; i++)
            notifier.Notify(new TickerFailureEvent { Reason = "secret-" + i });

        Assert.Equal(1, notifier.DroppedCount);
        Assert.True(notifier.Reader.TryRead(out var queued));
        Assert.Equal("[redacted]", queued!.Reason);
        await Task.CompletedTask;
    }

    [Fact]
    public void WebhookBuilder_ExposesReasonSanitizer()
    {
        static string Sanitize(string _) => "[safe]";
        var builder = new TickerOptionsBuilder<TimeTickerEntity, CronTickerEntity>(
            new TickerExecutionContext(), new SchedulerOptionsBuilder());

        builder.NotifyFailuresViaWebhook("https://example.test/hook", reasonSanitizer: Sanitize);

        Assert.NotNull(builder.FailureWebhook);
        Assert.Equal("[safe]", builder.FailureWebhook.ReasonSanitizer!("secret"));
    }

    [Fact]
    public void WebhookDefaultSanitizer_StripsCrLf()
    {
        var options = new FailureWebhookOptions();

        var sanitized = options.ReasonSanitizer!("line1\r\nline2\rline3\nline4");

        Assert.DoesNotContain('\r', sanitized);
        Assert.DoesNotContain('\n', sanitized);
        Assert.Equal("line1  line2 line3 line4", sanitized);
    }

    [Fact]
    public void WebhookDefaultSanitizer_TruncatesToBound()
    {
        var options = new FailureWebhookOptions();

        var sanitized = options.ReasonSanitizer!(new string('x', 5000));

        Assert.Equal(2048, sanitized.Length);
    }

    [Fact]
    public void WebhookBuilder_NullSanitizer_PreservesDefault()
    {
        var builder = new TickerOptionsBuilder<TimeTickerEntity, CronTickerEntity>(
            new TickerExecutionContext(), new SchedulerOptionsBuilder());

        builder.NotifyFailuresViaWebhook("https://example.test/hook", reasonSanitizer: null);

        Assert.NotNull(builder.FailureWebhook);
        Assert.NotNull(builder.FailureWebhook!.ReasonSanitizer);
        Assert.Equal("a  b", builder.FailureWebhook.ReasonSanitizer!("a\r\nb"));
    }

    [Fact]
    public void WebhookNotifier_NullSanitizer_FallbackIsNotIdentity()
    {
        var options = new FailureWebhookOptions { ReasonSanitizer = null };
        var notifier = new WebhookFailureNotifier(
            options, NullLogger<WebhookFailureNotifier>.Instance);

        notifier.Notify(new TickerFailureEvent { Reason = "a\r\nb" + new string('x', 5000) });

        Assert.True(notifier.Reader.TryRead(out var queued));
        Assert.DoesNotContain('\r', queued!.Reason);
        Assert.DoesNotContain('\n', queued.Reason);
        Assert.Equal(2048, queued.Reason.Length);
    }
}
