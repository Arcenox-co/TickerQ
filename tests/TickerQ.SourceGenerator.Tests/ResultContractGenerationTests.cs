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
