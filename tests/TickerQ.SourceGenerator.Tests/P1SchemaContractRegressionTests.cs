using System.Text.Json;
using System.Text.Json.Serialization;
using Json.Schema;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace TickerQ.SourceGenerator.Tests;

public sealed class P1SchemaContractRegressionTests
{
    [Fact]
    public void PublicSetterPrivateGetterRequiredProperty_MatchesStjDeserializationAndDraftSchema()
    {
        const string source = """
using System.Text.Json.Serialization;
using TickerQ.Utilities.Base;
public sealed class SetterRequest
{
    [JsonRequired]
    public string Value { private get; set; }
}
public static class Jobs
{
    [TickerFunction("Setter")]
    public static System.Threading.Tasks.Task Run(TickerFunctionContext<SetterRequest> context) => System.Threading.Tasks.Task.CompletedTask;
}
""";

        var result = SchemaTestHarness.Run(source);
        Assert.Empty(result.CompileErrors);
        var schema = ParseSchema(result, "global::SetterRequest");

        Assert.False(Evaluate(schema, "{}").IsValid);
        Assert.True(Evaluate(schema, "{\"Value\":\"ok\"}").IsValid);
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<SetterOnlyProbe>("{}"));
        Assert.NotNull(JsonSerializer.Deserialize<SetterOnlyProbe>("{\"Value\":\"ok\"}"));
        Assert.Equal("{\"Value\":\"string\"}", result.ExampleForDefault());
    }

    [Fact]
    public void JsonIncludePrivateSetterProperty_SuppressesAotContract()
    {
        const string source = """
using System.Text.Json.Serialization;
using TickerQ.Utilities.Base;
public sealed class IncludedRequest
{
    [JsonInclude, JsonRequired]
    public string Value { get; private set; }
}
public static class Jobs
{
    [TickerFunction("Included")]
    public static System.Threading.Tasks.Task Run(TickerFunctionContext<IncludedRequest> context) => System.Threading.Tasks.Task.CompletedTask;
}
""";

        var result = SchemaTestHarness.Run(source);
        Assert.Empty(result.CompileErrors);
        Assert.Contains(result.GeneratorDiagnostics,
            diagnostic => diagnostic.Id == "TQ012"
                && diagnostic.GetMessage().Contains("JsonInclude", StringComparison.Ordinal));
        Assert.Null(result.SchemaFor("global::IncludedRequest"));
        Assert.Null(result.ExampleForDefault());
    }

    [Fact]
    public void GenericStringEnumConverter_UnrepresentableCaseInsensitiveGrammarSuppressesSchema()
    {
        const string source = """
using System.Text.Json.Serialization;
using TickerQ.Utilities.Base;
[JsonConverter(typeof(JsonStringEnumConverter<State>))]
public enum State { Ready, Done }
public sealed class EnumRequest { public State State { get; set; } }
public static class Jobs
{
    [TickerFunction("Enum")]
    public static System.Threading.Tasks.Task Run(TickerFunctionContext<EnumRequest> context) => System.Threading.Tasks.Task.CompletedTask;
}
""";

        var result = SchemaTestHarness.Run(source);
        Assert.Empty(result.CompileErrors);
        Assert.Equal(StateProbe.Ready, JsonSerializer.Deserialize<StateProbe>("\"ready\""));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<StateProbe>("\"not-a-real-enum-value\""));
        Assert.Contains(result.GeneratorDiagnostics,
            diagnostic => diagnostic.Id == "TQ012"
                && diagnostic.GetMessage().Contains("string enum", StringComparison.OrdinalIgnoreCase));
        Assert.Null(result.SchemaFor("global::EnumRequest"));
        Assert.Null(result.ExampleForDefault());
    }

    [Fact]
    public void GenericStringFlagsConverter_UnrepresentableCombinationGrammarSuppressesSchema()
    {
        const string source = """
using System;
using System.Text.Json.Serialization;
using TickerQ.Utilities.Base;
[Flags, JsonConverter(typeof(JsonStringEnumConverter<Access>))]
public enum Access { Read = 1, Write = 2 }
public sealed class FlagsRequest { public Access Access { get; set; } }
public static class Jobs
{
    [TickerFunction("Flags")]
    public static System.Threading.Tasks.Task Run(TickerFunctionContext<FlagsRequest> context) => System.Threading.Tasks.Task.CompletedTask;
}
""";

        var result = SchemaTestHarness.Run(source);
        Assert.Empty(result.CompileErrors);
        var serialized = JsonSerializer.Serialize(AccessProbe.Read | AccessProbe.Write);

        Assert.Equal("\"Read, Write\"", serialized);
        Assert.Equal(AccessProbe.Read | AccessProbe.Write, JsonSerializer.Deserialize<AccessProbe>(serialized));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<AccessProbe>("\"not-a-real-enum-value\""));
        Assert.Contains(result.GeneratorDiagnostics,
            diagnostic => diagnostic.Id == "TQ012"
                && diagnostic.GetMessage().Contains("string enum", StringComparison.OrdinalIgnoreCase));
        Assert.Null(result.SchemaFor("global::FlagsRequest"));
        Assert.Null(result.ExampleForDefault());
    }

    [Theory]
    [InlineData("[System.ComponentModel.DataAnnotations.MinLength(4096)] public required string Value { get; set; }")]
    [InlineData("[System.ComponentModel.DataAnnotations.MinLength(4096)] public required System.Collections.Generic.List<int> Value { get; set; }")]
    [InlineData("[System.ComponentModel.DataAnnotations.MinLength(4096)] public required System.Collections.Generic.Dictionary<string,int> Value { get; set; }")]
    public void RequiredValueBeyondExampleBudget_EmitsTq012AndSuppressesSchemaAndExample(string property)
    {
        var source = $$"""
using TickerQ.Utilities.Base;
public sealed class BudgetRequest { {{property}} }
public static class Jobs
{
    [TickerFunction("Budget")]
    public static System.Threading.Tasks.Task Run(TickerFunctionContext<BudgetRequest> context) => System.Threading.Tasks.Task.CompletedTask;
}
""";

        var result = SchemaTestHarness.Run(source);

        Assert.Empty(result.CompileErrors);
        Assert.Contains(result.GeneratorDiagnostics,
            diagnostic => diagnostic.Id == "TQ012" && diagnostic.GetMessage().Contains("budget", StringComparison.OrdinalIgnoreCase));
        Assert.Null(result.SchemaFor("global::BudgetRequest"));
        Assert.Null(result.ExampleForDefault());
    }

    [Fact]
    public void ExclusiveDoubleRangeOutsideDecimalRange_GeneratesTypeCorrectValidWitness()
    {
        const string source = """
using System.ComponentModel.DataAnnotations;
using TickerQ.Utilities.Base;
public sealed class DoubleRequest
{
    [Range(typeof(double), "1E+100", "1.1E+100", MinimumIsExclusive = true)]
    public required double Value { get; set; }
}
public static class Jobs
{
    [TickerFunction("Double")]
    public static System.Threading.Tasks.Task Run(TickerFunctionContext<DoubleRequest> context) => System.Threading.Tasks.Task.CompletedTask;
}
""";

        var result = SchemaTestHarness.Run(source);
        Assert.Empty(result.CompileErrors);
        var schema = ParseSchema(result, "global::DoubleRequest");
        var example = result.ExampleForDefault()!;

        Assert.True(Evaluate(schema, example).IsValid);
        var value = JsonSerializer.Deserialize<LargeDoubleProbe>(example)!.Value;
        Assert.True(value > 1e100 && value <= 1.1e100, $"Unexpected witness {value:R}");
    }

    [Fact]
    public void CommonRegexSubset_EmitsDraftPatternWithValidatorBackedSemantics()
    {
        const string source = """
using System.ComponentModel.DataAnnotations;
using TickerQ.Utilities.Base;
public sealed class RegexRequest
{
    [RegularExpression("^[A-Z]{2}[0-9]+$")]
    public string Code { get; set; }
}
public static class Jobs
{
    [TickerFunction("Regex")]
    public static System.Threading.Tasks.Task Run(TickerFunctionContext<RegexRequest> context) => System.Threading.Tasks.Task.CompletedTask;
}
""";

        var result = SchemaTestHarness.Run(source);
        Assert.Empty(result.CompileErrors);
        var schema = ParseSchema(result, "global::RegexRequest");

        Assert.True(Evaluate(schema, "{\"Code\":\"AB12\"}").IsValid);
        Assert.False(Evaluate(schema, "{\"Code\":\"ab12\"}").IsValid);
    }

    [Theory]
    [InlineData("\\A[A-Z]+$")]
    [InlineData("^(?<word>[A-Z]+)$")]
    public void DotNetOnlyRegex_EmitsTq012AndSuppressesSchema(string pattern)
    {
        var source = $$"""
using System.ComponentModel.DataAnnotations;
using TickerQ.Utilities.Base;
public sealed class RegexRequest
{
    [RegularExpression({{SymbolDisplay.FormatLiteral(pattern, true)}})]
    public string Code { get; set; }
}
public static class Jobs
{
    [TickerFunction("Regex")]
    public static System.Threading.Tasks.Task Run(TickerFunctionContext<RegexRequest> context) => System.Threading.Tasks.Task.CompletedTask;
}
""";

        var result = SchemaTestHarness.Run(source);

        Assert.Empty(result.CompileErrors);
        Assert.Contains(result.GeneratorDiagnostics,
            diagnostic => diagnostic.Id == "TQ012" && diagnostic.GetMessage().Contains("regular expression", StringComparison.OrdinalIgnoreCase));
        Assert.Null(result.SchemaFor("global::RegexRequest"));
    }

    [Fact]
    public void NumericEnumExclusiveRange_ProducesIntegralDraftValidExample()
    {
        const string source = """
using System.ComponentModel.DataAnnotations;
using TickerQ.Utilities.Base;
public enum NumericState { One = 1, Two = 2 }
public sealed class EnumRangeRequest
{
    [Range(1, 2, MinimumIsExclusive = true)]
    public NumericState State { get; set; }
}
public static class Jobs
{
    [TickerFunction("EnumRange")]
    public static System.Threading.Tasks.Task Run(TickerFunctionContext<EnumRangeRequest> context) => System.Threading.Tasks.Task.CompletedTask;
}
""";

        var result = SchemaTestHarness.Run(source);
        Assert.Empty(result.CompileErrors);
        var schema = ParseSchema(result, "global::EnumRangeRequest");
        var example = result.ExampleForDefault()!;

        Assert.True(Evaluate(schema, example).IsValid, example);
        using var document = JsonDocument.Parse(example);
        Assert.Equal(JsonValueKind.Number, document.RootElement.GetProperty("State").ValueKind);
        Assert.True(document.RootElement.GetProperty("State").TryGetInt64(out _), example);
    }

    [Theory]
    [InlineData("[System.Text.Json.Serialization.JsonInclude] private string Secret { get; set; }")]
    [InlineData("[System.Text.Json.Serialization.JsonInclude] private string Secret;")]
    public void PrivateJsonIncludeMember_EmitsTq012AndSuppressesAotContract(string member)
    {
        var source = $$"""
using TickerQ.Utilities.Base;
public sealed class PrivateIncludeRequest { {{member}} }
public static class Jobs
{
    [TickerFunction("PrivateInclude")]
    public static System.Threading.Tasks.Task Run(TickerFunctionContext<PrivateIncludeRequest> context) => System.Threading.Tasks.Task.CompletedTask;
}
""";

        var result = SchemaTestHarness.Run(source);

        Assert.Empty(result.CompileErrors);
        Assert.Contains(result.GeneratorDiagnostics,
            diagnostic => diagnostic.Id == "TQ012"
                && diagnostic.GetMessage().Contains("JsonInclude", StringComparison.Ordinal));
        Assert.Null(result.SchemaFor("global::PrivateIncludeRequest"));
        Assert.Null(result.ExampleForDefault());
    }

    [Fact]
    public void WildcardRegex_EmitsTq012BecauseLineTerminatorSemanticsDiverge()
    {
        const string source = """
using System.ComponentModel.DataAnnotations;
using TickerQ.Utilities.Base;
public sealed class WildcardRequest
{
    [RegularExpression("^.$")]
    public string Value { get; set; }
}
public static class Jobs
{
    [TickerFunction("Wildcard")]
    public static System.Threading.Tasks.Task Run(TickerFunctionContext<WildcardRequest> context) => System.Threading.Tasks.Task.CompletedTask;
}
""";

        var validation = new System.ComponentModel.DataAnnotations.RegularExpressionAttribute("^.$");
        Assert.True(validation.IsValid("\r"));
        Assert.False(validation.IsValid("\n"));
        Assert.True(validation.IsValid("\u2028"));
        Assert.True(validation.IsValid("\u2029"));

        var result = SchemaTestHarness.Run(source);
        Assert.Empty(result.CompileErrors);
        Assert.Contains(result.GeneratorDiagnostics,
            diagnostic => diagnostic.Id == "TQ012"
                && diagnostic.GetMessage().Contains("regular expression", StringComparison.OrdinalIgnoreCase));
        Assert.Null(result.SchemaFor("global::WildcardRequest"));
    }

    [Fact]
    public void NumericEnumSchemas_EnforceAllUnderlyingTypeBoundsAndIntersectRange()
    {
        const string source = """
using System.ComponentModel.DataAnnotations;
using TickerQ.Utilities.Base;
public enum I8 : sbyte { Zero }
public enum U8 : byte { Zero }
public enum I16 : short { Zero }
public enum U16 : ushort { Zero }
public enum I32 : int { Zero }
public enum U32 : uint { Zero }
public enum I64 : long { Zero }
public enum U64 : ulong { Zero }
public sealed class EnumBoundsRequest
{
    public I8 I8 { get; set; }
    [Range(-10, 300)] public U8 U8 { get; set; }
    public I16 I16 { get; set; }
    public U16 U16 { get; set; }
    public I32 I32 { get; set; }
    public U32 U32 { get; set; }
    public I64 I64 { get; set; }
    public U64 U64 { get; set; }
}
public static class Jobs
{
    [TickerFunction("EnumBounds")]
    public static System.Threading.Tasks.Task Run(TickerFunctionContext<EnumBoundsRequest> context) => System.Threading.Tasks.Task.CompletedTask;
}
""";

        var result = SchemaTestHarness.Run(source);
        Assert.Empty(result.CompileErrors);
        var schemaText = result.SchemaFor("global::EnumBoundsRequest")!;
        using var document = JsonDocument.Parse(schemaText);
        var properties = document.RootElement.GetProperty("properties");
        var expected = new Dictionary<string, (string Min, string Max)>
        {
            ["I8"] = ("-128", "127"), ["U8"] = ("0", "255"),
            ["I16"] = ("-32768", "32767"), ["U16"] = ("0", "65535"),
            ["I32"] = ("-2147483648", "2147483647"), ["U32"] = ("0", "4294967295"),
            ["I64"] = ("-9223372036854775808", "9223372036854775807"),
            ["U64"] = ("0", "18446744073709551615")
        };
        foreach (var (name, bounds) in expected)
        {
            var property = properties.GetProperty(name);
            Assert.Equal(bounds.Min, property.GetProperty("minimum").GetRawText());
            Assert.Equal(bounds.Max, property.GetProperty("maximum").GetRawText());
        }

        var schema = JsonSchema.FromText(schemaText);
        Assert.True(Evaluate(schema, "{\"I8\":-128,\"U8\":255,\"I16\":-32768,\"U16\":65535,\"I32\":-2147483648,\"U32\":4294967295,\"I64\":-9223372036854775808,\"U64\":18446744073709551615}").IsValid);
        Assert.False(Evaluate(schema, "{\"U8\":256}").IsValid);
        Assert.False(Evaluate(schema, "{\"I8\":-129}").IsValid);
        Assert.True(Evaluate(schema, result.ExampleForDefault()!).IsValid);
    }

    [Fact]
    public void IntegralPrimitiveSchemas_EnforceClrBoundsAndIntersectRange()
    {
        const string source = """
using System.ComponentModel.DataAnnotations;
using TickerQ.Utilities.Base;
public sealed class IntegralBoundsRequest
{
    public sbyte I8 { get; set; }
    [Range(-10, 300)] public byte U8 { get; set; }
    public short I16 { get; set; }
    public ushort U16 { get; set; }
    public int I32 { get; set; }
    public uint U32 { get; set; }
    public long I64 { get; set; }
    public ulong U64 { get; set; }
}
public static class Jobs
{
    [TickerFunction("IntegralBounds")]
    public static System.Threading.Tasks.Task Run(TickerFunctionContext<IntegralBoundsRequest> context) => System.Threading.Tasks.Task.CompletedTask;
}
""";
        var result = SchemaTestHarness.Run(source);
        Assert.Empty(result.CompileErrors);
        var schemaText = result.SchemaFor("global::IntegralBoundsRequest")!;
        using var document = JsonDocument.Parse(schemaText);
        var properties = document.RootElement.GetProperty("properties");
        var expected = new Dictionary<string, (string Min, string Max)>
        {
            ["I8"] = ("-128", "127"), ["U8"] = ("0", "255"),
            ["I16"] = ("-32768", "32767"), ["U16"] = ("0", "65535"),
            ["I32"] = ("-2147483648", "2147483647"), ["U32"] = ("0", "4294967295"),
            ["I64"] = ("-9223372036854775808", "9223372036854775807"),
            ["U64"] = ("0", "18446744073709551615")
        };
        foreach (var (name, bounds) in expected)
        {
            var property = properties.GetProperty(name);
            Assert.Equal(bounds.Min, property.GetProperty("minimum").GetRawText());
            Assert.Equal(bounds.Max, property.GetProperty("maximum").GetRawText());
        }

        var schema = JsonSchema.FromText(schemaText);
        Assert.False(Evaluate(schema, "{\"I8\":-129}").IsValid);
        Assert.False(Evaluate(schema, "{\"U8\":256}").IsValid);
        var example = result.ExampleForDefault()!;
        Assert.True(Evaluate(schema, example).IsValid);
        Assert.NotEqual(-10, JsonDocument.Parse(example).RootElement.GetProperty("U8").GetInt32());
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<PrimitiveByteProbe>("{\"Value\":-10}"));
    }

    [Theory]
    [InlineData("[Range(300, 400)]")]
    [InlineData("[Range(255, 300, MinimumIsExclusive = true)]")]
    public void IntegralPrimitiveImpossibleRange_SuppressesSchemaAndExample(string range)
    {
        var source = $$"""
using System.ComponentModel.DataAnnotations;
using TickerQ.Utilities.Base;
public sealed class ImpossibleByteRequest { {{range}} public required byte Value { get; set; } }
public static class Jobs
{
    [TickerFunction("ImpossibleByte")]
    public static System.Threading.Tasks.Task Run(TickerFunctionContext<ImpossibleByteRequest> context) => System.Threading.Tasks.Task.CompletedTask;
}
""";
        var result = SchemaTestHarness.Run(source);
        Assert.Contains(result.GeneratorDiagnostics, diagnostic => diagnostic.Id == "TQ012");
        Assert.Null(result.SchemaFor("global::ImpossibleByteRequest"));
        Assert.Null(result.ExampleForDefault());
    }

    [Theory]
    [InlineData("public string Value { get; private set; }")]
    [InlineData("internal string Value { get; private set; }")]
    public void JsonIncludeInaccessibleAccessor_EmitsTq012(string property)
    {
        var source = $$"""
using System.Text.Json.Serialization;
using TickerQ.Utilities.Base;
public sealed class AccessorRequest { [JsonInclude] {{property}} }
public static class Jobs
{
    [TickerFunction("Accessor")]
    public static System.Threading.Tasks.Task Run(TickerFunctionContext<AccessorRequest> context) => System.Threading.Tasks.Task.CompletedTask;
}
""";
        var result = SchemaTestHarness.Run(source);
        Assert.Contains(result.GeneratorDiagnostics, diagnostic => diagnostic.Id == "TQ012");
        Assert.Null(result.SchemaFor("global::AccessorRequest"));
    }

    [Fact]
    public void JsonIncludeAssemblyAccessibleAccessors_RemainSupported()
    {
        const string source = """
using System.Text.Json.Serialization;
using TickerQ.Utilities.Base;
public sealed class AccessibleRequest
{
    [JsonInclude] internal string InternalValue { get; set; }
    [JsonInclude] protected internal string ProtectedInternalValue { get; set; }
}
public static class Jobs
{
    [TickerFunction("Accessible")]
    public static System.Threading.Tasks.Task Run(TickerFunctionContext<AccessibleRequest> context) => System.Threading.Tasks.Task.CompletedTask;
}
""";
        var result = SchemaTestHarness.Run(source);
        Assert.Empty(result.CompileErrors);
        var schema = result.SchemaFor("global::AccessibleRequest");
        Assert.Contains("\"InternalValue\"", schema);
        Assert.Contains("\"ProtectedInternalValue\"", schema);
    }

    [Fact]
    public void PortableRegexTranslation_RejectsTrailingLineTerminators()
    {
        const string source = """
using System.ComponentModel.DataAnnotations;
using TickerQ.Utilities.Base;
public sealed class RegexParityRequest
{
    [RegularExpression("^[A-Z]{2}[0-9]+$")] public string Code { get; set; }
    [RegularExpression("^a\\.b$")] public string Literal { get; set; }
    [RegularExpression("^a|b$")] public string Alternative { get; set; }
}
public static class Jobs
{
    [TickerFunction("RegexParity")]
    public static System.Threading.Tasks.Task Run(TickerFunctionContext<RegexParityRequest> context) => System.Threading.Tasks.Task.CompletedTask;
}
""";
        var result = SchemaTestHarness.Run(source);
        var schema = ParseSchema(result, "global::RegexParityRequest");
        Assert.True(Evaluate(schema, "{\"Alternative\":\"a\"}").IsValid);
        Assert.True(Evaluate(schema, "{\"Alternative\":\"b\"}").IsValid);
        Assert.False(Evaluate(schema, "{\"Alternative\":\"ax\"}").IsValid);
        Assert.False(Evaluate(schema, "{\"Alternative\":\"xb\"}").IsValid);
        foreach (var suffix in new[] { "\n", "\r", "\u2028", "\u2029" })
        {
            var codeJson = JsonSerializer.Serialize(new Dictionary<string, string> { ["Code"] = "AB12" + suffix });
            var literalJson = JsonSerializer.Serialize(new Dictionary<string, string> { ["Literal"] = "a.b" + suffix });
            var alternativeJson = JsonSerializer.Serialize(new Dictionary<string, string> { ["Alternative"] = "a" + suffix });
            Assert.False(Evaluate(schema, codeJson).IsValid);
            Assert.False(Evaluate(schema, literalJson).IsValid);
            Assert.False(Evaluate(schema, alternativeJson).IsValid);
        }
    }

    private static JsonSchema ParseSchema(SchemaTestHarness.GenResult result, string requestType)
    {
        Assert.DoesNotContain(result.GeneratorDiagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        return JsonSchema.FromText(result.SchemaFor(requestType)
            ?? throw new Xunit.Sdk.XunitException($"No schema was generated for {requestType}."));
    }

    private static EvaluationResults Evaluate(JsonSchema schema, string json)
    {
        using var document = JsonDocument.Parse(json);
        return schema.Evaluate(document.RootElement, new EvaluationOptions { OutputFormat = OutputFormat.List });
    }

    private sealed class LargeDoubleProbe
    {
        public double Value { get; set; }
    }

    [JsonConverter(typeof(JsonStringEnumConverter<StateProbe>))]
    private enum StateProbe { Ready, Done }

    [Flags, JsonConverter(typeof(JsonStringEnumConverter<AccessProbe>))]
    private enum AccessProbe { Read = 1, Write = 2 }

    private sealed class SetterOnlyProbe
    {
        [JsonRequired]
        public string Value { private get; set; } = null!;
    }

    private sealed class PrimitiveByteProbe
    {
        public byte Value { get; set; }
    }
}
