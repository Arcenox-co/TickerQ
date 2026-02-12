using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using System.Security.Claims;
using TickerQ.Dashboard.Authentication;

namespace TickerQ.Tests;

public class AuthServiceHostTests
{
    [Fact]
    public async Task AuthenticateAsync_HostMode_UserAuthenticated_WithName_ReturnsSuccess()
    {
        var config = new AuthConfig { Mode = AuthMode.Host };
        var logger = Substitute.For<ILogger<AuthService>>();
        var svc = new AuthService(config, logger);

        var context = new DefaultHttpContext();
        var identity = new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, "alice") }, "TestAuth");
        context.User = new ClaimsPrincipal(identity);

        var result = await svc.AuthenticateAsync(context);

        result.IsAuthenticated.Should().BeTrue();
        result.Username.Should().Be("alice");
    }

    [Fact]
    public async Task AuthenticateAsync_HostMode_UserAuthenticated_WithoutName_ReturnsHostUser()
    {
        var config = new AuthConfig { Mode = AuthMode.Host };
        var logger = Substitute.For<ILogger<AuthService>>();
        var svc = new AuthService(config, logger);

        var context = new DefaultHttpContext();
        // Create an authenticated identity without a name
        var identity = new ClaimsIdentity(System.Array.Empty<Claim>(), "TestAuth");
        context.User = new ClaimsPrincipal(identity);

        var result = await svc.AuthenticateAsync(context);

        result.IsAuthenticated.Should().BeTrue();
        result.Username.Should().Be("host-user");
    }

    [Fact]
    public async Task AuthenticateAsync_HostMode_UserNotAuthenticated_ReturnsFailure()
    {
        var config = new AuthConfig { Mode = AuthMode.Host };
        var logger = Substitute.For<ILogger<AuthService>>();
        var svc = new AuthService(config, logger);

        var context = new DefaultHttpContext();
        // Unauthenticated identity
        context.User = new ClaimsPrincipal(new ClaimsIdentity());

        var result = await svc.AuthenticateAsync(context);

        result.IsAuthenticated.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Host authentication required");
    }

    [Fact]
    public async Task AuthenticateAsync_HostMode_WithPolicy_AuthorizationFails_But_DefaultIdentityAuthenticates_ShouldFail()
    {
        var config = new AuthConfig { Mode = AuthMode.Host, HostAuthorizationPolicy = "MyPolicy" };
        var logger = Substitute.For<ILogger<AuthService>>();
        var svc = new AuthService(config, logger);

        var context = new DefaultHttpContext();
        var identity = new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, "alice") }, "TestAuth");
        context.User = new ClaimsPrincipal(identity);

        // Mock IAuthorizationService to return failure for the policy
        var authorizationService = Substitute.For<IAuthorizationService>();
        authorizationService.AuthorizeAsync(Arg.Any<ClaimsPrincipal>(), Arg.Any<object?>(), "MyPolicy")
            .Returns(Task.FromResult(AuthorizationResult.Failed()));

        var services = new ServiceCollection();
        services.AddSingleton(authorizationService);
        context.RequestServices = services.BuildServiceProvider();

        var result = await svc.AuthenticateAsync(context);

        // Expected: authentication should fail because the policy was not satisfied.
        result.IsAuthenticated.Should().BeFalse();
    }

    [Fact]
    public async Task AuthenticateAsync_HostMode_WithPolicy_Authorized_ReturnsSuccess()
    {
        var config = new AuthConfig { Mode = AuthMode.Host, HostAuthorizationPolicy = "MyPolicy" };
        var logger = Substitute.For<ILogger<AuthService>>();
        var svc = new AuthService(config, logger);

        var context = new DefaultHttpContext();
        var identity = new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, "alice") }, "TestAuth");
        context.User = new ClaimsPrincipal(identity);

        // Mock IAuthorizationService to return success for the given policy
        var authorizationService = Substitute.For<IAuthorizationService>();
        authorizationService.AuthorizeAsync(Arg.Any<ClaimsPrincipal>(), Arg.Any<object?>(), "MyPolicy")
            .Returns(Task.FromResult(AuthorizationResult.Success()));

        var services = new ServiceCollection();
        services.AddSingleton(authorizationService);
        context.RequestServices = services.BuildServiceProvider();

        var result = await svc.AuthenticateAsync(context);

        result.IsAuthenticated.Should().BeTrue();
        result.Username.Should().Be("alice");
    }

    [Fact]
    public async Task AuthenticateAsync_HostMode_WithMultipleIdentities_SecondaryIdentitySatisfiesPolicy_ReturnsSuccessWithSecondaryName()
    {
        var config = new AuthConfig { Mode = AuthMode.Host, HostAuthorizationPolicy = "MyPolicy" };
        var logger = Substitute.For<ILogger<AuthService>>();
        var svc = new AuthService(config, logger);

        var context = new DefaultHttpContext();

        // First identity is unauthenticated (default)
        var id1 = new ClaimsIdentity();

        // Second identity is authenticated
        var id2 = new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, "second-identity") }, "AuthType");

        context.User = new ClaimsPrincipal(new[] { id1, id2 });

        // Mock IAuthorizationService to return success when invoked with the policy
        var authorizationService = Substitute.For<IAuthorizationService>();
        authorizationService.AuthorizeAsync(Arg.Any<ClaimsPrincipal>(), Arg.Any<object?>(), "MyPolicy")
            .Returns(Task.FromResult(AuthorizationResult.Success()));

        var services = new ServiceCollection();
        services.AddSingleton(authorizationService);
        context.RequestServices = services.BuildServiceProvider();

        var result = await svc.AuthenticateAsync(context);

        result.IsAuthenticated.Should().BeTrue();
        result.Username.Should().Be("host-user");
    }
}
