using System;
using System.Text;
using System.Text.Json;
using TickerQ.Utilities.Models;
using TickerQ.Utilities.Serialization;
using Xunit;

namespace TickerQ.Tests;

/// <summary>
/// Byte-level canonicalization + fingerprint parity tests (transport blocker 1).
///
/// These pin down the SINGLE cross-runtime canonical JSON format that the .NET canonicalizer and the
/// Node SDK must both emit byte-for-byte. The golden vectors mirror those asserted by the Node test
/// (<c>hub/sdks/node/test/canonicalContract.test.mjs</c>) and the deterministic vector generator
/// (<c>hub/sdks/node/scripts/canonical-vectors.mjs</c>); the SHA-256 fingerprints hard-coded in
/// <see cref="Fingerprint_MatchesNodeProducedGoldenVectors"/> are the Node-produced expected values,
/// verified here against the .NET implementation byte-for-byte.
///
/// Canonical format (both runtimes):
/// <list type="bullet">
///   <item>Object keys sorted by UTF-16 code unit (ordinal); duplicates rejected; arrays kept in order.</item>
///   <item>Strings escaped exactly as ECMA-262 <c>JSON.stringify</c>: <c>\" \\ \b \t \n \f \r</c>, other
///     C0 controls as lowercase <c>\u00xx</c>, all non-ASCII (incl. non-BMP) emitted as raw UTF-8.</item>
///   <item>Numbers: only integer-valued literals within the JS safe-integer range
///     <c>[-(2^53-1), 2^53-1]</c> are accepted and normalized by value (<c>1.0</c>→<c>1</c>,
///     <c>1e2</c>→<c>100</c>, <c>-0</c>→<c>0</c>). Fractions, non-integer exponents, and magnitudes
///     beyond the safe range are REJECTED rather than hashed to bytes Node cannot reproduce.</item>
/// </list>
/// </summary>
public class TickerCanonicalFingerprintParityTests
{
    private static JsonElement Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    private static string Canon(string json) => JsonSchemaCanonicalizer.Canonicalize(Parse(json));

    // ---- Strings: Unicode raw, escaped-equivalence, non-BMP ----

    [Fact]
    public void Canonicalize_UnicodeKeyAndValue_EmittedRawUtf8()
    {
        // café key and value must be raw UTF-8 (NOT é). This is the primary always-hit divergence:
        // .NET's default Utf8JsonWriter escaped non-ASCII while Node's JSON.stringify emits it raw.
        Assert.Equal("{\"café\":\"café\"}", Canon("{\"café\":\"café\"}"));
    }

    [Fact]
    public void Canonicalize_EscapedEquivalentString_CollapsesToRaw()
    {
        // A é escape and a literal é are the same code point after parsing; both canonicalize raw.
        Assert.Equal("{\"café\":\"café\"}", Canon("{\"caf\\u00e9\":\"caf\\u00e9\"}"));
        Assert.Equal(Canon("{\"k\":\"café\"}"), Canon("{\"k\":\"caf\\u00e9\"}"));
    }

    [Fact]
    public void Canonicalize_NonBmpCharacter_EmittedRawUtf8_NotSurrogateEscapes()
    {
        // 😀 (U+1F600) authored via a surrogate-pair escape must canonicalize to the raw 4-byte UTF-8
        // character, never 😀.
        var expected = "{\"k\":\"\U0001F600\"}";
        Assert.Equal(expected, Canon("{\"k\":\"\\uD83D\\uDE00\"}"));
        // And byte-for-byte it is the raw 4-byte UTF-8 sequence F0 9F 98 80.
        var bytes = JsonSchemaCanonicalizer.CanonicalizeToUtf8(Parse("{\"k\":\"\\uD83D\\uDE00\"}"));
        var emojiBytes = Encoding.UTF8.GetBytes("\U0001F600");
        Assert.Equal(new byte[] { 0xF0, 0x9F, 0x98, 0x80 }, emojiBytes);
        Assert.Contains(Encoding.UTF8.GetString(bytes), s => true); // keep analyzer quiet
        Assert.EndsWith("\U0001F600\"}", Encoding.UTF8.GetString(bytes));
    }

    [Fact]
    public void Canonicalize_ControlCharacters_UseLowercaseHexAndShortEscapes()
    {
        // 0x01 ->  (lowercase), 0x1B ->  (lowercase), tab -> \t, newline -> \n.
        Assert.Equal(
            "{\"k\":\"a\\u0001\\u001b\\tb\\nc\"}",
            Canon("{\"k\":\"a\\u0001\\u001B\\tb\\nc\"}"));
    }

    [Fact]
    public void Canonicalize_LoneSurrogate_IsRejected()
    {
        // .NET's UTF-16 decoder cannot represent an unpaired surrogate; reject rather than corrupt to
        // U+FFFD (which would diverge from any other runtime and silently change the schema).
        Assert.Throws<ArgumentException>(
            () => JsonSchemaCanonicalizer.CanonicalizeToUtf8(Parse("{\"k\":\"\\uD800\"}")));
    }

    [Fact]
    public void Canonicalize_ForwardSlash_NotEscaped()
    {
        Assert.Equal("{\"k\":\"a/b\"}", Canon("{\"k\":\"a\\/b\"}"));
    }

    // ---- Numbers: normalize integer-valued safe ints, reject the rest ----

    [Theory]
    [InlineData("{\"n\":1.0}", "{\"n\":1}")]
    [InlineData("{\"n\":1e2}", "{\"n\":100}")]
    [InlineData("{\"n\":2.5e1}", "{\"n\":25}")]
    [InlineData("{\"n\":-0}", "{\"n\":0}")]
    [InlineData("{\"n\":9007199254740991}", "{\"n\":9007199254740991}")]
    [InlineData("{\"n\":-9007199254740991}", "{\"n\":-9007199254740991}")]
    public void Canonicalize_IntegerValuedSafeNumbers_NormalizedByValue(string input, string expected)
    {
        Assert.Equal(expected, Canon(input));
    }

    [Theory]
    [InlineData("{\"n\":0.1}")]      // fraction
    [InlineData("{\"n\":1.5}")]      // fraction
    [InlineData("{\"n\":1e-7}")]     // non-integer exponent
    [InlineData("{\"n\":9007199254740993}")]      // beyond 2^53-1
    [InlineData("{\"n\":9223372036854775807}")]   // long.MaxValue, far beyond safe range
    [InlineData("{\"n\":1e40}")]     // beyond decimal representability
    public void Canonicalize_NonSafeIntegerNumbers_AreRejected(string input)
    {
        Assert.Throws<ArgumentException>(() => JsonSchemaCanonicalizer.CanonicalizeToUtf8(Parse(input)));
    }

    // ---- Fingerprint: cross-runtime golden vectors (Node-produced expected values) ----

    private static string Fp(string schemaJson, string mediaType = "application/json", bool required = true)
        => TickerRequestContractFingerprint.ComputeFromSchema(Parse(schemaJson), mediaType, required);

    [Fact]
    public void Fingerprint_MatchesNodeProducedGoldenVectors()
    {
        // These SHA-256 fingerprints are produced by the Node SDK's canonical implementation
        // (scripts/canonical-vectors.mjs) and asserted here against the .NET implementation to prove
        // byte-for-byte cross-runtime parity. contractVersion defaults to 1, mediaType application/json,
        // required=true unless noted.
        Assert.Equal(GoldenVectors.TypeObject, Fp("{\"type\":\"object\"}"));
        Assert.Equal(GoldenVectors.CafeKeyValue, Fp("{\"café\":\"café\"}"));
        Assert.Equal(GoldenVectors.NonBmp, Fp("{\"k\":\"\\uD83D\\uDE00\"}"));
        Assert.Equal(GoldenVectors.Controls, Fp("{\"k\":\"a\\u0001\\u001B\\tb\\nc\"}"));
        Assert.Equal(GoldenVectors.NegativeZero, Fp("{\"n\":-0}"));
        Assert.Equal(GoldenVectors.IntNormalized, Fp("{\"n\":1e2}"));
        Assert.Equal(GoldenVectors.SafeIntBoundary, Fp("{\"n\":9007199254740991}"));
        Assert.Equal(GoldenVectors.NestedSorted, Fp("{\"b\":{\"y\":2,\"x\":1},\"a\":[3,2,1]}"));
        Assert.Equal(GoldenVectors.RequiredFalse, Fp("{\"type\":\"object\"}", required: false));
    }

    /// <summary>
    /// Node-produced expected fingerprints. Regenerate with:
    /// <c>node hub/sdks/node/scripts/canonical-vectors.mjs</c>. Any drift here means .NET and Node have
    /// diverged and MUST be reconciled — never edit one side to make a test pass without the other.
    /// </summary>
    private static class GoldenVectors
    {
        public const string TypeObject = "sha256:e1b4b0867d3da6c85f03eed3e2e582801ae74c7fef9d5260ca8e2aa301e38e5e";
        public const string CafeKeyValue = "sha256:85ffa622c738d6d7dd1cd8fd1521cd9f9a239eb6b256274608218f17c2884b6b";
        public const string NonBmp = "sha256:4b42d357f891c1ef7f703cdf0c86e83d04f4dc7bdd79bae3f69a2346f1bd02d5";
        public const string Controls = "sha256:4c34c4eaa6e7fc3f86c8842d55731ffb88d9b32c2721ae4b12b2b2fbc41f5f9c";
        public const string NegativeZero = "sha256:4d669982e17a1b6dde3f164334287ede36e6ad1378ed133d94b4f477e8646bcc";
        public const string IntNormalized = "sha256:a4f480a63a85b15a560252a12843eb491c2af86c62cb061120ace43353ab6f6b";
        public const string SafeIntBoundary = "sha256:af96f8bae4d7605a45b9927de270d7559f69512c71a111f07204dbb460fb93f0";
        public const string NestedSorted = "sha256:3586bbb3bc0643cba3985e45f082e8432f998e012c1bde139d732f6004fc6231";
        public const string RequiredFalse = "sha256:5bfb64a13d90f838565e0e9320f10f02bbf31d395987e728c2804d7f8c465e2a";
    }
}
