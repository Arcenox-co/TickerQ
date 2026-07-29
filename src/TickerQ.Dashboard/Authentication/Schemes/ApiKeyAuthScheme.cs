using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace TickerQ.Dashboard.Authentication.Schemes;

/// <summary>
/// API-key authentication. Accepts the configured key under any of:
/// <c>Authorization: Bearer &lt;key&gt;</c>, <c>Authorization: Bearer:&lt;key&gt;</c>,
/// raw <c>Authorization: &lt;key&gt;</c>, or <c>?access_token=&lt;key&gt;</c> (SignalR).
/// </summary>
internal sealed class ApiKeyAuthScheme : AuthSchemeBase
{
    public override string Name => "ApiKey";
    public override AuthMode LegacyMode => AuthMode.ApiKey;

    public string? ApiKey { get; set; }

    /// <summary>
    /// Custom header to read the key from (e.g. <c>X-Api-Key</c>). When null
    /// the scheme reads the <c>Authorization</c> header / <c>?access_token</c>
    /// as before.
    /// </summary>
    public string? HeaderName { get; set; }

    public override Task<AuthResult> TryAuthenticateAsync(HttpContext context)
    {
        var raw = HeaderName != null
            ? context.Request.Headers[HeaderName].FirstOrDefault()
            : ReadAuthorizationValue(context);
        if (string.IsNullOrEmpty(raw)) return Task.FromResult(AuthResult.Failure("No authorization provided"));

        var token = HeaderName != null ? raw : raw switch
        {
            _ when raw.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) => raw[7..],
            _ when raw.StartsWith("Bearer:", StringComparison.OrdinalIgnoreCase) => raw[7..],
            _ => raw,
        };

        if (FixedTimeEquals(token, ApiKey)) return Task.FromResult(AuthResult.Success("api-user"));
        return Task.FromResult(AuthResult.Failure("Invalid token"));
    }

    public override string? GetChallengeHeader(HttpContext context) => HeaderName == null ? "Bearer" : null;

    private static bool FixedTimeEquals(string? a, string? b)
    {
        if (a == null || b == null) return false;
        return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.UTF8.GetBytes(a),
            System.Text.Encoding.UTF8.GetBytes(b));
    }

    public override void Validate()
    {
        if (string.IsNullOrEmpty(ApiKey))
            throw new InvalidOperationException("ApiKey is required for ApiKey authentication mode");
    }

    private static string? ReadAuthorizationValue(HttpContext context)
        => HubQueryCredentialReader.ReadAuthorizationOrHubQuery(context);
}
