using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TickerQ.Dashboard.Authentication.Jwt;

/// <summary>
/// Hand-rolled HS256 JWT issuer + validator. Compact (~150 lines), AOT-safe
/// (no reflection / no <c>System.IdentityModel.Tokens.Jwt</c> dependency),
/// and limited to the exact subset the dashboard needs: `sub`, `iat`, `exp`,
/// optional `name`. Bigger setups should use Host auth and let the host
/// validate full-strength JWTs.
/// </summary>
internal sealed class JwtTokenIssuer
{
    private readonly byte[] _signingKey;
    private readonly TimeSpan _accessTokenLifetime;
    private readonly Func<DateTime> _utcNow;

    public string Issuer { get; }
    public string Audience { get; }

    public JwtTokenIssuer(byte[] signingKey, string issuer, string audience, TimeSpan accessTokenLifetime, Func<DateTime>? utcNow = null)
    {
        if (signingKey == null || signingKey.Length < 32)
            throw new ArgumentException("Signing key must be at least 32 bytes for HS256.", nameof(signingKey));

        _signingKey = signingKey;
        Issuer = issuer;
        Audience = audience;
        _accessTokenLifetime = accessTokenLifetime;
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
    }

    /// <summary>Issue a new access token for <paramref name="username"/>.</summary>
    public string IssueAccessToken(string username)
    {
        var now = _utcNow();
        var claims = new JwtClaims
        {
            Sub = username,
            Name = username,
            Iss = Issuer,
            Aud = Audience,
            Iat = ToUnixSeconds(now),
            Exp = ToUnixSeconds(now + _accessTokenLifetime),
        };

        var header = """{"alg":"HS256","typ":"JWT"}"""u8;
        var payload = JsonSerializer.SerializeToUtf8Bytes(claims, JwtJsonContext.Default.JwtClaims);

        var headerSegment = Base64UrlEncode(header);
        var payloadSegment = Base64UrlEncode(payload);
        var signingInput = $"{headerSegment}.{payloadSegment}";
        var signature = SignHs256(signingInput, _signingKey);

        return $"{signingInput}.{signature}";
    }

    /// <summary>Validate <paramref name="token"/> and extract the subject (username) on success.</summary>
    public bool TryValidate(string token, out string username)
    {
        username = string.Empty;
        if (string.IsNullOrWhiteSpace(token)) return false;

        var parts = token.Split('.');
        if (parts.Length != 3) return false;

        // Signature check (timing-safe).
        var expected = SignHs256($"{parts[0]}.{parts[1]}", _signingKey);
        if (!FixedTimeEquals(parts[2], expected)) return false;

        // Payload claims.
        JwtClaims? claims;
        try
        {
            var payloadBytes = Base64UrlDecode(parts[1]);
            claims = JsonSerializer.Deserialize(payloadBytes, JwtJsonContext.Default.JwtClaims);
        }
        catch
        {
            return false;
        }

        if (claims == null) return false;
        if (!string.Equals(claims.Iss, Issuer, StringComparison.Ordinal)) return false;
        if (!string.Equals(claims.Aud, Audience, StringComparison.Ordinal)) return false;

        var nowSec = ToUnixSeconds(_utcNow());
        if (claims.Exp < nowSec) return false;
        if (claims.Iat > nowSec + 60) return false; // tiny clock-skew tolerance going forward

        username = claims.Sub ?? string.Empty;
        return !string.IsNullOrEmpty(username);
    }

    private static long ToUnixSeconds(DateTime utc) => new DateTimeOffset(utc, TimeSpan.Zero).ToUnixTimeSeconds();

    private static string SignHs256(string input, byte[] key)
    {
        var bytes = Encoding.UTF8.GetBytes(input);
        var sig = HMACSHA256.HashData(key, bytes);
        return Base64UrlEncode(sig);
    }

    private static string Base64UrlEncode(ReadOnlySpan<byte> data)
    {
        // Base64 → strip padding, swap + and / for - and _.
        var b64 = Convert.ToBase64String(data);
        return b64.TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static byte[] Base64UrlDecode(string segment)
    {
        var s = segment.Replace('-', '+').Replace('_', '/');
        var padding = (4 - s.Length % 4) % 4;
        return Convert.FromBase64String(s + new string('=', padding));
    }

    private static bool FixedTimeEquals(string a, string b)
    {
        // Length check first (not strictly constant-time, but the secret never
        // varies in length so this leaks nothing).
        if (a.Length != b.Length) return false;
        return CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(a), Encoding.ASCII.GetBytes(b));
    }
}

internal sealed class JwtClaims
{
    [JsonPropertyName("sub")] public string? Sub { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("iss")] public string? Iss { get; set; }
    [JsonPropertyName("aud")] public string? Aud { get; set; }
    [JsonPropertyName("iat")] public long Iat { get; set; }
    [JsonPropertyName("exp")] public long Exp { get; set; }
}

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(JwtClaims))]
internal partial class JwtJsonContext : JsonSerializerContext { }
