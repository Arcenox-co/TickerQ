using Microsoft.CodeAnalysis;

namespace TickerQ.SourceGenerator.Tests;

public sealed class ResultContractGenerationTests
{
    [Fact]
    public void DeclaredResult_EmitsContractMetadataRuntimeRegistrationAndAotPublication()
    {
        const string source = """
            using System.Threading.Tasks;
            using TickerQ.Utilities.Base;

            namespace TestApp;

            public sealed record ResultModel(string? Value);

            public sealed class Jobs
            {
                [TickerFunction("result.job", ResultType = typeof(ResultModel))]
                public Task<ResultModel> Run(TickerFunctionContext context) =>
                    Task.FromResult(new ResultModel(null));
            }
            """;

        var result = SchemaTestHarness.Run(source);

        Assert.Empty(result.CompileErrors);
        Assert.Empty(result.GeneratorDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Contains("RegisterResultTypeInfoResolver", result.Generated, StringComparison.Ordinal);
        Assert.Contains("options.GetTypeInfo(typeof(global::TestApp.ResultModel))", result.Generated, StringComparison.Ordinal);
        Assert.Contains("GetResultTypeInfo<global::TestApp.ResultModel>(\"result.job\")", result.Generated, StringComparison.Ordinal);
        Assert.Contains("var resultContract = global::TickerQ.Utilities.TickerFunctionProvider.GetResultContract(\"result.job\")", result.Generated, StringComparison.Ordinal);
        Assert.Contains("context.SetResult(result, resultTypeInfo, resultContract);", result.Generated, StringComparison.Ordinal);
        Assert.Contains("new global::TickerQ.Utilities.Models.TickerResultContract(typeof(global::TestApp.ResultModel).FullName", result.Generated, StringComparison.Ordinal);
        Assert.DoesNotContain("result.job:result:v1", result.Generated, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("int?", "global::System.Nullable<global::System.Int32>")]
    [InlineData("(string Name, int Count)", "global::System.ValueTuple<global::System.String, global::System.Int32>")]
    [InlineData("System.Collections.Generic.Dictionary<string, int?>", "global::System.Collections.Generic.Dictionary<global::System.String, global::System.Nullable<global::System.Int32>>")]
    public void DeclaredResult_RendersNullableTupleAndGenericTypes(string sourceType, string generatedType)
    {
        var source = $$"""
            using System.Threading.Tasks;
            using TickerQ.Utilities.Base;
            namespace TestApp;
            public sealed class Jobs
            {
                [TickerFunction("shape.job", ResultType = typeof({{sourceType}}))]
                public {{sourceType}} Run(TickerFunctionContext context) => default;
            }
            """;

        var result = SchemaTestHarness.Run(source);

        Assert.Empty(result.CompileErrors);
        Assert.Contains($"typeof({generatedType})", result.Generated, StringComparison.Ordinal);
        Assert.Contains($"GetResultTypeInfo<{generatedType}>", result.Generated, StringComparison.Ordinal);
    }

    [Fact]
    public void FunctionWithoutDeclaredResult_RemainsUnchanged()
    {
        const string source = """
            using System.Threading.Tasks;
            using TickerQ.Utilities.Base;
            namespace TestApp;
            public sealed class Jobs
            {
                [TickerFunction("legacy.job")]
                public Task Run(TickerFunctionContext context) => Task.CompletedTask;
            }
            """;

        var result = SchemaTestHarness.Run(source);

        Assert.Empty(result.CompileErrors);
        Assert.DoesNotContain("RegisterResultTypeInfoResolver", result.Generated, StringComparison.Ordinal);
        Assert.DoesNotContain("TickerResultContract", result.Generated, StringComparison.Ordinal);
        Assert.DoesNotContain("SetResult", result.Generated, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("void", "void")]
    [InlineData("System.Collections.Generic.List<>", "System.Collections.Generic.List<T>")]
    public void InvalidDeclaredResult_ReportsDiagnostic(string declaredType, string returnType)
    {
        var source = $$"""
            using TickerQ.Utilities.Base;
            namespace TestApp;
            public sealed class Jobs
            {
                [TickerFunction("invalid.job", ResultType = typeof({{declaredType}}))]
                public {{returnType}} Run(TickerFunctionContext context) { {{(returnType == "void" ? "" : "return default;")}} }
            }
            """;

        var result = SchemaTestHarness.Run(source);

        Assert.Contains(result.GeneratorDiagnostics, d => d.Id == "TQ014");
    }

    [Fact]
    public void DeclaredResultThatDoesNotMatchReturnType_ReportsDiagnostic()
    {
        const string source = """
            using TickerQ.Utilities.Base;
            namespace TestApp;
            public sealed class Jobs
            {
                [TickerFunction("mismatch.job", ResultType = typeof(string))]
                public int Run(TickerFunctionContext context) => 42;
            }
            """;

        var result = SchemaTestHarness.Run(source);

        Assert.Contains(result.GeneratorDiagnostics, d => d.Id == "TQ015");
    }

    [Theory]
    [InlineData("object")]
    [InlineData("long")]
    [InlineData("ulong")]
    public void UnsupportedResultWireShape_ReportsDiagnostic_AndDoesNotRegisterSchemaLessContract(string resultType)
    {
        var source = $$"""
            using TickerQ.Utilities.Base;
            namespace TestApp;
            public sealed class Jobs
            {
                [TickerFunction("unsupported-result.job", ResultType = typeof({{resultType}}))]
                public {{resultType}} Run(TickerFunctionContext context) => default;
            }
            """;

        var result = SchemaTestHarness.Run(source);

        var diagnostic = Assert.Single(result.GeneratorDiagnostics, d => d.Id == "TQ016");
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.DoesNotContain("TickerResultContract(typeof", result.Generated, StringComparison.Ordinal);
        Assert.DoesNotContain("RegisterResultTypeInfoResolver", result.Generated, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("public sealed record ResultModel(long Value);", "ResultModel")]
    [InlineData("public sealed record ResultModel(ulong Value);", "ResultModel")]
    [InlineData("public enum ResultModel : long { Value = 1 }", "ResultModel")]
    [InlineData("public enum ResultModel : ulong { Value = 1 }", "ResultModel")]
    [InlineData("public interface IResult { int Value { get; } }", "IResult")]
    [InlineData("public abstract class ResultModel { public int Value { get; set; } }", "ResultModel")]
    [InlineData("[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter))] public enum ResultModel { Value }", "ResultModel")]
    public void NestedOrPolymorphicUnsupportedResult_ReportsTQ016(string declaration, string resultType)
    {
        var source = $$"""
            using TickerQ.Utilities.Base;
            namespace TestApp;
            {{declaration}}
            public sealed class Jobs
            {
                [TickerFunction("unsupported-nested.job", ResultType = typeof({{resultType}}))]
                public {{resultType}} Run(TickerFunctionContext context) => default;
            }
            """;

        var result = SchemaTestHarness.Run(source);

        Assert.Contains(result.GeneratorDiagnostics, d => d.Id == "TQ016" && d.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain("TickerResultContract(typeof", result.Generated, StringComparison.Ordinal);
    }

    [Fact]
    public void CustomConverterResult_ReportsTQ016()
    {
        const string source = """
            using System;
            using System.Text.Json;
            using System.Text.Json.Serialization;
            using TickerQ.Utilities.Base;
            namespace TestApp;
            [JsonConverter(typeof(ResultConverter))]
            public sealed record ResultModel(int Value);
            public sealed class ResultConverter : JsonConverter<ResultModel>
            {
                public override ResultModel Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options) => new(0);
                public override void Write(Utf8JsonWriter writer, ResultModel value, JsonSerializerOptions options) => writer.WriteStringValue(value.Value.ToString());
            }
            public sealed class Jobs
            {
                [TickerFunction("converter-result.job", ResultType = typeof(ResultModel))]
                public ResultModel Run(TickerFunctionContext context) => new(1);
            }
            """;

        var result = SchemaTestHarness.Run(source);

        Assert.Contains(result.GeneratorDiagnostics, d => d.Id == "TQ016" && d.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain("TickerResultContract(typeof", result.Generated, StringComparison.Ordinal);
    }

    [Fact]
    public void DeclaredResult_ValueTaskOfT_IsAwaitedAndPublished()
    {
        const string source = """
            using System.Threading.Tasks;
            using TickerQ.Utilities.Base;
            namespace TestApp;
            public sealed record ResultModel(int Value);
            public sealed class Jobs
            {
                [TickerFunction("valuetask.job", ResultType = typeof(ResultModel))]
                public ValueTask<ResultModel> Run(TickerFunctionContext context) =>
                    ValueTask.FromResult(new ResultModel(42));
            }
            """;

        var result = SchemaTestHarness.Run(source);

        Assert.Empty(result.GeneratorDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Empty(result.CompileErrors);
        Assert.Contains("var result = await", result.Generated, StringComparison.Ordinal);
        Assert.Contains("context.SetResult(result, resultTypeInfo, resultContract);", result.Generated, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Task")]
    [InlineData("ValueTask")]
    public void DeclaredResult_NonGenericAwaitable_IsRejected(string returnType)
    {
        var source = $$"""
            using System.Threading.Tasks;
            using TickerQ.Utilities.Base;
            namespace TestApp;
            public sealed class Jobs
            {
                [TickerFunction("awaitable.job", ResultType = typeof({{returnType}}))]
                public {{returnType}} Run(TickerFunctionContext context) => default;
            }
            """;

        var result = SchemaTestHarness.Run(source);

        Assert.Contains(result.GeneratorDiagnostics, d => d.Id == "TQ015");
    }
}
