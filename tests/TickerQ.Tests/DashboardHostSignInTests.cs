using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TickerQ.Dashboard;
using TickerQ.Dashboard.Endpoints;

namespace TickerQ.Tests;

/// <summary>
/// Host mode without a configured login page: the dashboard's Sign in starts the host's own challenge
/// and returns to the dashboard, and never loops for a user who is already signed in.
/// </summary>
public class DashboardHostSignInTests
{
    [Fact]
    public async Task Anonymous_user_is_challenged_with_the_dashboard_as_return_address()
    {
        var context = Context(authenticated: false);

        await (await DashboardEndpoints.HostSignInChallenge(context)).ExecuteAsync(context);

        Assert.Equal(StatusCodes.Status302Found, context.Response.StatusCode);
        // The host's own login page (not one under the dashboard prefix), returning to the dashboard.
        Assert.Equal("http://localhost/shop/account/login?ReturnUrl=%2Fshop%2Ftickerq%2Fdashboard%2F",
            context.Response.Headers.Location.ToString());
        Assert.Equal("/shop/tickerq/dashboard", context.Request.PathBase.Value);
    }

    [Fact]
    public async Task Signed_in_user_goes_back_to_the_dashboard()
    {
        var context = Context(authenticated: true);

        await (await DashboardEndpoints.HostSignInChallenge(context)).ExecuteAsync(context);

        Assert.Equal(StatusCodes.Status302Found, context.Response.StatusCode);
        Assert.Equal("/shop/tickerq/dashboard/", context.Response.Headers.Location.ToString());
    }

    [Fact]
    public async Task Other_modes_answer_unauthorized()
    {
        var context = Context(authenticated: false, host: false);

        await (await DashboardEndpoints.HostSignInChallenge(context)).ExecuteAsync(context);

        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
    }

    private static DefaultHttpContext Context(bool authenticated, bool host = true)
    {
        var options = new DashboardOptionsBuilder();
        if (host) options.WithHostAuthentication();
        else options.WithBasicAuth("user", "password");
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(options);
        services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(cookie => cookie.LoginPath = "/account/login");
        var context = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        // As inside the dashboard branch of an application hosted under /shop.
        context.Items[TickerQ.Dashboard.DependencyInjection.ServiceCollectionExtensions.ApplicationPathBaseItem] =
            new PathString("/shop");
        context.Request.PathBase = "/shop/tickerq/dashboard";
        context.Request.Path = "/auth/challenge";
        context.Request.Scheme = "http";
        context.Request.Host = new HostString("localhost");
        if (authenticated)
            context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, "jane")], "host"));
        return context;
    }
}
