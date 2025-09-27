using System;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace TickerQ.Dashboard.Authentication;

public class BasicAuthenticationSchemeOptions : AuthenticationSchemeOptions
{
}

public class BasicAuthenticationHandler : AuthenticationHandler<BasicAuthenticationSchemeOptions>
{
    private readonly IConfiguration _configuration;
    private readonly DashboardOptionsBuilder _dashboardOptions;

    public BasicAuthenticationHandler(
        IOptionsMonitor<BasicAuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IConfiguration configuration,
        DashboardOptionsBuilder dashboardOptions)
        : base(options, logger, encoder)
    {
        _configuration = configuration;
        _dashboardOptions = dashboardOptions;
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        // Skip authentication if basic auth is not enabled
        if (!_dashboardOptions.EnableBasicAuth && !_dashboardOptions.EnableBuiltInAuth)
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        // Check for Authorization header
        if (!Request.Headers.ContainsKey("Authorization"))
        {
            return Task.FromResult(AuthenticateResult.Fail("Missing Authorization header"));
        }

        var authHeader = Request.Headers.Authorization.ToString();
        if (!authHeader.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(AuthenticateResult.Fail("Invalid Authorization header"));
        }

        try
        {
            // Extract credentials
            var encodedCredentials = authHeader["Basic ".Length..].Trim();
            var decodedBytes = Convert.FromBase64String(encodedCredentials);
            var decoded = Encoding.UTF8.GetString(decodedBytes);
            var parts = decoded.Split(':', 2);

            if (parts.Length != 2)
            {
                return Task.FromResult(AuthenticateResult.Fail("Invalid credentials format"));
            }

            var username = parts[0];
            var password = parts[1];

            // Validate credentials
            if (ValidateCredentials(username, password))
            {
                var claims = new[]
                {
                    new Claim(ClaimTypes.Name, username),
                    new Claim(ClaimTypes.NameIdentifier, username)
                };

                var identity = new ClaimsIdentity(claims, Scheme.Name);
                var principal = new ClaimsPrincipal(identity);
                var ticket = new AuthenticationTicket(principal, Scheme.Name);

                return Task.FromResult(AuthenticateResult.Success(ticket));
            }

            return Task.FromResult(AuthenticateResult.Fail("Invalid credentials"));
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error during basic authentication");
            return Task.FromResult(AuthenticateResult.Fail("Authentication error"));
        }
    }

    private bool ValidateCredentials(string username, string password)
    {
        // Get credentials from configuration
        var validUser = _configuration["TickerQBasicAuth:Username"] ?? "admin";
        var validPass = _configuration["TickerQBasicAuth:Password"] ?? "admin";

        return username == validUser && password == validPass;
    }
}
