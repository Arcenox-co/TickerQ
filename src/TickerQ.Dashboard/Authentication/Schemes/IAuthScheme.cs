using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace TickerQ.Dashboard.Authentication.Schemes;

/// <summary>
/// One authentication strategy. The middleware iterates the configured
/// schemes in order and stops at the first one that returns
/// <see cref="AuthResult.IsAuthenticated"/> = true. Each scheme owns its own
/// request-parsing rules (header / cookie / query string) and optionally
/// contributes a <c>WWW-Authenticate</c> value on a 401 — multiple schemes'
/// values stack as multiple headers in the same response, letting browsers
/// pick Basic while curl picks Bearer.
/// </summary>
internal interface IAuthScheme
{
    /// <summary>Display name for logs.</summary>
    string Name { get; }

    /// <summary>
    /// The legacy <see cref="AuthMode"/> this scheme represents. Used to keep
    /// <c>AuthConfig.Mode</c> / <c>AuthInfo.Mode</c> reporting the same value
    /// existing consumers expected before the multi-scheme refactor.
    /// </summary>
    AuthMode LegacyMode { get; }

    /// <summary>Try to authenticate the request. Returns failure (not an exception) on mismatch.</summary>
    Task<AuthResult> TryAuthenticateAsync(HttpContext context);

    /// <summary>
    /// Return the <c>WWW-Authenticate</c> value this scheme advertises on a
    /// 401 — e.g. <c>"Basic realm=…"</c>, <c>"Bearer"</c>, or null for none.
    /// The middleware aggregates non-null returns from every registered
    /// scheme into the response so clients can pick whichever they support.
    /// Receives the request context so schemes can tailor the challenge —
    /// e.g. Basic suppresses its browser-prompt challenge for fetch/XHR
    /// requests, where the native dialog would fight the SPA's login page.
    /// </summary>
    string? GetChallengeHeader(HttpContext context);

    /// <summary>
    /// Validate the scheme's own configuration at startup. Throw
    /// <see cref="System.InvalidOperationException"/> with an actionable
    /// message if anything is missing.
    /// </summary>
    void Validate();
}
