using System;
using System.Text;
using System.Text.Json;
using TickerQ.Utilities.Models;
using TickerQ.Utilities.Serialization;
using Xunit;

namespace TickerQ.Tests;

/// <summary>
/// Behavior tests for deterministic JSON-schema canonicalization and SHA-256 request-contract
/// fingerprinting (typed-request-contracts plan, Task 3).
///
/// Documented numeric decision (transport blocker 1): numbers are normalized BY VALUE so the .NET and
/// Node canonicalizers emit byte-identical output. Integer-valued literals within the JS safe-integer
/// range are accepted and normalized (<c>1</c>, <c>1.0</c>, and <c>1e0</c> all canonicalize to
/// <c>1</c>); fractions, non-integer exponents, and magnitudes beyond the safe range are REJECTED
/// (see <see cref="TickerCanonicalFingerprintParityTests"/>). Only object property ORDER and
/// insignificant whitespace are otherwise normalized; array order is preserved verbatim.
/// </summary>
public class TickerRequestContractFingerprintTests
{
    private static JsonElement Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    private static string Canon(string json) => JsonSchemaCanonicalizer.Canonicalize(Parse(json));

    // ---------------------------------------------------------------
    // Stage A: canonicalizer core
    // ---------------------------------------------------------------

    [Fact]
    public void Canonicalize_SortsObjectPropertyNames_Ordinally()
    {
        Assert.Equal(
            "{\"a\":1,\"b\":2,\"z\":3}",
            Canon("{\"z\":3,\"a\":1,\"b\":2}"));
    }

    [Fact]
    public void Canonicalize_IsWhitespaceIndependent()
    {
        Assert.Equal(
            Canon("{\"type\":\"object\",\"title\":\"x\"}"),
            Canon("{  \"type\" : \"object\" , \n \"title\" : \"x\" }"));
    }

    [Fact]
    public void Canonicalize_SortsNestedObjectsRecursively()
    {
        Assert.Equal(
            "{\"outer\":{\"a\":1,\"b\":2}}",
            Canon("{\"outer\":{\"b\":2,\"a\":1}}"));
    }

    [Fact]
    public void Canonicalize_PreservesArrayOrder()
    {
        Assert.Equal(
            "{\"required\":[\"b\",\"a\",\"c\"]}",
            Canon("{\"required\":[\"b\",\"a\",\"c\"]}"));
    }

    [Fact]
    public void Canonicalize_SortsObjectsInsideArrays_ButKeepsArrayOrder()
    {
        Assert.Equal(
            "{\"items\":[{\"a\":1,\"b\":2},{\"c\":3}]}",
            Canon("{\"items\":[{\"b\":2,\"a\":1},{\"c\":3}]}"));
    }

    [Fact]
    public void Canonicalize_NormalizesIntegerValuedNumbersByValue_ForCrossRuntimeParity()
    {
        // Transport blocker 1: 1, 1.0, and 1e0 all denote the same integer value and canonicalize
        // identically, because Node cannot preserve their distinct lexical spellings across JSON.parse.
        Assert.Equal("{\"n\":1}", Canon("{\"n\":1}"));
        Assert.Equal("{\"n\":1}", Canon("{\"n\":1.0}"));
        Assert.Equal("{\"n\":1}", Canon("{\"n\":1e0}"));
        Assert.Equal(Canon("{\"n\":1}"), Canon("{\"n\":1.0}"));
    }

    [Fact]
    public void Canonicalize_PreservesJsonValueSemantics()
    {
        Assert.Equal(
            "{\"b\":true,\"nil\":null,\"s\":\"v\"}",
            Canon("{\"s\":\"v\",\"b\":true,\"nil\":null}"));
    }

    [Fact]
    public void Canonicalize_UnicodePropertyNames_AreDeterministicallyOrdered()
    {
        // Ordinal (UTF-16 code-unit) ordering: ASCII 'a' (0x61) precedes 'é' (0x00E9) precedes 'あ' (0x3042).
        var once = Canon("{\"あ\":3,\"é\":2,\"a\":1}");
        var again = Canon("{\"é\":2,\"a\":1,\"あ\":3}");
        Assert.Equal(once, again);
        Assert.StartsWith("{\"a\":1,", once);
    }

    // ---------------------------------------------------------------
    // Stage B: canonicalizer rejection rules
    // ---------------------------------------------------------------

    [Theory]
    [InlineData("123")]
    [InlineData("[]")]
    [InlineData("\"str\"")]
    [InlineData("true")]
    [InlineData("null")]
    public void Canonicalize_RejectsNonObjectRoot(string json)
    {
        Assert.Throws<ArgumentException>(() => JsonSchemaCanonicalizer.CanonicalizeToUtf8(Parse(json)));
    }

    [Fact]
    public void Canonicalize_RejectsDuplicateObjectKeys()
    {
        // JsonDocument preserves duplicate names; canonicalization must reject them.
        var withDuplicates = Parse("{\"a\":1,\"a\":2}");
        Assert.Throws<ArgumentException>(() => JsonSchemaCanonicalizer.CanonicalizeToUtf8(withDuplicates));
    }

    [Fact]
    public void Canonicalize_RejectsDuplicateObjectKeys_WhenNested()
    {
        var withDuplicates = Parse("{\"outer\":{\"b\":1,\"b\":2}}");
        Assert.Throws<ArgumentException>(() => JsonSchemaCanonicalizer.CanonicalizeToUtf8(withDuplicates));
    }

    [Fact]
    public void Canonicalize_RejectsNestingBeyondMaxDepth()
    {
        // Build a structure deeper than MaxDepth. Parse with relaxed options so the element itself
        // exists; the canonicalizer's own bound must then reject it.
        var depth = JsonSchemaCanonicalizer.MaxDepth + 5;
        var sb = new StringBuilder();
        for (var i = 0; i < depth; i++) sb.Append("{\"a\":");
        sb.Append('1');
        for (var i = 0; i < depth; i++) sb.Append('}');

        using var doc = JsonDocument.Parse(sb.ToString(), new JsonDocumentOptions { MaxDepth = depth + 8 });
        var deep = doc.RootElement.Clone();

        Assert.Throws<ArgumentException>(() => JsonSchemaCanonicalizer.CanonicalizeToUtf8(deep));
    }

    [Fact]
    public void Canonicalize_AllowsSchemasWithinMaxDepth()
    {
        var sb = new StringBuilder();
        for (var i = 0; i < 20; i++) sb.Append("{\"a\":");
        sb.Append('1');
        for (var i = 0; i < 20; i++) sb.Append('}');

        var canonical = Canon(sb.ToString());
        Assert.StartsWith("{\"a\":", canonical);
    }

    // ---------------------------------------------------------------
    // Stage C: fingerprint format and sensitivity
    // ---------------------------------------------------------------

    private static string Fp(string schemaJson, string mediaType = "application/json", bool required = true)
        => TickerRequestContractFingerprint.ComputeFromSchema(Parse(schemaJson), mediaType, required);

    [Fact]
    public void Fingerprint_HasSha256Prefix_AndLowercaseHex()
    {
        var fp = Fp("{\"type\":\"object\"}");

        Assert.StartsWith("sha256:", fp);
        var hex = fp["sha256:".Length..];
        Assert.Equal(64, hex.Length); // 32 bytes -> 64 hex chars
        Assert.Equal(hex.ToLowerInvariant(), hex);
        Assert.Matches("^[0-9a-f]{64}$", hex);
    }

    [Fact]
    public void Fingerprint_IsStableAcrossPropertyOrderAndWhitespace()
    {
        var a = Fp("{\"type\":\"object\",\"title\":\"x\"}");
        var b = Fp("{ \"title\" : \"x\" ,\n \"type\":\"object\" }");

        Assert.Equal(a, b);
    }

    [Fact]
    public void Fingerprint_DiffersWhenSchemaChanges()
    {
        Assert.NotEqual(
            Fp("{\"type\":\"object\"}"),
            Fp("{\"type\":\"string\"}"));
    }

    [Fact]
    public void Fingerprint_DiffersWhenRequirednessChanges()
    {
        Assert.NotEqual(
            Fp("{\"type\":\"object\"}", required: true),
            Fp("{\"type\":\"object\"}", required: false));
    }

    [Fact]
    public void Fingerprint_DiffersWhenMediaTypeChanges()
    {
        Assert.NotEqual(
            Fp("{\"type\":\"object\"}", mediaType: "application/json"),
            Fp("{\"type\":\"object\"}", mediaType: "application/xml"));
    }

    [Fact]
    public void Fingerprint_DiffersWhenRequiredArrayOrderChanges()
    {
        // Arrays are NOT sorted: a reordered "required" array is a meaningful change.
        Assert.NotEqual(
            Fp("{\"required\":[\"a\",\"b\"]}"),
            Fp("{\"required\":[\"b\",\"a\"]}"));
    }

    [Fact]
    public void Fingerprint_MatchesWhenOnlySiblingPropertiesAreReordered()
    {
        // Guard the inverse: identical array order + reordered sibling props still match.
        Assert.Equal(
            Fp("{\"required\":[\"a\",\"b\"],\"type\":\"object\"}"),
            Fp("{\"type\":\"object\",\"required\":[\"a\",\"b\"]}"));
    }

    // ---------------------------------------------------------------
    // Stage D: TickerRequestContract integration
    // ---------------------------------------------------------------

    [Fact]
    public void Contract_NullSchema_YieldsNullFingerprint()
    {
        Assert.Null(new TickerRequestContract("Req").Fingerprint);
    }

    [Fact]
    public void Contract_NonNullSchema_YieldsNonNullFingerprint()
    {
        var contract = new TickerRequestContract("Req", schemaJson: "{\"type\":\"object\"}");

        Assert.NotNull(contract.Fingerprint);
        Assert.StartsWith("sha256:", contract.Fingerprint);
    }

    [Fact]
    public void Contract_Fingerprint_MatchesCanonicalizerComputation()
    {
        var contract = new TickerRequestContract("Req",
            schemaJson: "{\"title\":\"x\",\"type\":\"object\"}",
            required: false);

        var expected = TickerRequestContractFingerprint.ComputeFromSchema(
            Parse("{\"type\":\"object\",\"title\":\"x\"}"), "application/json", required: false);

        Assert.Equal(expected, contract.Fingerprint);
    }

    [Fact]
    public void Contract_StoresSchemaInCanonicalForm()
    {
        var contract = new TickerRequestContract("Req",
            schemaJson: "{\"z\":1,\"a\":2}");

        // The stored schema is canonical: property order is normalized ordinally.
        var raw = contract.Schema!.Value.GetRawText();
        Assert.Equal("{\"a\":2,\"z\":1}", raw);
    }

    [Fact]
    public void Contract_ReorderedSchema_ProducesEqualFingerprintAndEquality()
    {
        var a = new TickerRequestContract("Req", schemaJson: "{\"type\":\"object\",\"title\":\"x\"}");
        var b = new TickerRequestContract("Req", schemaJson: "{\"title\":\"x\",\"type\":\"object\"}");

        Assert.Equal(a.Fingerprint, b.Fingerprint);
        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void Contract_RequirednessChange_ProducesDifferentFingerprint()
    {
        var required = new TickerRequestContract("Req", schemaJson: "{\"type\":\"object\"}", required: true);
        var optional = new TickerRequestContract("Req", schemaJson: "{\"type\":\"object\"}", required: false);

        Assert.NotEqual(required.Fingerprint, optional.Fingerprint);
        Assert.NotEqual(required, optional);
    }

    [Fact]
    public void Descriptor_ContractVersionChange_ProducesDifferentRequestFingerprint()
    {
        var request = new TickerRequestContract("Req", schemaJson: "{\"type\":\"object\"}");
        var versionOne = new TickerFunctionDescriptor("Job", contractVersion: 1, request: request);
        var versionTwo = new TickerFunctionDescriptor("Job", contractVersion: 2, request: request);

        Assert.NotEqual(versionOne.Request!.Fingerprint, versionTwo.Request!.Fingerprint);
    }

    [Fact]
    public void Contract_RejectsNonObjectSchemaRoot_AtContractBoundary()
    {
        // The existing contract boundary rejects non-object schema roots (string path).
        Assert.Throws<ArgumentException>(() => new TickerRequestContract("Req", schemaJson: "[1,2,3]"));
    }
}
