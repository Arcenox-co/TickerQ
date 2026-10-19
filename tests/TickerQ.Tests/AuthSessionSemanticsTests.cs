using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TickerQ.Dashboard.Authentication;
using TickerQ.Dashboard.Authentication.Endpoints;
using TickerQ.Dashboard.Authentication.Jwt;
using TickerQ.Dashboard.Authentication.Schemes;
using Xunit;

namespace TickerQ.Tests;

/// <summary>
/// Regression coverage for the dashboard's honest session contract: sliding
/// access-token renewal (no durable refresh-token store, no revocation, no
/// server-side bearer logout). Drives the real <see cref="AuthEndpoints.MapAuthEndpoints"/>
/// through a TestServer for the endpoint behaviours and the real
/// <see cref="JwtTokenIssuer"/> for the token-claim / lifetime guarantees.
/// </summary>
public class AuthSessionSemanticsTests
{
    private static readonly byte[] Key = Enumerable.Range(0, 32).Select(i => (byte)(i + 1)).ToArray();
    private const string Iss = "tickerq";
    private const string Aud = "tickerq-dashboard";
    private const string CookieName = "tickerq.auth";

    private static JwtTokenIssuer Issuer(TimeSpan lifetime, Func<DateTime>? utcNow = null)
        => new(Key, Iss, Aud, lifetime, utcNow);

    private static async Task<IHost> CreateHostAsync(JwtTokenIssuer issuer)
    {
        var users = new InMemoryUserStore();
        users.AddUser("admin", "correct-horse");

        var config = new AuthConfig
        {
            JwtBearerOptions = new JwtBearerOptions { AccessTokenLifetime = TimeSpan.FromMinutes(30) },
            CookieAuthOptions = new CookieAuthOptions { SecureCookie = true, SessionLifetime = TimeSpan.FromHours(8) },
        };
        config.Schemes.Add(new JwtBearerScheme { Issuer = issuer, Users = users });
        config.Schemes.Add(new CookieAuthScheme { Issuer = issuer, Users = users, CookieName = CookieName });

        return await new HostBuilder()
            .ConfigureWebHost(web =>
            {
                web.UseTestServer();
                web.ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddCors(o => o.AddPolicy("TickerQ_Dashboard_CORS", b => b.AllowAnyOrigin()));
                    services.AddSingleton(config);
                });
                web.Configure(app =>
                {
                    app.UseRouting();
                    app.UseCors();
                    app.UseEndpoints(e => e.MapAuthEndpoints(config));
                });
            })
            .StartAsync();
    }

    private static string? ExtractJwt(string body)
    {
        var m = Regex.Match(body, "[A-Za-z0-9_-]+\\.[A-Za-z0-9_-]+\\.[A-Za-z0-9_-]+");
        return m.Success ? m.Value : null;
    }

    // ───────── endpoint: sliding refresh ─────────

    [Fact]
    public async Task Refresh_WithStillValidBearer_ReissuesTokenAndAdvertisesSlidingSemantics()
    {
        var issuer = Issuer(TimeSpan.FromMinutes(30));
        using var host = await CreateHostAsync(issuer);
        var client = host.GetTestClient();
        var token = issuer.IssueAccessToken("admin");

        var req = new HttpRequestMessage(HttpMethod.Post, "/api/auth/refresh");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var resp = await client.SendAsync(req);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.True(resp.Headers.TryGetValues("X-TickerQ-Session-Semantics", out var semantics));
        Assert.Equal("sliding-access-token", semantics!.Single());

        var reissued = ExtractJwt(await resp.Content.ReadAsStringAsync());
        Assert.NotNull(reissued);
        Assert.True(issuer.TryValidate(reissued!, out var user));
        Assert.Equal("admin", user);
    }

    [Fact]
    public async Task Refresh_WithStillValidCookie_ReissuesToken()
    {
        var issuer = Issuer(TimeSpan.FromMinutes(30));
        using var host = await CreateHostAsync(issuer);
        var client = host.GetTestClient();
        var token = issuer.IssueAccessToken("admin");

        var req = new HttpRequestMessage(HttpMethod.Post, "/api/auth/refresh");
        req.Headers.Add("Cookie", $"{CookieName}={token}");
        var resp = await client.SendAsync(req);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    [Fact]
    public async Task Refresh_WithExpiredToken_IsRejected_ExpiryIsTheOnlyGate()
    {
        // Expired token: minted by a clock two hours in the past, so it is
        // already past its 5-minute lifetime relative to the server's real now.
        var expiredIssuer = Issuer(TimeSpan.FromMinutes(5), () => DateTime.UtcNow.AddHours(-2));
        var expired = expiredIssuer.IssueAccessToken("admin");

        using var host = await CreateHostAsync(Issuer(TimeSpan.FromMinutes(30)));
        var client = host.GetTestClient();

        var req = new HttpRequestMessage(HttpMethod.Post, "/api/auth/refresh");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", expired);
        var resp = await client.SendAsync(req);

        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task Refresh_WithoutAnyToken_IsRejected()
    {
        using var host = await CreateHostAsync(Issuer(TimeSpan.FromMinutes(30)));
        var client = host.GetTestClient();

        var resp = await client.PostAsync("/api/auth/refresh", content: null);

        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    // ───────── endpoint: logout clears the cookie ─────────

    [Fact]
    public async Task Logout_ClearsAuthCookie_AndHasNoServerSideState()
    {
        using var host = await CreateHostAsync(Issuer(TimeSpan.FromMinutes(30)));
        var client = host.GetTestClient();

        var resp = await client.PostAsync("/api/auth/logout", content: null);

        Assert.Equal(HttpStatusCode.NoContent, resp.StatusCode);
        Assert.True(resp.Headers.TryGetValues("Set-Cookie", out var cookies));
        var clearing = cookies!.Single(c => c.StartsWith(CookieName + "=", StringComparison.Ordinal));
        // Deletion is expressed as an empty value with an epoch expiry.
        Assert.Contains("expires=Thu, 01 Jan 1970", clearing, StringComparison.OrdinalIgnoreCase);
    }

    // ───────── token: no refresh-token / revocation claims, bounded lifetime ─────────

    [Fact]
    public void AccessToken_CarriesNoRefreshTokenOrRevocationClaims()
    {
        var token = Issuer(TimeSpan.FromMinutes(30)).IssueAccessToken("admin");

        var payload = DecodePayload(token);
        var claims = payload.EnumerateObject().Select(p => p.Name).ToHashSet(StringComparer.Ordinal);

        Assert.Equal(new HashSet<string>(StringComparer.Ordinal) { "sub", "name", "iss", "aud", "iat", "exp" }, claims);
        foreach (var forbidden in new[] { "jti", "refresh", "refresh_token", "rt", "sid", "revocation", "nonce" })
            Assert.DoesNotContain(forbidden, claims);
    }

    [Fact]
    public void AccessToken_LifetimeIsBoundedByConfiguredLifetime()
    {
        var lifetime = TimeSpan.FromMinutes(30);
        var payload = DecodePayload(Issuer(lifetime).IssueAccessToken("admin"));

        var iat = payload.GetProperty("iat").GetInt64();
        var exp = payload.GetProperty("exp").GetInt64();

        Assert.Equal((long)lifetime.TotalSeconds, exp - iat);
    }

    [Fact]
    public void Refresh_RequiresStillValidToken_ExpiredTokenNoLongerValidates()
    {
        var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var current = t0;
        var issuer = Issuer(TimeSpan.FromMinutes(5), () => current);
        var token = issuer.IssueAccessToken("admin");

        current = t0.AddMinutes(1);
        Assert.True(issuer.TryValidate(token, out _)); // still valid → refreshable

        current = t0.AddMinutes(10);
        Assert.False(issuer.TryValidate(token, out _)); // expired → fresh login required
    }

    private static JsonElement DecodePayload(string token)
    {
        var segment = token.Split('.')[1];
        var s = segment.Replace('-', '+').Replace('_', '/');
        s += new string('=', (4 - s.Length % 4) % 4);
        var json = Encoding.UTF8.GetString(Convert.FromBase64String(s));
        return JsonDocument.Parse(json).RootElement.Clone();
    }
}
