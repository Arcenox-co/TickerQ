using Microsoft.CodeAnalysis;

namespace TickerQ.SourceGenerator.Tests;

/// <summary>
/// Behavior tests for the compile-time JSON Schema 2020-12 emitter (Task 4). Each test drives the real
/// generator, decodes the embedded schema/example from the emitted descriptor, and asserts both the
/// wire shape and that the generated code compiles clean against the canonical registration API.
/// Object member keys are emitted in ordinal order (canonical form), so exact snapshots are stable.
/// </summary>
public class SchemaGenerationTests
{
    private static SchemaTestHarness.GenResult RunTyped(string models, string reqTypeName, out string schema)
    {
        var source = $@"
using System.Threading;
using System.Threading.Tasks;
using TickerQ.Utilities.Base;

namespace TestApp
{{
    {models}

    public class Jobs
    {{
        [TickerFunction(""Fn"")]
        public Task Fn(TickerFunctionContext<{reqTypeName}> context, CancellationToken ct) => Task.CompletedTask;
    }}
}}";
        var result = SchemaTestHarness.Run(source);
        Assert.DoesNotContain(result.GeneratorDiagnostics, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Empty(result.CompileErrors);
        schema = result.SchemaFor($"global::TestApp.{reqTypeName}")!;
        return result;
    }

    [Fact]
    public void Primitives_EmitObjectSchema_AndCompiles()
    {
        RunTyped("public class ReqModel { public string Id { get; set; } public int Amount { get; set; } }",
            "ReqModel", out var schema);

        Assert.Equal(
            "{\"$schema\":\"https://json-schema.org/draft/2020-12/schema\",\"additionalProperties\":true," +
            "\"properties\":{\"Amount\":{\"maximum\":2147483647,\"minimum\":-2147483648,\"type\":\"integer\"},\"Id\":{\"type\":\"string\"}},\"type\":\"object\"}",
            schema);
    }

    [Fact]
    public void NestedObject_EmitsDefAndRef()
    {
        RunTyped(
            "public class Address { public string City { get; set; } } " +
            "public class ReqModel { public string Name { get; set; } public Address Home { get; set; } }",
            "ReqModel", out var schema);

        Assert.Contains("\"Home\":{\"$ref\":\"#/$defs/Address\"}", schema);
        Assert.Contains("\"$defs\":{\"Address\":{\"additionalProperties\":true,\"properties\":{\"City\":{\"type\":\"string\"}},\"type\":\"object\"}}", schema);
    }

    [Fact]
    public void RecursiveType_UsesRefForCycle()
    {
        RunTyped("public class ReqModel { public string Value { get; set; } public ReqModel Next { get; set; } }",
            "ReqModel", out var schema);

        Assert.Contains("\"Next\":{\"$ref\":\"#/$defs/ReqModel\"}", schema);
        Assert.Contains("\"$defs\":{\"ReqModel\":{", schema);
    }

    [Fact]
    public void Collections_EmitArraySchemas()
    {
        RunTyped("public class ReqModel { public System.Collections.Generic.List<string> Tags { get; set; } public int[] Ids { get; set; } }",
            "ReqModel", out var schema);

        Assert.Contains("\"Tags\":{\"items\":{\"type\":\"string\"},\"type\":\"array\"}", schema);
        Assert.Contains("\"Ids\":{\"items\":{\"maximum\":2147483647,\"minimum\":-2147483648,\"type\":\"integer\"},\"type\":\"array\"}", schema);
    }

    [Fact]
    public void Dictionary_EmitsAdditionalPropertiesValueSchema()
    {
        RunTyped("public class ReqModel { public System.Collections.Generic.Dictionary<string,int> Values { get; set; } }",
            "ReqModel", out var schema);

        Assert.Contains("\"Values\":{\"additionalProperties\":{\"maximum\":2147483647,\"minimum\":-2147483648,\"type\":\"integer\"},\"type\":\"object\"}", schema);
    }

    [Fact]
    public void NumericEnum_IsAnOpenIntegerContract()
    {
        RunTyped("public enum Color { Red, Green = 5, Blue } public class ReqModel { public Color Color { get; set; } }",
            "ReqModel", out var schema);

        Assert.Contains("\"Color\":{\"maximum\":2147483647,\"minimum\":-2147483648,\"type\":\"integer\"}", schema);
        Assert.DoesNotContain("\"enum\"", schema);
    }

    [Fact]
    public void StringEnum_WithConverter_SuppressesUnrepresentableSchema()
    {
        var result = RunTyped(
            "[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter))] " +
            "public enum Status { Active, Inactive } public class ReqModel { public Status Status { get; set; } }",
            "ReqModel", out var schema);

        Assert.Null(schema);
        Assert.Contains(result.GeneratorDiagnostics, diagnostic => diagnostic.Id == "TQ012");
    }

    [Fact]
    public void NullableValueType_EmitsNullUnion()
    {
        RunTyped("public class ReqModel { public int? Count { get; set; } }", "ReqModel", out var schema);
        Assert.Contains("\"Count\":{\"maximum\":2147483647,\"minimum\":-2147483648,\"type\":[\"integer\",\"null\"]}", schema);
    }

    [Fact]
    public void NullableReferenceTypes_EmitNullUnions()
    {
        var source = @"
#nullable enable
using System.Threading;
using System.Threading.Tasks;
using TickerQ.Utilities.Base;

namespace TestApp
{
    public class Address { public string City { get; set; } = """"; }
    public class ReqModel { public string? Name { get; set; } public Address? Home { get; set; } }

    public class Jobs
    {
        [TickerFunction(""Fn"")]
        public Task Fn(TickerFunctionContext<ReqModel> context, CancellationToken ct) => Task.CompletedTask;
    }
}";
        var result = SchemaTestHarness.Run(source);
        Assert.Empty(result.CompileErrors);
        var schema = result.SchemaFor("global::TestApp.ReqModel");

        Assert.Contains("\"Name\":{\"type\":[\"string\",\"null\"]}", schema);
        Assert.Contains("\"Home\":{\"anyOf\":[{\"$ref\":\"#/$defs/Address\"},{\"type\":\"null\"}]}", schema);
    }

    [Fact]
    public void JsonPropertyName_RenamesWireProperty()
    {
        RunTyped(
            "public class ReqModel { [System.Text.Json.Serialization.JsonPropertyName(\"order_id\")] public string OrderId { get; set; } }",
            "ReqModel", out var schema);

        Assert.Contains("\"order_id\":{\"type\":\"string\"}", schema);
        Assert.DoesNotContain("OrderId", schema);
    }

    [Fact]
    public void JsonIgnore_ExcludesProperty()
    {
        RunTyped(
            "public class ReqModel { public string Keep { get; set; } [System.Text.Json.Serialization.JsonIgnore] public string Secret { get; set; } }",
            "ReqModel", out var schema);

        Assert.Contains("\"Keep\":{\"type\":\"string\"}", schema);
        Assert.DoesNotContain("Secret", schema);
    }

    [Fact]
    public void RequiredMembers_AreListed()
    {
        RunTyped(
            "public class ReqModel { public required string A { get; set; } " +
            "[System.ComponentModel.DataAnnotations.Required] public string B { get; set; } public string C { get; set; } }",
            "ReqModel", out var schema);

        Assert.Contains("\"required\":[\"A\",\"B\"]", schema);
    }

    [Fact]
    public void ValidationAnnotations_MapToKeywords()
    {
        RunTyped(
            "public class ReqModel { " +
            "[System.ComponentModel.DataAnnotations.MinLength(1)][System.ComponentModel.DataAnnotations.MaxLength(200)] public string Subject { get; set; } " +
            "[System.ComponentModel.DataAnnotations.Range(0, 100)] public int Amount { get; set; } " +
            "[System.ComponentModel.DataAnnotations.RegularExpression(\"^a$\")] public string Code { get; set; } " +
            "[System.ComponentModel.DataAnnotations.EmailAddress] public string Email { get; set; } " +
            "[System.ComponentModel.DataAnnotations.Url] public string Site { get; set; } }",
            "ReqModel", out var schema);

        Assert.Contains("\"Subject\":{\"maxLength\":200,\"minLength\":1,\"type\":\"string\"}", schema);
        Assert.Contains("\"Amount\":{\"maximum\":100,\"minimum\":0,\"type\":\"integer\"}", schema);
        Assert.Contains("\"Code\":{\"pattern\":\"^(?:a)(?![\\\\s\\\\S])\",\"type\":\"string\"}", schema);
        Assert.Contains("\"Email\":{\"format\":\"email\",\"type\":\"string\"}", schema);
        Assert.Contains("\"Site\":{\"format\":\"uri\",\"type\":\"string\"}", schema);
    }

    [Fact]
    public void UnmappedMemberHandlingDisallow_SetsAdditionalPropertiesFalse()
    {
        RunTyped(
            "[System.Text.Json.Serialization.JsonUnmappedMemberHandling(System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow)] " +
            "public class ReqModel { public string A { get; set; } }",
            "ReqModel", out var schema);

        Assert.Contains("\"additionalProperties\":false", schema);
    }

    [Fact]
    public void DefaultExample_IsDeterministicAndEmbedded()
    {
        var result = RunTyped("public class ReqModel { public string Id { get; set; } public int Amount { get; set; } }",
            "ReqModel", out _);

        Assert.Equal("{\"Amount\":0,\"Id\":\"string\"}", result.ExampleForDefault());
    }

    [Fact]
    public void DefaultExample_UsesRangeMinimum()
    {
        var result = RunTyped(
            "public class ReqModel { [System.ComponentModel.DataAnnotations.Range(10, 20)] public int Amount { get; set; } }",
            "ReqModel", out _);

        Assert.Equal("{\"Amount\":10}", result.ExampleForDefault());
    }

    [Fact]
    public void DefaultExample_SatisfiesMinimumStringLength()
    {
        var result = RunTyped(
            "public class ReqModel { [System.ComponentModel.DataAnnotations.MinLength(10)] public string Name { get; set; } }",
            "ReqModel", out _);

        Assert.Equal("{\"Name\":\"xxxxxxxxxx\"}", result.ExampleForDefault());
    }

    [Fact]
    public void DefaultExample_UsesValidEmailAndUrlFormats()
    {
        var result = RunTyped(
            "public class ReqModel { [System.ComponentModel.DataAnnotations.EmailAddress] public string Email { get; set; } " +
            "[System.ComponentModel.DataAnnotations.Url] public string Site { get; set; } }",
            "ReqModel", out _);

        Assert.Equal("{\"Email\":\"user@example.com\",\"Site\":\"https://example.com\"}", result.ExampleForDefault());
    }

    [Fact]
    public void DefaultExample_IsSuppressedWithUnrepresentableStringEnumSchema()
    {
        var result = RunTyped(
            "public enum Color { Red, Blue } " +
            "[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter))] " +
            "public enum Status { Active, Inactive } " +
            "public class ReqModel { public Color Color { get; set; } public Status Status { get; set; } }",
            "ReqModel", out _);

        Assert.Contains(result.GeneratorDiagnostics, diagnostic => diagnostic.Id == "TQ012");
        Assert.Null(result.ExampleForDefault());
    }

    [Fact]
    public void DefaultExample_OmitsOptionalRecursiveProperty()
    {
        var result = RunTyped(
            "public class ReqModel { public ReqModel Next { get; set; } public string Value { get; set; } }",
            "ReqModel", out _);

        Assert.Equal("{\"Value\":\"string\"}", result.ExampleForDefault());
    }

    [Fact]
    public void DefaultExample_UsesNullForRequiredNullableRecursion()
    {
        var result = RunTyped(
            "#nullable enable\npublic class ReqModel { public required ReqModel? Next { get; set; } public string Value { get; set; } = \"\"; }",
            "ReqModel", out _);

        Assert.Equal("{\"Next\":null,\"Value\":\"string\"}", result.ExampleForDefault());
    }

    [Fact]
    public void RequiredNonNullableRecursion_EmitsDiagnosticAndNoSchemaOrExample()
    {
        var result = RunTyped(
            "public class ReqModel { [System.ComponentModel.DataAnnotations.Required] public ReqModel Next { get; set; } }",
            "ReqModel", out var schema);

        Assert.Null(schema);
        Assert.Contains(result.GeneratorDiagnostics,
            d => d.Id == "TQ012" && d.GetMessage().Contains("required") && d.GetMessage().Contains("recursive"));
        Assert.DoesNotContain("TickerRequestExample", result.Generated);
    }

    [Fact]
    public void RequiredRegularExpression_EmitsDiagnosticAndNoSchemaOrExample()
    {
        var result = RunTyped(
            "public class ReqModel { [System.ComponentModel.DataAnnotations.Required] " +
            "[System.ComponentModel.DataAnnotations.RegularExpression(\"^[A-Z]{3}[0-9]{4}$\")] public string Code { get; set; } }",
            "ReqModel", out var schema);

        Assert.Null(schema);
        Assert.Contains(result.GeneratorDiagnostics,
            d => d.Id == "TQ012" && d.GetMessage().Contains("regular expression"));
        Assert.DoesNotContain("TickerRequestExample", result.Generated);
    }

    [Fact]
    public void UnsupportedType_EmitsDiagnostic_AndNoSchema()
    {
        var source = @"
using System.Threading;
using System.Threading.Tasks;
using TickerQ.Utilities.Base;

namespace TestApp
{
    public interface IThing { }
    public class ReqModel { public IThing Thing { get; set; } }

    public class Jobs
    {
        [TickerFunction(""Fn"")]
        public Task Fn(TickerFunctionContext<ReqModel> context, CancellationToken ct) => Task.CompletedTask;
    }
}";
        var result = SchemaTestHarness.Run(source);
        Assert.Empty(result.CompileErrors);

        Assert.Contains(result.GeneratorDiagnostics, d => d.Id == "TQ012" && d.GetMessage().Contains("Fn"));
        Assert.Null(result.SchemaFor("global::TestApp.ReqModel"));
        // Descriptor still registered, without a schema.
        Assert.Contains("new global::TickerQ.Utilities.Models.TickerRequestContract(typeof(global::TestApp.ReqModel).FullName))", result.Generated);
    }

    [Fact]
    public void CustomConverter_EmitsDiagnostic_NamingConverter_AndNoSchema()
    {
        var source = @"
using System.Threading;
using System.Threading.Tasks;
using TickerQ.Utilities.Base;

namespace TestApp
{
    [System.Text.Json.Serialization.JsonConverter(typeof(MoneyConverter))]
    public class ReqModel { public decimal Amount { get; set; } }

    public class MoneyConverter : System.Text.Json.Serialization.JsonConverter<ReqModel>
    {
        public override ReqModel Read(ref System.Text.Json.Utf8JsonReader reader, System.Type type, System.Text.Json.JsonSerializerOptions options) => null;
        public override void Write(System.Text.Json.Utf8JsonWriter writer, ReqModel value, System.Text.Json.JsonSerializerOptions options) { }
    }

    public class Jobs
    {
        [TickerFunction(""Fn"")]
        public Task Fn(TickerFunctionContext<ReqModel> context, CancellationToken ct) => Task.CompletedTask;
    }
}";
        var result = SchemaTestHarness.Run(source);
        Assert.Empty(result.CompileErrors);

        Assert.Contains(result.GeneratorDiagnostics,
            d => d.Id == "TQ013" && d.GetMessage().Contains("MoneyConverter") && d.GetMessage().Contains("Fn"));
        Assert.Null(result.SchemaFor("global::TestApp.ReqModel"));
    }
}
