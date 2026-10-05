using Microsoft.CodeAnalysis;

namespace TickerQ.SourceGenerator.Tests;

public sealed class SchemaGenerationEdgeTests
{
    private static SchemaTestHarness.GenResult Run(string declarations, string properties)
    {
        var source = $$"""
using System.Threading;
using System.Threading.Tasks;
using TickerQ.Utilities.Base;

namespace TestApp
{
    {{declarations}}
    public class ReqModel { {{properties}} }
    public class Jobs
    {
        [TickerFunction("Fn")]
        public Task Fn(TickerFunctionContext<ReqModel> context, CancellationToken ct) => Task.CompletedTask;
    }
}
""";
        return SchemaTestHarness.Run(source);
    }

    [Fact]
    public void PropertyLevelCustomConverter_EmitsTq013AndNoSchema()
    {
        var result = Run(
            "public sealed class ValueConverter : System.Text.Json.Serialization.JsonConverter<int> { public override int Read(ref System.Text.Json.Utf8JsonReader reader, System.Type type, System.Text.Json.JsonSerializerOptions options) => 0; public override void Write(System.Text.Json.Utf8JsonWriter writer, int value, System.Text.Json.JsonSerializerOptions options) { } }",
            "[System.Text.Json.Serialization.JsonConverter(typeof(ValueConverter))] public int Value { get; set; }");

        Assert.Contains(result.GeneratorDiagnostics,
            d => d.Id == "TQ013" && d.GetMessage().Contains("ValueConverter") && d.GetMessage().Contains("Fn"));
        Assert.Null(result.SchemaFor("global::TestApp.ReqModel"));
    }

    [Fact]
    public void ExtensionData_IsFlattenedIntoAdditionalProperties()
    {
        var result = Run(string.Empty,
            "public string Id { get; set; } [System.Text.Json.Serialization.JsonExtensionData] public System.Collections.Generic.Dictionary<string,System.Text.Json.JsonElement> Extra { get; set; }");
        var schema = result.SchemaFor("global::TestApp.ReqModel");

        Assert.Empty(result.CompileErrors);
        Assert.DoesNotContain("\"Extra\"", schema);
        Assert.Contains("\"additionalProperties\":true", schema);
    }

    [Fact]
    public void InvalidExtensionDataType_ProducesDiagnostic()
    {
        var result = Run(string.Empty,
            "[System.Text.Json.Serialization.JsonExtensionData] public System.Collections.Generic.Dictionary<string,int> Extra { get; set; }");

        Assert.Contains(result.GeneratorDiagnostics, d => d.Id == "TQ012");
        Assert.Null(result.SchemaFor("global::TestApp.ReqModel"));
    }

    [Fact]
    public void CollidingDefNames_AreStableWhenPropertyDeclarationsAreReordered()
    {
        const string declarations = "namespace A { public class Item { public string A { get; set; } } } namespace B { public class Item { public int B { get; set; } } }";
        var first = Run(declarations, "public A.Item First { get; set; } public B.Item Second { get; set; }");
        var reordered = Run(declarations, "public B.Item Second { get; set; } public A.Item First { get; set; }");

        Assert.Empty(first.CompileErrors);
        Assert.Empty(reordered.CompileErrors);
        Assert.Equal(first.SchemaFor("global::TestApp.ReqModel"), reordered.SchemaFor("global::TestApp.ReqModel"));
    }

    [Fact]
    public void GenericStringEnumConverter_SuppressesUnrepresentableSchema()
    {
        var result = Run(
            "[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter<State>))] public enum State { Ready, Done }",
            "public State State { get; set; }");
        var schema = result.SchemaFor("global::TestApp.ReqModel");

        Assert.Empty(result.CompileErrors);
        Assert.Null(schema);
        Assert.Contains(result.GeneratorDiagnostics, diagnostic => diagnostic.Id == "TQ012");
    }

    [Fact]
    public void UnsignedEnumValues_DoNotCrashGeneratorOrChangeWireValue()
    {
        var result = Run("public enum Flags : ulong { Max = 18446744073709551615UL }", "public Flags Value { get; set; }");
        var schema = result.SchemaFor("global::TestApp.ReqModel");

        Assert.DoesNotContain(result.GeneratorDiagnostics, d => d.Id == "CS8785");
        Assert.Empty(result.CompileErrors);
        Assert.Contains("\"Value\":{\"maximum\":18446744073709551615,\"minimum\":0,\"type\":\"integer\"}", schema);
    }

    [Fact]
    public void DuplicateJsonPropertyNames_ProduceDiagnosticInsteadOfInvalidSchema()
    {
        var result = Run(string.Empty,
            "[System.Text.Json.Serialization.JsonPropertyName(\"same\")] public string First { get; set; } [System.Text.Json.Serialization.JsonPropertyName(\"same\")] public string Second { get; set; }");

        Assert.Contains(result.GeneratorDiagnostics, d => d.Id == "TQ012");
        Assert.Null(result.SchemaFor("global::TestApp.ReqModel"));
    }

    [Fact]
    public void StrictJsonNumberHandling_PreservesNumericSchema()
    {
        var result = Run(string.Empty,
            "[System.Text.Json.Serialization.JsonNumberHandling(System.Text.Json.Serialization.JsonNumberHandling.Strict)] public int Value { get; set; }");
        var schema = result.SchemaFor("global::TestApp.ReqModel");

        Assert.Empty(result.CompileErrors);
        Assert.DoesNotContain(result.GeneratorDiagnostics, d => d.Id == "TQ012");
        Assert.Contains("\"Value\":{\"maximum\":2147483647,\"minimum\":-2147483648,\"type\":\"integer\"}", schema);
    }

    [Theory]
    [InlineData("System.Text.Json.Serialization.JsonNumberHandling.WriteAsString", true)]
    [InlineData("System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString", false)]
    public void NonStrictJsonNumberHandling_ProducesTq012AndNoSchema(string handling, bool onType)
    {
        var result = onType
            ? Run($"[System.Text.Json.Serialization.JsonNumberHandling({handling})] public class NumberModel {{ public int Value {{ get; set; }} }}",
                "public NumberModel Value { get; set; }")
            : Run(string.Empty,
                $"[System.Text.Json.Serialization.JsonNumberHandling({handling})] public int Value {{ get; set; }}");

        Assert.Empty(result.CompileErrors);
        Assert.Contains(result.GeneratorDiagnostics, d => d.Id == "TQ012");
        Assert.Null(result.SchemaFor("global::TestApp.ReqModel"));
    }

    [Fact]
    public void JsonIncludedPublicField_MatchesPropertyMemberSemantics()
    {
        var result = Run(string.Empty,
            "[System.Text.Json.Serialization.JsonInclude] " +
            "[System.Text.Json.Serialization.JsonPropertyName(\"field_value\")] " +
            "[System.ComponentModel.DataAnnotations.Required] " +
            "[System.ComponentModel.DataAnnotations.Range(1, 9)] public int Value; " +
            "public int UnincludedField; " +
            "[System.Text.Json.Serialization.JsonInclude][System.Text.Json.Serialization.JsonIgnore] public int Ignored;");
        var schema = result.SchemaFor("global::TestApp.ReqModel");

        Assert.Empty(result.CompileErrors);
        Assert.Contains("\"field_value\":{\"maximum\":9,\"minimum\":1,\"type\":\"integer\"}", schema);
        Assert.Contains("\"required\":[\"field_value\"]", schema);
        Assert.DoesNotContain("UnincludedField", schema);
        Assert.DoesNotContain("Ignored", schema);
    }

    [Fact]
    public void JsonIncludedField_DuplicateWireNameProducesTq012()
    {
        var result = Run(string.Empty,
            "[System.Text.Json.Serialization.JsonPropertyName(\"same\")] public string Property { get; set; } " +
            "[System.Text.Json.Serialization.JsonInclude][System.Text.Json.Serialization.JsonPropertyName(\"same\")] public string Field;");

        Assert.Contains(result.GeneratorDiagnostics, d => d.Id == "TQ012");
        Assert.Null(result.SchemaFor("global::TestApp.ReqModel"));
    }

    [Fact]
    public void JsonIncludedField_CustomConverterProducesTq013()
    {
        var result = Run(
            "public sealed class ValueConverter : System.Text.Json.Serialization.JsonConverter<int> { public override int Read(ref System.Text.Json.Utf8JsonReader reader, System.Type type, System.Text.Json.JsonSerializerOptions options) => 0; public override void Write(System.Text.Json.Utf8JsonWriter writer, int value, System.Text.Json.JsonSerializerOptions options) { } }",
            "[System.Text.Json.Serialization.JsonInclude][System.Text.Json.Serialization.JsonConverter(typeof(ValueConverter))] public int Value;");

        Assert.Contains(result.GeneratorDiagnostics,
            d => d.Id == "TQ013" && d.GetMessage().Contains("ValueConverter"));
        Assert.Null(result.SchemaFor("global::TestApp.ReqModel"));
    }

    [Fact]
    public void JsonIncludedExtensionDataField_IsFlattened()
    {
        var result = Run(string.Empty,
            "public string Id { get; set; } " +
            "[System.Text.Json.Serialization.JsonInclude][System.Text.Json.Serialization.JsonExtensionData] " +
            "public System.Collections.Generic.Dictionary<string,System.Text.Json.JsonElement> Extra;");
        var schema = result.SchemaFor("global::TestApp.ReqModel");

        Assert.Empty(result.CompileErrors);
        Assert.DoesNotContain("\"Extra\"", schema);
        Assert.Contains("\"additionalProperties\":true", schema);
    }

    [Fact]
    public void NullableStringEnum_SuppressesUnrepresentableSchema()
    {
        var result = Run(
            "[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter<State>))] public enum State { Ready, Done }",
            "public State? State { get; set; }");
        var schema = result.SchemaFor("global::TestApp.ReqModel");

        Assert.Empty(result.CompileErrors);
        Assert.Null(schema);
        Assert.Contains(result.GeneratorDiagnostics, diagnostic => diagnostic.Id == "TQ012");
    }

    [Fact]
    public void CustomEnumConverter_ProducesTq013()
    {
        var result = Run(
            "[System.Text.Json.Serialization.JsonConverter(typeof(StateConverter))] public enum State { Ready } " +
            "public sealed class StateConverter : System.Text.Json.Serialization.JsonConverter<State> { public override State Read(ref System.Text.Json.Utf8JsonReader reader, System.Type type, System.Text.Json.JsonSerializerOptions options) => State.Ready; public override void Write(System.Text.Json.Utf8JsonWriter writer, State value, System.Text.Json.JsonSerializerOptions options) { } }",
            "public State State { get; set; }");

        Assert.Contains(result.GeneratorDiagnostics,
            d => d.Id == "TQ013" && d.GetMessage().Contains("StateConverter"));
        Assert.Null(result.SchemaFor("global::TestApp.ReqModel"));
    }

    [Fact]
    public void RangeTypeString_ParsesInvariantNumericBoundsExactly()
    {
        var result = Run(string.Empty,
            "[System.ComponentModel.DataAnnotations.Range(typeof(decimal), \"0.1234567890123456789012345678\", \"9.876543210987654321098765432\")] public decimal Value { get; set; }");
        var schema = result.SchemaFor("global::TestApp.ReqModel");

        Assert.Empty(result.CompileErrors);
        Assert.Contains("\"maximum\":9.876543210987654321098765432", schema);
        Assert.Contains("\"minimum\":0.1234567890123456789012345678", schema);
    }

    [Fact]
    public void RangeTypeString_WithUnsupportedOrInvalidBoundsProducesTq012()
    {
        var invalid = Run(string.Empty,
            "[System.ComponentModel.DataAnnotations.Range(typeof(decimal), \"not-a-number\", \"10\")] public decimal Value { get; set; }");
        var unsupported = Run(string.Empty,
            "[System.ComponentModel.DataAnnotations.Range(typeof(System.DateTime), \"2020-01-01\", \"2030-01-01\")] public System.DateTime Value { get; set; }");

        Assert.Contains(invalid.GeneratorDiagnostics, d => d.Id == "TQ012");
        Assert.Null(invalid.SchemaFor("global::TestApp.ReqModel"));
        Assert.Contains(unsupported.GeneratorDiagnostics, d => d.Id == "TQ012");
        Assert.Null(unsupported.SchemaFor("global::TestApp.ReqModel"));
    }

    [Fact]
    public void LengthAnnotations_UseWireShapeSpecificKeywords()
    {
        var result = Run(string.Empty,
            "[System.ComponentModel.DataAnnotations.MinLength(1)][System.ComponentModel.DataAnnotations.MaxLength(2)] public string Text { get; set; } " +
            "[System.ComponentModel.DataAnnotations.MinLength(3)][System.ComponentModel.DataAnnotations.MaxLength(4)] public System.Collections.Generic.List<int> Items { get; set; } " +
            "[System.ComponentModel.DataAnnotations.MinLength(5)][System.ComponentModel.DataAnnotations.MaxLength(6)] public System.Collections.Generic.Dictionary<string,int> Map { get; set; } " +
            "[System.ComponentModel.DataAnnotations.MinLength(7)][System.ComponentModel.DataAnnotations.MaxLength(8)] public byte[] Bytes { get; set; }");
        var schema = result.SchemaFor("global::TestApp.ReqModel");

        Assert.Empty(result.CompileErrors);
        Assert.Contains("\"Text\":{\"maxLength\":2,\"minLength\":1,\"type\":\"string\"}", schema);
        Assert.Contains("\"Items\":{\"items\":{\"maximum\":2147483647,\"minimum\":-2147483648,\"type\":\"integer\"},\"maxItems\":4,\"minItems\":3,\"type\":\"array\"}", schema);
        Assert.Contains("\"Map\":{\"additionalProperties\":{\"maximum\":2147483647,\"minimum\":-2147483648,\"type\":\"integer\"},\"maxProperties\":6,\"minProperties\":5,\"type\":\"object\"}", schema);
        Assert.Contains("\"Bytes\":{\"contentEncoding\":\"base64\",\"type\":\"string\"}", schema);
    }
}
