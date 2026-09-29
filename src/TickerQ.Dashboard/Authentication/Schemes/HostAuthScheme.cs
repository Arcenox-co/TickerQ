using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace TickerQ.Dashboard.Authentication.Schemes;

/// <summary>
/// Delegates to the host application's authentication / authorization
/// pipeline. The host is expected to populate <see cref="HttpContext.User"/>
/// via its own scheme (cookies, JWT, OIDC, etc.); this scheme just inspects
/// the result and optionally enforces a named authorization policy.
/// </summary>
internal sealed class HostAuthScheme : AuthSchemeBase
{
    public override string Name => "Host";
    public override AuthMode LegacyMode => AuthMode.Host;

    /// <summary>Optional named policy to enforce on top of authenticated-user check.</summary>
    public string? AuthorizationPolicy { get; set; }

    public override async Task<AuthResult> TryAuthenticateAsync(HttpContext context)
    {
        // A ClaimsPrincipal may carry several identities (e.g. host stacks a
        // secondary scheme on top of a primary one). Authenticate against any
        // identity that is actually authenticated rather than only the primary
        // one exposed via context.User.Identity.
        var authenticatedIdentity = context.User.Identities
            .FirstOrDefault(identity => identity.IsAuthenticated);

        if (authenticatedIdentity is null)
            return AuthResult.Failure("Host authentication required");

        if (!string.IsNullOrEmpty(AuthorizationPolicy))
        {
            var authz = context.RequestServices.GetRequiredService<IAuthorizationService>();
            var policyResult = await authz.AuthorizeAsync(context.User, context, AuthorizationPolicy);
            if (!policyResult.Succeeded)
                return AuthResult.Failure("Host authorization policy not satisfied");
        }

        // Derive the username from the authenticated identity, never from an
        // unauthenticated one, falling back to a generic host user when the
        // authenticated identity carries no name.
        return AuthResult.Success(authenticatedIdentity.Name ?? "host-user");
    }
}
