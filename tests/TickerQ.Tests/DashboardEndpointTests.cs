using System;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Logging;
using NSubstitute;
using TickerQ.Dashboard;
using TickerQ.Dashboard.Authentication;
using TickerQ.Utilities;
using TickerQ.Utilities.DashboardDtos;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Interfaces.Managers;
using TickerQ.Utilities.Models;

namespace TickerQ.Tests;

/// <summary>
/// Tests for dashboard-specific logic: AuthService modes (Basic, ApiKey, Custom),
/// AuthConfig validation, and AuthInfo/AuthResult DTOs.
///
/// The endpoint handler methods in DashboardEndpoints are thin wrappers that delegate
/// to ITickerDashboardRepository, ITimeTickerManager, ICronTickerManager, and ITickerQHostScheduler.
/// Those manager/repository interactions are covered by TickerManagerTests and related test files.
/// AuthService Host mode is covered by AuthServiceHostTests.
/// </summary>
public class DashboardEndpointTests
{
    #region AuthService — Basic Authentication

    [Fact]
    public async Task AuthenticateAsync_BasicMode_ValidCredentials_ReturnsSuccessWithUsername()
    {
        var config = new AuthConfig
        {
            Mode = AuthMode.Basic,
            BasicCredentials = Convert.ToBase64String(Encoding.UTF8.GetBytes("admin:secret"))
        };
        var logger = Substitute.For<ILogger<AuthService>>();
        var svc = new AuthService(config, logger);

        var context = new DefaultHttpContext();
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes("admin:secret"));
        context.Request.Headers.Authorization = $"Basic {encoded}";

        var result = await svc.AuthenticateAsync(context);

        Assert.True(result.IsAuthenticated);
        Assert.Equal("admin", result.Username);
    }

    [Fact]
    public async Task AuthenticateAsync_BasicMode_RawCredentials_WithoutPrefix_ReturnsSuccess()
    {
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes("user1:pass1"));
        var config = new AuthConfig
        {
            Mode = AuthMode.Basic,
            BasicCredentials = encoded
        };
        var logger = Substitute.For<ILogger<AuthService>>();
        var svc = new AuthService(config, logger);

        var context = new DefaultHttpContext();
        // Send raw base64 without "Basic " prefix
        context.Request.Headers.Authorization = encoded;

        var result = await svc.AuthenticateAsync(context);

        Assert.True(result.IsAuthenticated);
        Assert.Equal("user1", result.Username);
    }

    [Fact]
    public async Task AuthenticateAsync_BasicMode_InvalidCredentials_ReturnsFailure()
    {
        var config = new AuthConfig
        {
            Mode = AuthMode.Basic,
            BasicCredentials = Convert.ToBase64String(Encoding.UTF8.GetBytes("admin:secret"))
        };
        var logger = Substitute.For<ILogger<AuthService>>();
        var svc = new AuthService(config, logger);

        var context = new DefaultHttpContext();
        var wrongEncoded = Convert.ToBase64String(Encoding.UTF8.GetBytes("admin:wrong"));
        context.Request.Headers.Authorization = $"Basic {wrongEncoded}";

        var result = await svc.AuthenticateAsync(context);

        Assert.False(result.IsAuthenticated);
        Assert.Contains("Invalid credentials", result.ErrorMessage);
    }

    [Fact]
    public async Task AuthenticateAsync_BasicMode_NoAuthHeader_ReturnsFailure()
    {
        var config = new AuthConfig
        {
            Mode = AuthMode.Basic,
            BasicCredentials = Convert.ToBase64String(Encoding.UTF8.GetBytes("admin:secret"))
        };
        var logger = Substitute.For<ILogger<AuthService>>();
        var svc = new AuthService(config, logger);

        var context = new DefaultHttpContext();

        var result = await svc.AuthenticateAsync(context);

        Assert.False(result.IsAuthenticated);
        Assert.Contains("No authorization provided", result.ErrorMessage);
    }

    #endregion

    #region AuthService — ApiKey Authentication

    [Fact]
    public async Task AuthenticateAsync_ApiKeyMode_ValidBearerToken_ReturnsSuccess()
    {
        var config = new AuthConfig
        {
            Mode = AuthMode.ApiKey,
            ApiKey = "my-secret-key-123"
        };
        var logger = Substitute.For<ILogger<AuthService>>();
        var svc = new AuthService(config, logger);

        var context = new DefaultHttpContext();
        context.Request.Headers.Authorization = "Bearer my-secret-key-123";

        var result = await svc.AuthenticateAsync(context);

        Assert.True(result.IsAuthenticated);
        Assert.Equal("api-user", result.Username);
    }

    [Fact]
    public async Task AuthenticateAsync_ApiKeyMode_BearerColonFormat_ReturnsSuccess()
    {
        var config = new AuthConfig
        {
            Mode = AuthMode.ApiKey,
            ApiKey = "key-456"
        };
        var logger = Substitute.For<ILogger<AuthService>>();
        var svc = new AuthService(config, logger);

        var context = new DefaultHttpContext();
        context.Request.Headers.Authorization = "Bearer:key-456";

        var result = await svc.AuthenticateAsync(context);

        Assert.True(result.IsAuthenticated);
        Assert.Equal("api-user", result.Username);
    }

    [Fact]
    public async Task AuthenticateAsync_ApiKeyMode_RawToken_ReturnsSuccess()
    {
        var config = new AuthConfig
        {
            Mode = AuthMode.ApiKey,
            ApiKey = "raw-key"
        };
        var logger = Substitute.For<ILogger<AuthService>>();
        var svc = new AuthService(config, logger);

        var context = new DefaultHttpContext();
        context.Request.Headers.Authorization = "raw-key";

        var result = await svc.AuthenticateAsync(context);

        Assert.True(result.IsAuthenticated);
    }

    [Fact]
    public async Task AuthenticateAsync_ApiKeyMode_InvalidToken_ReturnsFailure()
    {
        var config = new AuthConfig
        {
            Mode = AuthMode.ApiKey,
            ApiKey = "correct-key"
        };
        var logger = Substitute.For<ILogger<AuthService>>();
        var svc = new AuthService(config, logger);

        var context = new DefaultHttpContext();
        context.Request.Headers.Authorization = "Bearer wrong-key";

        var result = await svc.AuthenticateAsync(context);

        Assert.False(result.IsAuthenticated);
        Assert.Contains("Invalid token", result.ErrorMessage);
    }

    [Fact]
    public async Task AuthenticateAsync_ApiKeyMode_ViaQueryParameter_ReturnsSuccess()
    {
        var config = new AuthConfig
        {
            Mode = AuthMode.ApiKey,
            ApiKey = "query-key"
        };
        var logger = Substitute.For<ILogger<AuthService>>();
        var svc = new AuthService(config, logger);

        var context = new DefaultHttpContext();
        context.Request.QueryString = new QueryString("?access_token=query-key");

        var result = await svc.AuthenticateAsync(context);

        Assert.True(result.IsAuthenticated);
    }

    #endregion

    #region AuthService — Custom Authentication

    [Fact]
    public async Task AuthenticateAsync_CustomMode_ValidatorReturnsTrue_ReturnsSuccess()
    {
        var config = new AuthConfig
        {
            Mode = AuthMode.Custom,
            CustomValidator = token => token == "custom-token-abc"
        };
        var logger = Substitute.For<ILogger<AuthService>>();
        var svc = new AuthService(config, logger);

        var context = new DefaultHttpContext();
        context.Request.Headers.Authorization = "custom-token-abc";

        var result = await svc.AuthenticateAsync(context);

        Assert.True(result.IsAuthenticated);
        Assert.Equal("custom-user", result.Username);
    }

    [Fact]
    public async Task AuthenticateAsync_CustomMode_ValidatorReturnsFalse_ReturnsFailure()
    {
        var config = new AuthConfig
        {
            Mode = AuthMode.Custom,
            CustomValidator = _ => false
        };
        var logger = Substitute.For<ILogger<AuthService>>();
        var svc = new AuthService(config, logger);

        var context = new DefaultHttpContext();
        context.Request.Headers.Authorization = "some-token";

        var result = await svc.AuthenticateAsync(context);

        Assert.False(result.IsAuthenticated);
        Assert.Contains("Custom authentication failed", result.ErrorMessage);
    }

    [Fact]
    public async Task AuthenticateAsync_CustomMode_ValidatorThrows_ReturnsFailure()
    {
        var config = new AuthConfig
        {
            Mode = AuthMode.Custom,
            CustomValidator = _ => throw new InvalidOperationException("boom")
        };
        var logger = Substitute.For<ILogger<AuthService>>();
        var svc = new AuthService(config, logger);

        var context = new DefaultHttpContext();
        context.Request.Headers.Authorization = "any-token";

        var result = await svc.AuthenticateAsync(context);

        Assert.False(result.IsAuthenticated);
    }

    #endregion

    #region AuthService — None Mode

    [Fact]
    public async Task AuthenticateAsync_NoneMode_AlwaysReturnsAnonymousSuccess()
    {
        var config = new AuthConfig { Mode = AuthMode.None };
        var logger = Substitute.For<ILogger<AuthService>>();
        var svc = new AuthService(config, logger);

        var context = new DefaultHttpContext();

        var result = await svc.AuthenticateAsync(context);

        Assert.True(result.IsAuthenticated);
        Assert.Equal("anonymous", result.Username);
    }

    #endregion

    #region AuthService — GetAuthInfo

    [Fact]
    public void GetAuthInfo_ReturnsCorrectInfo_ForBasicMode()
    {
        var config = new AuthConfig
        {
            Mode = AuthMode.Basic,
            BasicCredentials = Convert.ToBase64String(Encoding.UTF8.GetBytes("u:p")),
            SessionTimeoutMinutes = 120
        };
        var logger = Substitute.For<ILogger<AuthService>>();
        var svc = new AuthService(config, logger);

        var info = svc.GetAuthInfo();

        Assert.Equal(AuthMode.Basic, info.Mode);
        Assert.True(info.IsEnabled);
        Assert.Equal(120, info.SessionTimeoutMinutes);
    }

    [Fact]
    public void GetAuthInfo_ReturnsNotEnabled_ForNoneMode()
    {
        var config = new AuthConfig { Mode = AuthMode.None };
        var logger = Substitute.For<ILogger<AuthService>>();
        var svc = new AuthService(config, logger);

        var info = svc.GetAuthInfo();

        Assert.Equal(AuthMode.None, info.Mode);
        Assert.False(info.IsEnabled);
    }

    #endregion

    #region AuthConfig — Validation

    [Fact]
    public void AuthConfig_Validate_BasicMode_NullCredentials_Throws()
    {
        var config = new AuthConfig { Mode = AuthMode.Basic, BasicCredentials = null };

        var ex = Assert.Throws<InvalidOperationException>(() => config.Validate());
        Assert.Contains("BasicCredentials", ex.Message);
    }

    [Fact]
    public void AuthConfig_Validate_ApiKeyMode_NullApiKey_Throws()
    {
        var config = new AuthConfig { Mode = AuthMode.ApiKey, ApiKey = null };

        var ex = Assert.Throws<InvalidOperationException>(() => config.Validate());
        Assert.Contains("ApiKey", ex.Message);
    }

    [Fact]
    public void AuthConfig_Validate_CustomMode_NullValidator_Throws()
    {
        var config = new AuthConfig { Mode = AuthMode.Custom, CustomValidator = null };

        var ex = Assert.Throws<InvalidOperationException>(() => config.Validate());
        Assert.Contains("CustomValidator", ex.Message);
    }

    [Fact]
    public void AuthConfig_Validate_NoneMode_DoesNotThrow()
    {
        var config = new AuthConfig { Mode = AuthMode.None };

        // Should not throw
        config.Validate();
    }

    [Fact]
    public void AuthConfig_Validate_HostMode_DoesNotThrow()
    {
        var config = new AuthConfig { Mode = AuthMode.Host };

        // Should not throw even without policy
        config.Validate();
    }

    [Fact]
    public void AuthConfig_IsEnabled_ReturnsFalse_OnlyForNone()
    {
        Assert.False(new AuthConfig { Mode = AuthMode.None }.IsEnabled);
        Assert.True(new AuthConfig { Mode = AuthMode.Basic }.IsEnabled);
        Assert.True(new AuthConfig { Mode = AuthMode.ApiKey }.IsEnabled);
        Assert.True(new AuthConfig { Mode = AuthMode.Host }.IsEnabled);
        Assert.True(new AuthConfig { Mode = AuthMode.Custom }.IsEnabled);
    }

    #endregion

    #region AuthResult — Static Factories

    [Fact]
    public void AuthResult_Success_DefaultUsername_IsUser()
    {
        var result = AuthResult.Success();

        Assert.True(result.IsAuthenticated);
        Assert.Equal("user", result.Username);
    }

    [Fact]
    public void AuthResult_Success_WithUsername_SetsUsername()
    {
        var result = AuthResult.Success("alice");

        Assert.True(result.IsAuthenticated);
        Assert.Equal("alice", result.Username);
    }

    [Fact]
    public void AuthResult_Failure_DefaultMessage()
    {
        var result = AuthResult.Failure();

        Assert.False(result.IsAuthenticated);
        Assert.Equal("Authentication failed", result.ErrorMessage);
    }

    [Fact]
    public void AuthResult_Failure_CustomMessage()
    {
        var result = AuthResult.Failure("bad token");

        Assert.False(result.IsAuthenticated);
        Assert.Equal("bad token", result.ErrorMessage);
    }

    #endregion

    #region DashboardOptionsBuilder — Auth Configuration

    [Fact]
    public void WithNoAuth_SetsNoneMode()
    {
        var builder = new DashboardOptionsBuilder();
        builder.WithNoAuth();

        Assert.Equal(AuthMode.None, builder.Auth.Mode);
    }

    [Fact]
    public void WithBasicAuth_SetsBasicMode_AndEncodesCredentials()
    {
        var builder = new DashboardOptionsBuilder();
        builder.WithBasicAuth("admin", "pass123");

        Assert.Equal(AuthMode.Basic, builder.Auth.Mode);
        var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(builder.Auth.BasicCredentials));
        Assert.Equal("admin:pass123", decoded);
    }

    [Fact]
    public void WithApiKey_SetsApiKeyMode()
    {
        var builder = new DashboardOptionsBuilder();
        builder.WithApiKey("my-key");

        Assert.Equal(AuthMode.ApiKey, builder.Auth.Mode);
        Assert.Equal("my-key", builder.Auth.ApiKey);
    }

    [Fact]
    public void WithHostAuthentication_SetsHostMode_WithOptionalPolicy()
    {
        var builder = new DashboardOptionsBuilder();
        builder.WithHostAuthentication("AdminPolicy");

        Assert.Equal(AuthMode.Host, builder.Auth.Mode);
        Assert.Equal("AdminPolicy", builder.Auth.HostAuthorizationPolicy);
    }

    [Fact]
    public void WithHostAuthentication_NullPolicy_SetsHostMode()
    {
        var builder = new DashboardOptionsBuilder();
        builder.WithHostAuthentication();

        Assert.Equal(AuthMode.Host, builder.Auth.Mode);
        Assert.Null(builder.Auth.HostAuthorizationPolicy);
    }

    [Fact]
    public void WithCustomAuth_SetsCustomMode_AndValidator()
    {
        Func<string, bool> validator = t => t == "valid";
        var builder = new DashboardOptionsBuilder();
        builder.WithCustomAuth(validator);

        Assert.Equal(AuthMode.Custom, builder.Auth.Mode);
        Assert.True(builder.Auth.CustomValidator("valid"));
        Assert.False(builder.Auth.CustomValidator("invalid"));
    }

    [Fact]
    public void WithSessionTimeout_SetsTimeoutMinutes()
    {
        var builder = new DashboardOptionsBuilder();
        builder.WithSessionTimeout(30);

        Assert.Equal(30, builder.Auth.SessionTimeoutMinutes);
    }

    [Fact]
    public void Validate_DelegatesToAuthConfig()
    {
        var builder = new DashboardOptionsBuilder();
        builder.WithApiKey("valid-key");

        // Should not throw
        builder.Validate();
    }

    [Fact]
    public void Validate_InvalidConfig_Throws()
    {
        var builder = new DashboardOptionsBuilder();
        // Set ApiKey mode but leave ApiKey null
        builder.Auth.Mode = AuthMode.ApiKey;
        builder.Auth.ApiKey = null;

        Assert.Throws<InvalidOperationException>(() => builder.Validate());
    }

    #endregion

    #region DashboardOptionsBuilder — Fluent Chaining

    [Fact]
    public void FluentChaining_ReturnsBuilderInstance()
    {
        var builder = new DashboardOptionsBuilder();

        var result = builder.WithNoAuth().WithSessionTimeout(45);

        Assert.Same(builder, result);
    }

    #endregion

    #region AuthConfig — Default Values

    [Fact]
    public void AuthConfig_Defaults_AreCorrect()
    {
        var config = new AuthConfig();

        Assert.Equal(AuthMode.None, config.Mode);
        Assert.Null(config.BasicCredentials);
        Assert.Null(config.ApiKey);
        Assert.Null(config.CustomValidator);
        Assert.Equal(60, config.SessionTimeoutMinutes);
        Assert.Null(config.HostAuthorizationPolicy);
        Assert.False(config.IsEnabled);
    }

    #endregion
}
