using System.Security.Claims;
using Microsoft.Extensions.DependencyInjection;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using TickerQ.Dashboard.Assistant;
using TickerQ.Dashboard.Authentication;
using TickerQ.Dashboard.Authentication.Jwt;
using TickerQ.Dashboard.Authentication.Schemes;

namespace TickerQ.Tests;

public class DashboardUserIdentityTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("Shared display name")]
    public async Task HostSubjects_AreDistinctEvenWithoutUniqueDisplayNames(string? name)
    {
        var first = await HostResult("subject-1", name);
        var second = await HostResult("subject-2", name);
        Assert.True(first.IsAuthenticated);
        Assert.NotNull(first.UserId);
        Assert.NotEqual(first.UserId, second.UserId);
        Assert.Equal(first.Username, second.Username);
    }

    [Fact]
    public async Task HostIdentity_RemainsStableAcrossRenames_AndScopesIssuerAndScheme()
    {
        var first = await HostResult("subject", "Old name");
        Assert.Equal(first.UserId, (await HostResult("subject", "New name")).UserId);
        Assert.NotEqual(first.UserId, (await HostResult("subject", "Old name", issuer: "other-issuer")).UserId);
        Assert.NotEqual(first.UserId, (await HostResult("subject", "Old name", scheme: "other-scheme")).UserId);
    }

    [Fact]
    public async Task ConfiguredNameClaims_AreShownInOrder_AndDoNotChangeTheUserId()
    {
        var identity = new ClaimsIdentity(
            [new Claim("sub", "subject"), new Claim(ClaimTypes.Name, "jdoe"), new Claim("email", "jane@example.com")],
            "host");
        var services = new ServiceCollection()
            .AddSingleton(new AuthConfig { HostUserNameClaims = ["preferred_username", "email"] })
            .BuildServiceProvider();
        var context = new DefaultHttpContext { User = new ClaimsPrincipal(identity), RequestServices = services };

        var result = await new HostAuthScheme().TryAuthenticateAsync(context);

        Assert.Equal("jane@example.com", result.Username);
        var withoutSetting = await new HostAuthScheme().TryAuthenticateAsync(
            new DefaultHttpContext { User = new ClaimsPrincipal(identity) });
        Assert.Equal("jdoe", withoutSetting.Username);
        Assert.NotNull(result.UserId);
        Assert.Equal(withoutSetting.UserId, result.UserId);
        Assert.Equal("jdoe", HostAuthScheme.DisplayName(identity, ["missing"]));
        Assert.Equal("host-user", HostAuthScheme.DisplayName(new ClaimsIdentity([], "host"), null));
    }

    [Fact]
    public void UseUserNameClaim_RejectsEmptyClaimTypes()
    {
        var options = new TickerQ.Dashboard.DashboardOptionsBuilder();

        Assert.Throws<ArgumentException>(() => options.UseUserNameClaim());
        Assert.Throws<ArgumentException>(() => options.UseUserNameClaim("email", " "));
        options.UseUserNameClaim(" email ");
        Assert.Equal(["email"], options.Auth.HostUserNameClaims);
    }

    [Fact]
    public async Task UnauthenticatedClaims_AndDisplayNameOnly_NeverBecomeHistoryOwners()
    {
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new[]
            {
                new ClaimsIdentity([new Claim("sub", "forged"), new Claim(ClaimTypes.Name, "forged")]),
                new ClaimsIdentity([new Claim(ClaimTypes.Name, "display-only")], "host")
            })
        };
        var result = await new HostAuthScheme().TryAuthenticateAsync(context);
        Assert.True(result.IsAuthenticated);
        Assert.Equal("display-only", result.Username);
        Assert.Null(result.UserId);
        context.Items["auth.username"] = "display-only";
        Assert.Null(AssistantEndpoints.ResolveUserId(context));
    }

    [Fact]
    public async Task BasicIdentityUsesValidatedUsername_WhileCustomBooleanAndAnonymousRemainUnpersisted()
    {
        var scheme = new BasicAuthScheme { AsyncValidator = (_, _) => Task.FromResult(true) };
        var context = new DefaultHttpContext();
        context.Request.Headers.Authorization = "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("alice:password"));
        var result = await scheme.TryAuthenticateAsync(context);
        Assert.Equal("alice", result.Username);
        Assert.NotNull(result.UserId);
        Assert.Equal(result.UserId, (await scheme.TryAuthenticateAsync(context)).UserId);
        context.Request.Headers.Authorization = "custom-token";
        Assert.Null((await new CustomAuthScheme { Validator = _ => true }.TryAuthenticateAsync(context)).UserId);
        Assert.Null((await new AuthService(new AuthConfig(), NullLogger<AuthService>.Instance).AuthenticateAsync(context)).UserId);
    }

    [Fact]
    public async Task CookieAndBearerTransportsShareTheValidatedCredentialIdentity()
    {
        var issuer = new JwtTokenIssuer(new byte[32], "issuer", "audience", TimeSpan.FromHours(1));
        var token = issuer.IssueAccessToken("alice");
        var context = new DefaultHttpContext();
        context.Request.Headers.Authorization = "Bearer " + token;
        context.Request.Headers.Cookie = "tickerq.auth=" + token;
        var bearer = await new JwtBearerScheme { Issuer = issuer }.TryAuthenticateAsync(context);
        var cookie = await new CookieAuthScheme { Issuer = issuer }.TryAuthenticateAsync(context);
        Assert.NotNull(bearer.UserId);
        Assert.Equal(bearer.UserId, cookie.UserId);
    }

    private static Task<AuthResult> HostResult(string subject, string? name, string issuer = "issuer", string scheme = "host")
    {
        var claims = new List<Claim> { new("sub", subject), new("iss", issuer) };
        if (name != null) claims.Add(new(ClaimTypes.Name, name));
        return new HostAuthScheme().TryAuthenticateAsync(new DefaultHttpContext
        { User = new ClaimsPrincipal(new ClaimsIdentity(claims, scheme)) });
    }
}
