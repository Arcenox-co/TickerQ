using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace TickerQ.Dashboard.Authentication.Schemes;

/// <summary>
/// User-supplied validator. Receives the raw <c>Authorization</c> header
/// value (or <c>?access_token</c>) and returns true/false.
/// </summary>
internal sealed class CustomAuthScheme : AuthSchemeBase
{
    public override string Name => "Custom";
    public override AuthMode LegacyMode => AuthMode.Custom;

    public Func<string, bool>? Validator { get; set; }

    /// <summary>
    /// Async validator receiving the full request context — can inspect any
    /// header/cookie and await a database or external service. When set,
    /// <see cref="Validator"/> is ignored.
    /// </summary>
    public Func<HttpContext, Task<bool>>? AsyncContextValidator { get; set; }

    public override async Task<AuthResult> TryAuthenticateAsync(HttpContext context)
    {
        try
        {
            if (AsyncContextValidator != null)
            {
                return await AsyncContextValidator(context)
                    ? AuthResult.Success("custom-user")
                    : AuthResult.Failure("Custom authentication failed");
            }

            var raw = ReadAuthorizationValue(context);
            if (string.IsNullOrEmpty(raw)) return AuthResult.Failure("No authorization provided");

            if (Validator?.Invoke(raw) == true)
                return AuthResult.Success("custom-user");
            return AuthResult.Failure("Custom authentication failed");
        }
        catch
        {
            return AuthResult.Failure("Custom authentication error");
        }
    }

    public override void Validate()
    {
        if (Validator == null && AsyncContextValidator == null)
            throw new InvalidOperationException("CustomValidator is required for Custom authentication mode");
    }

    private static string? ReadAuthorizationValue(HttpContext context)
        => HubQueryCredentialReader.ReadAuthorizationOrHubQuery(context);
}
