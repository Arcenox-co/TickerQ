using System;
using System.Collections.Generic;
using System.Text.Json;
using TickerQ.Utilities.Models;
using Xunit;
// ReSharper disable once RedundantUsingDirective
using System.Collections.ObjectModel;

namespace TickerQ.Tests;

/// <summary>
/// Behavior tests for the canonical, immutable request-contract value objects
/// (<see cref="TickerFunctionDescriptor"/>, <see cref="TickerRequestContract"/>,
/// <see cref="TickerRequestExample"/>). These freeze the wire-shape semantics
/// described by the typed-request-contracts plan: value equality, immutability,
/// ordered/named examples, and request-less functions expressed as Request == null.
/// </summary>
public class TickerFunctionDescriptorTests
{
    // ---------------------------------------------------------------
    // Constants / defaults
    // ---------------------------------------------------------------
    [Fact]
    public void Constants_ExposeCanonicalDefaults()
    {
        Assert.Equal(1, TickerRequestContractConstants.InitialContractVersion);
        Assert.Equal("application/json", TickerRequestContractConstants.DefaultMediaType);
        Assert.Equal("https://json-schema.org/draft/2020-12/schema", TickerRequestContractConstants.SchemaDialect2020_12);
    }

    // ---------------------------------------------------------------
    // Descriptor: request-less function uses Request == null
    // ---------------------------------------------------------------
    [Fact]
    public void Descriptor_RequestLess_HasNullRequest_AndVersionOne()
    {
        var descriptor = new TickerFunctionDescriptor("Jobs.Cleanup");

        Assert.Equal("Jobs.Cleanup", descriptor.FunctionName);
        Assert.Null(descriptor.Request);
        Assert.Equal(TickerRequestContractConstants.InitialContractVersion, descriptor.ContractVersion);
    }

    [Fact]
    public void Descriptor_NullFunctionName_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new TickerFunctionDescriptor(null!));
    }

    // ---------------------------------------------------------------
    // Contract defaults
    // ---------------------------------------------------------------
    [Fact]
    public void Contract_Defaults_AreCanonical()
    {
        var contract = new TickerRequestContract("Orders.ProcessOrderRequest");

        Assert.Equal("Orders.ProcessOrderRequest", contract.TypeName);
        Assert.Equal(TickerRequestContractConstants.DefaultMediaType, contract.MediaType);
        Assert.True(contract.Required);
        Assert.Equal(TickerRequestContractConstants.SchemaDialect2020_12, contract.SchemaDialect);
        Assert.Null(contract.Schema);
        Assert.Null(contract.Fingerprint);
        Assert.Empty(contract.Examples);
    }

    // ---------------------------------------------------------------
    // Value equality
    // ---------------------------------------------------------------
    [Fact]
    public void Descriptor_ValueEquality_EqualWhenContractsMatch()
    {
        var a = new TickerFunctionDescriptor("Orders.Process",
            request: new TickerRequestContract("Orders.ProcessOrderRequest"));
        var b = new TickerFunctionDescriptor("Orders.Process",
            request: new TickerRequestContract("Orders.ProcessOrderRequest"));

        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void Descriptor_ValueEquality_DiffersWhenRequiredDiffers()
    {
        var a = new TickerFunctionDescriptor("Orders.Process",
            request: new TickerRequestContract("Req", required: true));
        var b = new TickerFunctionDescriptor("Orders.Process",
            request: new TickerRequestContract("Req", required: false));

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Descriptor_RequestLess_DiffersFromTypedRequest()
    {
        var requestLess = new TickerFunctionDescriptor("Orders.Process");
        var typed = new TickerFunctionDescriptor("Orders.Process",
            request: new TickerRequestContract("Req"));

        Assert.NotEqual(requestLess, typed);
    }

    // ---------------------------------------------------------------
    // Examples: named, ordered, immutable
    // ---------------------------------------------------------------
    [Fact]
    public void Contract_Examples_AreOrdered_OrderMatters()
    {
        var first = new TickerRequestContract("Req", examples: new[]
        {
            new TickerRequestExample("a", "A", "{}"),
            new TickerRequestExample("b", "B", "{}")
        });
        var reversed = new TickerRequestContract("Req", examples: new[]
        {
            new TickerRequestExample("b", "B", "{}"),
            new TickerRequestExample("a", "A", "{}")
        });

        Assert.NotEqual(first, reversed);
    }

    [Fact]
    public void Contract_Examples_AreImmutable_DefensiveCopy()
    {
        var mutable = new List<TickerRequestExample>
        {
            new("default", "Generated example", "{\"orderId\":\"string\"}")
        };
        var contract = new TickerRequestContract("Req", examples: mutable);

        mutable.Add(new TickerRequestExample("extra", "Extra", "{}"));

        Assert.Single(contract.Examples);
        Assert.Equal("default", contract.Examples[0].Key);
    }

    [Fact]
    public void Contract_DuplicateExampleKeys_Throws()
    {
        Assert.Throws<ArgumentException>(() => new TickerRequestContract("Req", examples: new[]
        {
            new TickerRequestExample("dup", "One", "{}"),
            new TickerRequestExample("dup", "Two", "{}")
        }));
    }

    [Fact]
    public void Example_ValueEquality()
    {
        Assert.Equal(
            new TickerRequestExample("k", "s", "{\"x\":1}"),
            new TickerRequestExample("k", "s", "{\"x\":1}"));
        Assert.NotEqual(
            new TickerRequestExample("k", "s", "{\"x\":1}"),
            new TickerRequestExample("k", "s", "{\"x\":2}"));
    }

    // ===============================================================
    // Defect 2: Examples must be genuinely immutable — not castable to a
    // mutable array/list that can alter observed contents.
    // ===============================================================
    [Fact]
    public void Contract_Examples_NotCastableToMutableArray()
    {
        var contract = new TickerRequestContract("Req", examples: new[]
        {
            new TickerRequestExample("a", "A", "{}")
        });

        Assert.False(contract.Examples is TickerRequestExample[]);
        var asList = Assert.IsAssignableFrom<IList<TickerRequestExample>>(contract.Examples);
        Assert.Throws<NotSupportedException>(() =>
            asList[0] = new TickerRequestExample("b", "B", "{}"));

        // The exposed contents remain what was constructed.
        Assert.Single(contract.Examples);
        Assert.Equal("a", contract.Examples[0].Key);
    }

    // ===============================================================
    // Defect 3: public wire invariants
    // ===============================================================
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Descriptor_EmptyOrWhitespaceFunctionName_Throws(string name)
    {
        Assert.Throws<ArgumentException>(() => new TickerFunctionDescriptor(name));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Descriptor_NonPositiveContractVersion_Throws(int version)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new TickerFunctionDescriptor("Jobs.Cleanup", contractVersion: version));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Contract_EmptyTypeName_Throws(string typeName)
    {
        Assert.Throws<ArgumentException>(() => new TickerRequestContract(typeName));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Contract_EmptyMediaType_Throws(string mediaType)
    {
        Assert.Throws<ArgumentException>(() => new TickerRequestContract("Req", mediaType: mediaType));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Contract_EmptySchemaDialect_Throws(string schemaDialect)
    {
        Assert.Throws<ArgumentException>(() => new TickerRequestContract("Req", schemaDialect: schemaDialect));
    }

    [Theory]
    [InlineData("https://json-schema.org/draft-07/schema")]
    [InlineData("http://json-schema.org/draft/2019-09/schema")]
    [InlineData("urn:custom:dialect")]
    public void Contract_UnsupportedSchemaDialect_Throws(string schemaDialect)
    {
        // Draft 2020-12 is the sole supported policy; every other dialect is rejected at the boundary.
        Assert.Throws<ArgumentException>(() =>
            new TickerRequestContract("Req", schemaDialect: schemaDialect, schemaJson: "{\"type\":\"object\"}"));
    }

    [Fact]
    public void Contract_ConflictingEmbeddedSchema_Throws()
    {
        // Embedded $schema contradicting the (supported) declared dialect must be rejected before publication.
        var schemaJson = "{\"$schema\":\"https://json-schema.org/draft-07/schema\",\"type\":\"object\"}";
        Assert.Throws<ArgumentException>(() =>
            new TickerRequestContract("Req", schemaJson: schemaJson));
    }

    [Fact]
    public void Contract_NonStringEmbeddedSchemaDialect_Throws()
    {
        var error = Assert.Throws<ArgumentException>(() =>
            new TickerRequestContract("Req", schemaJson: "{\"$schema\":123,\"type\":\"object\"}"));

        Assert.Contains("Embedded $schema", error.Message);
    }

    [Fact]
    public void Contract_EmbeddedSchemaMatchingCanonicalDialect_IsAccepted()
    {
        var schemaJson =
            "{\"$schema\":\"" + TickerRequestContractConstants.SchemaDialect2020_12 + "\",\"type\":\"object\"}";
        var contract = new TickerRequestContract("Req", schemaJson: schemaJson);

        Assert.Equal(TickerRequestContractConstants.SchemaDialect2020_12, contract.SchemaDialect);
        Assert.NotNull(contract.Fingerprint);
    }

    [Fact]
    public void Contract_EmbeddedCanonicalDialect_IsPartOfIdentity()
    {
        // The embedded dialect is an authoritative identity input: a schema declaring the canonical
        // $schema is a distinct contract (distinct fingerprint) from the same schema without it, so a
        // dialect-only descriptor change is visible to execution drift.
        var withDialect = new TickerRequestContract("Req",
            schemaJson: "{\"$schema\":\"" + TickerRequestContractConstants.SchemaDialect2020_12 + "\",\"type\":\"object\"}");
        var withoutDialect = new TickerRequestContract("Req", schemaJson: "{\"type\":\"object\"}");

        Assert.NotEqual(withDialect.Fingerprint, withoutDialect.Fingerprint);
        Assert.NotEqual(withDialect, withoutDialect);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Example_EmptyKey_Throws(string key)
    {
        Assert.Throws<ArgumentException>(() => new TickerRequestExample(key, "summary", "{}"));
    }

    [Fact]
    public void Example_NullValue_Throws()
    {
        Assert.Throws<ArgumentException>(() => new TickerRequestExample("k", "s", null));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{unclosed")]
    [InlineData("")]
    public void Example_InvalidJsonValue_Throws(string value)
    {
        Assert.Throws<ArgumentException>(() => new TickerRequestExample("k", "s", value));
    }

    [Fact]
    public void Example_NullSummary_IsAllowed()
    {
        var example = new TickerRequestExample("k", null, "{}");
        Assert.Null(example.Summary);
    }

    [Theory]
    [InlineData("123")]        // number root
    [InlineData("[]")]         // array root
    [InlineData("\"str\"")]    // string root
    [InlineData("not json")]   // invalid
    public void Contract_SchemaJson_MustBeJsonObject_WhenNonNull(string schemaJson)
    {
        Assert.Throws<ArgumentException>(() =>
            new TickerRequestContract("Req", schemaJson: schemaJson));
    }

    [Fact]
    public void Contract_SchemaJson_ValidObject_IsAccepted()
    {
        var contract = new TickerRequestContract("Req",
            schemaJson: "{\"type\":\"object\",\"additionalProperties\":false}");
        Assert.NotNull(contract.Schema);
        Assert.Equal(JsonValueKind.Object, contract.Schema!.Value.ValueKind);
    }

    // ===============================================================
    // C: wire values are embedded JsonElement, not escaped strings
    // ===============================================================
    [Fact]
    public void Example_Value_IsEmbeddedJsonElement()
    {
        var example = new TickerRequestExample("k", "s", "{\"a\":1}");

        Assert.Equal(JsonValueKind.Object, example.Value.ValueKind);
        Assert.Equal(1, example.Value.GetProperty("a").GetInt32());
    }

    [Fact]
    public void Example_Value_SupportsAnyJsonValue()
    {
        Assert.Equal(JsonValueKind.Array, new TickerRequestExample("k", null, "[1,2]").Value.ValueKind);
        Assert.Equal(JsonValueKind.String, new TickerRequestExample("k", null, "\"hi\"").Value.ValueKind);
        Assert.Equal(JsonValueKind.Number, new TickerRequestExample("k", null, "42").Value.ValueKind);
    }

    [Fact]
    public void Contract_Schema_IsNullableObjectOnlyJsonElement()
    {
        Assert.Null(new TickerRequestContract("Req").Schema);
        var withSchema = new TickerRequestContract("Req", schemaJson: "{\"type\":\"object\"}");
        Assert.Equal(JsonValueKind.Object, withSchema.Schema!.Value.ValueKind);
    }

    // ===============================================================
    // C: structural value equality (pre-canonicalization: whitespace-insensitive)
    // ===============================================================
    [Fact]
    public void Example_StructuralEquality_IgnoresWhitespace()
    {
        var a = new TickerRequestExample("k", "s", "{\"a\":1,\"b\":2}");
        var b = new TickerRequestExample("k", "s", "{ \"a\" : 1 , \"b\" : 2 }");

        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void Example_StructuralEquality_DiffersOnValue()
    {
        Assert.NotEqual(
            new TickerRequestExample("k", "s", "{\"a\":1}"),
            new TickerRequestExample("k", "s", "{\"a\":2}"));
    }

    [Fact]
    public void Example_StructuralEquality_IgnoresObjectPropertyOrder()
    {
        var a = new TickerRequestExample("k", "s", "{\"a\":1,\"b\":2}");
        var b = new TickerRequestExample("k", "s", "{\"b\":2,\"a\":1}");

        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void Contract_StructuralEquality_IgnoresSchemaPropertyOrder()
    {
        var a = new TickerRequestContract("Req", schemaJson: "{\"type\":\"object\",\"title\":\"x\"}");
        var b = new TickerRequestContract("Req", schemaJson: "{\"title\":\"x\",\"type\":\"object\"}");

        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void Contract_StructuralEquality_SchemaWhitespaceIgnored()
    {
        var a = new TickerRequestContract("Req", schemaJson: "{\"type\":\"object\"}");
        var b = new TickerRequestContract("Req", schemaJson: "{ \"type\" : \"object\" }");

        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    // ===============================================================
    // B: descriptor carries wire scheduling metadata (Priority, CronExpression)
    // ===============================================================
    [Fact]
    public void Descriptor_CarriesPriorityAndCron()
    {
        var descriptor = new TickerFunctionDescriptor("Jobs.Cleanup",
            priority: TickerQ.Utilities.Enums.TickerTaskPriority.High,
            cronExpression: "0 0 * * *");

        Assert.Equal(TickerQ.Utilities.Enums.TickerTaskPriority.High, descriptor.Priority);
        Assert.Equal("0 0 * * *", descriptor.CronExpression);
    }

    [Fact]
    public void Descriptor_Equality_IncludesPriorityAndCron()
    {
        var a = new TickerFunctionDescriptor("F", priority: TickerQ.Utilities.Enums.TickerTaskPriority.High);
        var b = new TickerFunctionDescriptor("F", priority: TickerQ.Utilities.Enums.TickerTaskPriority.Low);
        Assert.NotEqual(a, b);
    }
}
