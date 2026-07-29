namespace TickerQ.SourceGenerator.Tests;

public class RuntimeJsonTypeInfoGenerationTests
{
    [Fact]
    public void TypedFunction_RegistersAndConsumesFinalRuntimeJsonTypeInfo()
    {
        const string source = """
            using System.Threading.Tasks;
            using TickerQ.Utilities.Base;

            namespace TestApp;

            public sealed class ReqModel
            {
                public string Id { get; set; }
            }

            public sealed class Jobs
            {
                [TickerFunction("typed.job")]
                public Task Run(TickerFunctionContext<ReqModel> context) => Task.CompletedTask;
            }
            """;

        var result = SchemaTestHarness.Run(source);

        Assert.Empty(result.CompileErrors);
        Assert.Contains("RegisterRequestTypeInfoResolver", result.Generated, StringComparison.Ordinal);
        Assert.Contains("options.GetTypeInfo(typeof(global::TestApp.ReqModel))", result.Generated, StringComparison.Ordinal);
        Assert.Contains("GetRequestTypeInfo<global::TestApp.ReqModel>", result.Generated, StringComparison.Ordinal);
        Assert.Contains("ToGenericContextAsync<global::TestApp.ReqModel>", result.Generated, StringComparison.Ordinal);
    }
}
