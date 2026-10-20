using System;
using System.Linq;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

namespace TickerQ.Dashboard.Authentication;

/// <summary>Opaque history keys, separate from mutable/non-unique display names.</summary>
internal static class DashboardUserIdentity
{
    internal static string? FromIdentity(ClaimsIdentity? identity)
    {
        if (identity?.IsAuthenticated != true) return null;
        var subject = identity.FindFirst("sub") ?? identity.FindFirst(ClaimTypes.NameIdentifier);
        return subject == null ? null : Create("host:" + identity.AuthenticationType,
            identity.FindFirst("iss")?.Value ?? subject.Issuer, subject.Value);
    }

    internal static string? FromPrincipal(ClaimsPrincipal principal)
        => FromIdentity(principal.Identities.FirstOrDefault(identity => identity.IsAuthenticated));

    internal static string? Create(string scheme, string issuer, string? subject)
    {
        if (string.IsNullOrWhiteSpace(subject)) return null;
        // Length prefixes avoid ambiguous identifiers, without putting subjects into browser storage keys.
        var value = $"{scheme.Length}:{scheme}{issuer.Length}:{issuer}{subject.Length}:{subject}";
        return "v1:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    }
}
