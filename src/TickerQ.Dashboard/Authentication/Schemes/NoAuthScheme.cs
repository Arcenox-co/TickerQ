using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace TickerQ.Dashboard.Authentication.Schemes;

/// <summary>
/// Sentinel scheme that always succeeds — used when no auth is configured.
/// Its presence in the scheme list flips <c>IsEnabled</c> to false so the
/// dashboard middleware skips wiring the auth pipeline entirely.
/// </summary>
internal sealed class NoAuthScheme : AuthSchemeBase
{
    public override string Name => "None";
    public override AuthMode LegacyMode => AuthMode.None;

    public override Task<AuthResult> TryAuthenticateAsync(HttpContext context)
        => Task.FromResult(AuthResult.Success("anonymous"));
}
