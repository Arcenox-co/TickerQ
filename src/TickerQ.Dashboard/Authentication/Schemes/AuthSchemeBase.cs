using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace TickerQ.Dashboard.Authentication.Schemes;

/// <summary>
/// Default scheme base. Returns no <c>WWW-Authenticate</c> value (cookie /
/// host / custom schemes don't advertise one); subclasses override
/// <see cref="GetChallengeHeader"/> when they have one to add.
/// </summary>
internal abstract class AuthSchemeBase : IAuthScheme
{
    public abstract string Name { get; }
    public abstract AuthMode LegacyMode { get; }
    public abstract Task<AuthResult> TryAuthenticateAsync(HttpContext context);

    public virtual string? GetChallengeHeader(HttpContext context) => null;

    public virtual void Validate()
    {
    }
}
