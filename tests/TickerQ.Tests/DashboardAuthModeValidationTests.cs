using Microsoft.AspNetCore.Http;
using TickerQ.Dashboard;
using TickerQ.Dashboard.Authentication;

namespace TickerQ.Tests;

/// <summary>
/// Single-mode authentication methods used to be dropped silently when another scheme was
/// registered. Startup validation now rejects the combination instead.
/// </summary>
public class DashboardAuthModeValidationTests
{
    public static TheoryData<string, Action<DashboardOptionsBuilder>> SingleModeMethods => new()
    {
        { "host", o => o.WithHostAuthentication("SchedulerOperators") },
        { "basic", o => o.WithBasicAuth("admin", "secret") },
        { "api-key", o => o.WithApiKey("key") },
        { "custom", o => o.WithCustomAuth(token => token == "ok") },
    };

    public static TheoryData<string, Action<DashboardOptionsBuilder>> SchemeMethods => new()
    {
        { "with-authentication", o => o.WithAuthentication(auth => auth.AddApiKey("other")) },
        { "basic-validator", o => o.WithBasicAuth((_, _) => Task.FromResult(true)) },
        { "api-key-header", o => o.WithApiKey("other", "X-Api-Key") },
        { "custom-context", o => o.WithCustomAuth((HttpContext _) => Task.FromResult(true)) },
    };

    public static TheoryData<string, string> Combinations()
    {
        var data = new TheoryData<string, string>();
        foreach (var single in SingleModeMethods)
            foreach (var scheme in SchemeMethods)
                data.Add((string)single[0], (string)scheme[0]);
        return data;
    }

    [Theory]
    [MemberData(nameof(Combinations))]
    public void Single_mode_method_combined_with_a_scheme_is_rejected(string single, string scheme)
    {
        foreach (var order in new[] { false, true })
        {
            var options = new DashboardOptionsBuilder();
            if (order)
            {
                Find(SchemeMethods, scheme)(options);
                Find(SingleModeMethods, single)(options);
            }
            else
            {
                Find(SingleModeMethods, single)(options);
                Find(SchemeMethods, scheme)(options);
            }

            var error = Assert.Throws<InvalidOperationException>(() => options.Validate());
            Assert.Contains("would be ignored", error.Message);
            Assert.Contains("WithAuthentication", error.Message);
        }
    }

    [Theory]
    [MemberData(nameof(SingleModeMethods))]
    public void Single_mode_method_alone_validates_repeatedly(string name, Action<DashboardOptionsBuilder> configure)
    {
        _ = name;
        var options = new DashboardOptionsBuilder();
        configure(options);

        // Validate runs during registration and again when AuthService is constructed.
        options.Validate();
        options.Validate();
        _ = new AuthService(options.Auth,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<AuthService>.Instance);

        Assert.True(options.Auth.IsEnabled);
    }

    [Fact]
    public void Host_and_custom_together_through_WithAuthentication_are_accepted()
    {
        var options = new DashboardOptionsBuilder()
            .WithAuthentication(auth => auth
                .AddHost("SchedulerOperators")
                .AddCustom((HttpContext _) => Task.FromResult(true)));

        options.Validate();
        options.Validate();

        Assert.Equal(2, options.Auth.Schemes.Count);
    }

    [Fact]
    public void Anonymous_dashboard_with_registered_schemes_is_not_reported_as_a_conflict()
    {
        var options = new DashboardOptionsBuilder()
            .WithAuthentication(auth => auth.AddApiKey("key"))
            .AllowAnonymousDashboard();

        options.Validate();

        Assert.True(options.Auth.IsEnabled);
    }

    private static Action<DashboardOptionsBuilder> Find(
        TheoryData<string, Action<DashboardOptionsBuilder>> data, string name)
        => (Action<DashboardOptionsBuilder>)data.Single(row => (string)row[0] == name)[1];
}
