using System.Net;
using System.Reflection;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TickerQ.Dashboard.DependencyInjection;

namespace TickerQ.Tests;

/// <summary>
/// Integration tests for the dashboard authorization middleware pipeline.
/// Covers issue #408: InvalidOperationException when using Host authentication because
/// UseAuthorization() was missing in the dashboard's Map() branch pipeline.
///
/// These tests reproduce the exact pattern used by the dashboard pipeline:
/// a Map() branch with UseRouting() + UseEndpoints() containing endpoints with
/// RequireAuthorization() metadata. Without UseAuthorization() in the branch,
/// ASP.NET Core's EndpointMiddleware throws InvalidOperationException.
/// </summary>
public class DashboardAuthorizationPipelineTests
{
    private static readonly MethodInfo MapPathBaseAwareMethod =
        typeof(ServiceCollectionExtensions).GetMethod(
            "MapPathBaseAware",
            BindingFlags.NonPublic | BindingFlags.Static)!;

    /// <summary>
    /// Reproduces issue #408: a Map() branch pipeline with UseRouting() + UseEndpoints()
    /// where endpoints have RequireAuthorization() metadata but UseAuthorization() is
    /// present in the branch. Should NOT throw InvalidOperationException.
    /// </summary>
    [Fact]
    public async Task BranchPipeline_WithUseAuthorization_AndRequireAuthorization_DoesNotThrow()
    {
        using var host = await CreateTestHost(includeUseAuthorization: true);
        var client = host.GetTestClient();

        var response = await client.GetAsync("/dashboard/api/test");

        // Should get 401 (unauthenticated) but NOT throw InvalidOperationException
        Assert.True(
            response.StatusCode == HttpStatusCode.Unauthorized ||
            response.StatusCode == HttpStatusCode.OK,
            $"Expected 401 or 200, got {(int)response.StatusCode}");
    }

    /// <summary>
    /// Verifies the bug condition: without UseAuthorization() in the branch pipeline,
    /// endpoints with RequireAuthorization() cause InvalidOperationException.
    /// This test documents the exact failure that issue #408 reports.
    /// </summary>
    [Fact]
    public async Task BranchPipeline_WithoutUseAuthorization_AndRequireAuthorization_Throws()
    {
        using var host = await CreateTestHost(includeUseAuthorization: false);
        var client = host.GetTestClient();

        // Without UseAuthorization(), ASP.NET Core's EndpointMiddleware throws
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.GetAsync("/dashboard/api/test"));

        Assert.Contains("authorization", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Endpoints marked with AllowAnonymous should work even with UseAuthorization()
    /// and no authenticated user, matching the dashboard's /api/auth/info behavior.
    /// </summary>
    [Fact]
    public async Task BranchPipeline_AllowAnonymousEndpoint_Returns200()
    {
        using var host = await CreateTestHost(includeUseAuthorization: true);
        var client = host.GetTestClient();

        var response = await client.GetAsync("/dashboard/api/anonymous");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal("anonymous-ok", body);
    }

    /// <summary>
    /// When auth is not configured (no RequireAuthorization on endpoints),
    /// UseAuthorization() is not needed and endpoints work without it.
    /// This covers the non-auth dashboard configuration path.
    /// </summary>
    [Fact]
    public async Task BranchPipeline_NoAuthEndpoints_WorksWithout_UseAuthorization()
    {
        using var host = await CreateTestHostNoAuth();
        var client = host.GetTestClient();

        var response = await client.GetAsync("/dashboard/api/test");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal("no-auth-ok", body);
    }

    /// <summary>
    /// Creates a test host that mirrors the dashboard's Map() branch pipeline pattern:
    /// UseRouting() → UseCors() → [optional UseAuthorization()] → UseEndpoints() with
    /// endpoints that have RequireAuthorization() metadata.
    /// </summary>
    private static async Task<IHost> CreateTestHost(bool includeUseAuthorization)
    {
        var host = await new HostBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder.UseTestServer();
                webBuilder.ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddAuthorization();
                    services.AddAuthentication("Test")
                        .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>("Test", _ => { });
                    services.AddCors(options =>
                    {
                        options.AddPolicy("TestCORS", b => b.AllowAnyOrigin());
                    });
                });
                webBuilder.Configure(app =>
                {
                    // Use MapPathBaseAware to mirror the dashboard's exact pipeline
                    MapPathBaseAwareMethod.Invoke(null, new object[]
                    {
                        app, "/dashboard", new Action<IApplicationBuilder>(branch =>
                        {
                            branch.UseRouting();
                            branch.UseCors("TestCORS");

                            if (includeUseAuthorization)
                            {
                                branch.UseAuthorization();
                            }

                            branch.UseEndpoints(endpoints =>
                            {
                                endpoints.MapGet("/api/test", () => "ok")
                                    .RequireAuthorization()
                                    .RequireCors("TestCORS");

                                endpoints.MapGet("/api/anonymous", () => "anonymous-ok")
                                    .AllowAnonymous()
                                    .RequireCors("TestCORS");
                            });
                        })
                    });

                    app.Run(async context =>
                    {
                        await context.Response.WriteAsync("fallthrough");
                    });
                });
            })
            .StartAsync();

        return host;
    }

    /// <summary>
    /// Creates a test host with no authorization on endpoints (mirrors no-auth dashboard config).
    /// </summary>
    private static async Task<IHost> CreateTestHostNoAuth()
    {
        var host = await new HostBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder.UseTestServer();
                webBuilder.ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddCors(options =>
                    {
                        options.AddPolicy("TestCORS", b => b.AllowAnyOrigin());
                    });
                });
                webBuilder.Configure(app =>
                {
                    MapPathBaseAwareMethod.Invoke(null, new object[]
                    {
                        app, "/dashboard", new Action<IApplicationBuilder>(branch =>
                        {
                            branch.UseRouting();
                            branch.UseCors("TestCORS");

                            branch.UseEndpoints(endpoints =>
                            {
                                endpoints.MapGet("/api/test", () => "no-auth-ok")
                                    .RequireCors("TestCORS");
                            });
                        })
                    });

                    app.Run(async context =>
                    {
                        await context.Response.WriteAsync("fallthrough");
                    });
                });
            })
            .StartAsync();

        return host;
    }

    /// <summary>
    /// Minimal test authentication handler that always returns no-result (unauthenticated).
    /// </summary>
    private class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public TestAuthHandler(
            IOptionsMonitor<AuthenticationSchemeOptions> options,
            ILoggerFactory logger,
            System.Text.Encodings.Web.UrlEncoder encoder)
            : base(options, logger, encoder) { }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }
    }
}
