using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using TickerQ.Dashboard.Authentication.Jwt;

namespace TickerQ.Dashboard.Authentication.Schemes;

/// <summary>
/// JWT Bearer authentication. Reads <c>Authorization: Bearer &lt;token&gt;</c>
/// (or <c>?access_token=&lt;token&gt;</c> for SignalR WebSocket upgrades),
/// validates the HS256 signature and the <c>iss</c>/<c>aud</c>/<c>exp</c>
/// claims using the dashboard's own <see cref="JwtTokenIssuer"/>.
/// </summary>
/// <remarks>
/// The token's <c>sub</c> claim becomes the authenticated username. The
/// dashboard does not validate the user still exists at request time — token
/// lifetime is the only revocation mechanism in this Phase 2 design.
/// </remarks>
internal sealed class JwtBearerScheme : AuthSchemeBase
{
    public override string Name => "JwtBearer";
    public override AuthMode LegacyMode => AuthMode.Jwt;

    public JwtTokenIssuer? Issuer { get; set; }
    public IUserStore? Users { get; set; }

    public override Task<AuthResult> TryAuthenticateAsync(HttpContext context)
    {
        if (Issuer == null) return Task.FromResult(AuthResult.Failure("JWT issuer not configured"));

        var raw = ReadAuthorizationValue(context);
        if (string.IsNullOrEmpty(raw)) return Task.FromResult(AuthResult.Failure("No authorization provided"));

        var token = raw.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? raw[7..] : raw;

        return Issuer.TryValidate(token, out var username)
            ? Task.FromResult(AuthResult.Success(username))
            : Task.FromResult(AuthResult.Failure("Invalid or expired token"));
    }

    public override string? GetChallengeHeader(HttpContext context) => "Bearer";

    public override void Validate()
    {
        // Issuer is bound by JwtBearerSchemeBinder during app startup AFTER
        // AuthConfig.Validate() runs, so it is intentionally not checked here.
        // TryAuthenticateAsync guards against null at request time.
        if (Users == null) throw new InvalidOperationException("JwtBearer scheme is missing a configured IUserStore.");
    }

    private static string? ReadAuthorizationValue(HttpContext context)
        => HubQueryCredentialReader.ReadAuthorizationOrHubQuery(context);
}
