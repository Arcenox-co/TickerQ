using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace TickerQ.Dashboard.Authentication.Schemes;

/// <summary>
/// HTTP Basic authentication — reads <c>Authorization: Basic &lt;base64&gt;</c>
/// (or a raw base64 string for backward compatibility with the legacy single-
/// credential format) and compares against a fixed credential string.
/// </summary>
internal sealed class BasicAuthScheme : AuthSchemeBase
{
    public override string Name => "Basic";
    public override AuthMode LegacyMode => AuthMode.Basic;

    /// <summary>Base64-encoded <c>username:password</c> the dashboard accepts.</summary>
    public string? Credentials { get; set; }

    /// <summary>
    /// Async credential validator — receives the decoded username and
    /// password and can await a database / cache / identity provider.
    /// When set, <see cref="Credentials"/> is ignored.
    /// </summary>
    public Func<string, string, Task<bool>>? AsyncValidator { get; set; }

    /// <summary>
    /// When true, a failed authentication ends with
    /// <c>WWW-Authenticate: Basic realm="…"</c> so browsers show the native
    /// login prompt. Off by default — SPA login flows prefer a plain 401.
    /// </summary>
    public bool ChallengeBrowserPrompt { get; set; }

    /// <summary>Realm name advertised on the browser prompt.</summary>
    public string Realm { get; set; } = "TickerQ Dashboard";

    public override async Task<AuthResult> TryAuthenticateAsync(HttpContext context)
    {
        var raw = ReadAuthorizationValue(context);
        if (string.IsNullOrEmpty(raw)) return AuthResult.Failure("No authorization provided");

        // Accept "Basic <b64>" and raw "<b64>" (legacy)
        var b64 = raw.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase) ? raw.Substring(6) : raw;

        try
        {
            if (AsyncValidator != null)
            {
                var decodedPair = Encoding.UTF8.GetString(Convert.FromBase64String(b64));
                var parts = decodedPair.Split(':', 2);
                if (parts.Length != 2)
                    return AuthResult.Failure("Invalid basic auth format");

                return await AsyncValidator(parts[0], parts[1])
                    ? AuthResult.Success(parts[0])
                    : AuthResult.Failure("Invalid credentials");
            }

            var suppliedBytes = Encoding.ASCII.GetBytes(b64);
            var expectedBytes = Encoding.ASCII.GetBytes(Credentials!);
            if (suppliedBytes.Length != expectedBytes.Length ||
                !CryptographicOperations.FixedTimeEquals(suppliedBytes, expectedBytes))
                return AuthResult.Failure("Invalid credentials");

            var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(b64));
            var username = decoded.Split(':', 2)[0];
            return AuthResult.Success(username);
        }
        catch
        {
            return AuthResult.Failure("Invalid basic auth format");
        }
    }

    public override string? GetChallengeHeader(HttpContext context)
    {
        if (!ChallengeBrowserPrompt) return null;

        // Browsers pop their NATIVE credential dialog whenever a response
        // carries `WWW-Authenticate: Basic` — including responses to the
        // SPA's own fetch() calls, which puts the native dialog on top of
        // the dashboard's login page. Programmatic requests identify
        // themselves via Sec-Fetch-Mode (cors / same-origin / no-cors);
        // real navigations send "navigate". Non-browser clients (curl,
        // scripts) omit the header entirely and still get the challenge.
        var fetchMode = context.Request.Headers["Sec-Fetch-Mode"].FirstOrDefault();
        if (!string.IsNullOrEmpty(fetchMode) &&
            !string.Equals(fetchMode, "navigate", StringComparison.OrdinalIgnoreCase))
            return null;

        return $"Basic realm=\"{Realm}\"";
    }

    public override void Validate()
    {
        if (string.IsNullOrEmpty(Credentials) && AsyncValidator == null)
            throw new InvalidOperationException("BasicCredentials or an async validator is required for Basic authentication mode");
    }

    private static string? ReadAuthorizationValue(HttpContext context)
        => HubQueryCredentialReader.ReadAuthorizationOrHubQuery(context);
}
