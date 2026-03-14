using System.Net;
using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Hosting;
using TickerQ.Dashboard.DependencyInjection;

namespace TickerQ.Tests;

/// <summary>
/// Tests for Dashboard PathBase-aware routing (MapPathBaseAware) and CombinePathBase logic.
/// Covers issue #332: Dashboard BasePath not working correctly with UsePathBase.
/// </summary>
public class DashboardPathBaseTests
{
    #region CombinePathBase — via reflection (private static method)

    private static readonly MethodInfo CombinePathBaseMethod =
        typeof(ServiceCollectionExtensions).GetMethod("CombinePathBase", BindingFlags.NonPublic | BindingFlags.Static)!;

    private static string CombinePathBase(string pathBase, string basePath)
        => (string)CombinePathBaseMethod.Invoke(null, new object[] { pathBase, basePath })!;

    [Fact]
    public void CombinePathBase_NoPathBase_ReturnsBasePath()
    {
        var result = CombinePathBase("", "/tickerq/dashboard");
        Assert.Equal("/tickerq/dashboard", result);
    }

    [Fact]
    public void CombinePathBase_NullPathBase_ReturnsBasePath()
    {
        var result = CombinePathBase(null!, "/tickerq/dashboard");
        Assert.Equal("/tickerq/dashboard", result);
    }

    [Fact]
    public void CombinePathBase_RootBasePath_ReturnsPathBase()
    {
        var result = CombinePathBase("/cool-app", "/");
        Assert.Equal("/cool-app", result);
    }

    [Fact]
    public void CombinePathBase_EmptyBasePath_ReturnsRoot()
    {
        var result = CombinePathBase("", "/");
        Assert.Equal("/", result);
    }

    [Fact]
    public void CombinePathBase_BasePathIncludesPathBase_ReturnsBasePath()
    {
        // User sets SetBasePath("/cool-app/dashboard") with UsePathBase("/cool-app")
        var result = CombinePathBase("/cool-app", "/cool-app/dashboard");
        Assert.Equal("/cool-app/dashboard", result);
    }

    [Fact]
    public void CombinePathBase_PathBaseEndsWithBasePath_ReturnsPathBase()
    {
        // Inside Map() branch: ASP.NET adds matched segment to PathBase
        // PathBase="/cool-app/dashboard", basePath="/dashboard"
        var result = CombinePathBase("/cool-app/dashboard", "/dashboard");
        Assert.Equal("/cool-app/dashboard", result);
    }

    [Fact]
    public void CombinePathBase_PathBaseEqualsBasePath_ReturnsBasePath()
    {
        // Inside Map() with no external PathBase: PathBase="/dashboard", basePath="/dashboard"
        var result = CombinePathBase("/dashboard", "/dashboard");
        Assert.Equal("/dashboard", result);
    }

    [Fact]
    public void CombinePathBase_DisjointPaths_Concatenates()
    {
        // PathBase="/api" and basePath="/dashboard" — no overlap
        var result = CombinePathBase("/api", "/dashboard");
        Assert.Equal("/api/dashboard", result);
    }

    [Fact]
    public void CombinePathBase_PathBaseWithTrailingSlash_NormalizesAndConcatenates()
    {
        var result = CombinePathBase("/api/", "/dashboard");
        Assert.Equal("/api/dashboard", result);
    }

    #endregion

    #region MapPathBaseAware — integration tests with TestHost

    [Fact]
    public async Task MapPathBaseAware_NoPathBase_MatchesBasePath()
    {
        // Standard case: no UsePathBase, SetBasePath("/dashboard")
        using var host = await CreateTestHost(
            basePath: "/dashboard",
            usePathBase: null);

        var client = host.GetTestClient();
        var response = await client.GetAsync("/dashboard/test");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal("matched", body);
    }

    [Fact]
    public async Task MapPathBaseAware_NoPathBase_NonMatchingPath_PassesThrough()
    {
        using var host = await CreateTestHost(
            basePath: "/dashboard",
            usePathBase: null);

        var client = host.GetTestClient();
        var response = await client.GetAsync("/other/path");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal("fallthrough", body);
    }

    [Fact]
    public async Task MapPathBaseAware_WithPathBase_NormalBasePath_Matches()
    {
        // Normal case: UsePathBase("/cool-app"), SetBasePath("/dashboard")
        using var host = await CreateTestHost(
            basePath: "/dashboard",
            usePathBase: "/cool-app");

        var client = host.GetTestClient();
        var response = await client.GetAsync("/cool-app/dashboard/test");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal("matched", body);
    }

    [Fact]
    public async Task MapPathBaseAware_WithPathBase_BasePathIncludesPrefix_Matches()
    {
        // Issue #332 scenario: UsePathBase("/cool-app"), SetBasePath("/cool-app/dashboard")
        // Standard Map() would fail here because path is "/dashboard" after PathBase stripping
        using var host = await CreateTestHost(
            basePath: "/cool-app/dashboard",
            usePathBase: "/cool-app");

        var client = host.GetTestClient();
        var response = await client.GetAsync("/cool-app/dashboard/test");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal("matched", body);
    }

    [Fact]
    public async Task MapPathBaseAware_WithPathBase_BasePathIncludesPrefix_SetsCorrectPathBase()
    {
        // Verify that PathBase is correctly set inside the branch
        using var host = await CreateTestHost(
            basePath: "/cool-app/dashboard",
            usePathBase: "/cool-app",
            writePathBase: true);

        var client = host.GetTestClient();
        var response = await client.GetAsync("/cool-app/dashboard/test");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal("/cool-app/dashboard", body);
    }

    [Fact]
    public async Task MapPathBaseAware_WithPathBase_NormalBasePath_SetsCorrectPathBase()
    {
        using var host = await CreateTestHost(
            basePath: "/dashboard",
            usePathBase: "/cool-app",
            writePathBase: true);

        var client = host.GetTestClient();
        var response = await client.GetAsync("/cool-app/dashboard/test");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal("/cool-app/dashboard", body);
    }

    [Fact]
    public async Task MapPathBaseAware_WithPathBase_RootPath_PassesThrough()
    {
        using var host = await CreateTestHost(
            basePath: "/cool-app/dashboard",
            usePathBase: "/cool-app");

        var client = host.GetTestClient();
        var response = await client.GetAsync("/cool-app/other");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal("fallthrough", body);
    }

    [Fact]
    public async Task MapPathBaseAware_ExactBasePathMatch_WithNoTrailingSegment()
    {
        using var host = await CreateTestHost(
            basePath: "/dashboard",
            usePathBase: null);

        var client = host.GetTestClient();
        var response = await client.GetAsync("/dashboard");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal("matched", body);
    }

    /// <summary>
    /// Creates a test host that uses MapPathBaseAware with the given config.
    /// Inside the branch it writes "matched"; outside it writes "fallthrough".
    /// </summary>
    private static async Task<IHost> CreateTestHost(
        string basePath,
        string? usePathBase,
        bool writePathBase = false)
    {
        // Access the private MapPathBaseAware extension method via reflection
        var mapMethod = typeof(ServiceCollectionExtensions).GetMethod(
            "MapPathBaseAware",
            BindingFlags.NonPublic | BindingFlags.Static)!;

        var host = await new HostBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder.UseTestServer();
                webBuilder.Configure(app =>
                {
                    if (usePathBase != null)
                        app.UsePathBase(usePathBase);

                    // Call private MapPathBaseAware via reflection
                    var configuration = new Action<IApplicationBuilder>(branch =>
                    {
                        branch.Run(async context =>
                        {
                            if (writePathBase)
                                await context.Response.WriteAsync(context.Request.PathBase.Value ?? "");
                            else
                                await context.Response.WriteAsync("matched");
                        });
                    });

                    mapMethod.Invoke(null, new object[] { app, basePath, configuration });

                    // Fallthrough
                    app.Run(async context =>
                    {
                        await context.Response.WriteAsync("fallthrough");
                    });
                });
            })
            .StartAsync();

        return host;
    }

    #endregion
}
