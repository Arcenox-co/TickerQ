namespace TickerQ.SourceGenerator.Tests;

public sealed class RequestTypeSyntaxGenerationTests
{
    private const string ConsumerSource = """
        #nullable enable
        using System.Collections.Generic;
        using System.Threading.Tasks;
        using TickerQ.Utilities.Base;

        namespace TestApp
        {
            public sealed class Request { }

            public sealed class Outer
            {
                public sealed class Inner { }
            }

            namespace @event
            {
                public sealed class @class { }
            }

            public static class Jobs
            {
                [TickerFunction("nullable")]
                public static Task Nullable(TickerFunctionContext<Request?> context) => Task.CompletedTask;

                [TickerFunction("tuple")]
                public static Task Tuple(TickerFunctionContext<(int Id, string? Name)> context) => Task.CompletedTask;

                [TickerFunction("nested.generic")]
                public static Task NestedGeneric(
                    TickerFunctionContext<Dictionary<string, List<Outer.Inner?>>> context) => Task.CompletedTask;

                [TickerFunction("array")]
                public static Task Array(TickerFunctionContext<Outer.Inner?[]> context) => Task.CompletedTask;

                [TickerFunction("escaped")]
                public static Task Escaped(TickerFunctionContext<@event.@class> context) => Task.CompletedTask;

                [TickerFunction("nested.type")]
                public static Task NestedType(TickerFunctionContext<Outer.Inner> context) => Task.CompletedTask;
            }
        }
        """;

    [Fact]
    public void NullableReferenceRequest_UsesRuntimeTypeWithoutNullableAnnotation()
    {
        var generated = GenerateCompilingConsumer();

        Assert.Contains("GetRequestTypeInfo<global::TestApp.Request>", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("global::TestApp.Request?", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void TupleRequest_UsesFullyQualifiedValueTupleSyntax()
    {
        var generated = GenerateCompilingConsumer();

        Assert.Contains(
            "GetRequestTypeInfo<global::System.ValueTuple<global::System.Int32, global::System.String>>",
            generated,
            StringComparison.Ordinal);
    }

    [Fact]
    public void NestedGenericRequest_QualifiesEveryNamedTypeAndRemovesNullableAnnotations()
    {
        var generated = GenerateCompilingConsumer();

        Assert.Contains(
            "GetRequestTypeInfo<global::System.Collections.Generic.Dictionary<global::System.String, global::System.Collections.Generic.List<global::TestApp.Outer.Inner>>>",
            generated,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ArrayRequest_PreservesArraySyntaxAndNestedTypeQualification()
    {
        var generated = GenerateCompilingConsumer();

        Assert.Contains("GetRequestTypeInfo<global::TestApp.Outer.Inner[]>", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void EscapedIdentifiers_ArePreservedInGeneratedTypeSyntax()
    {
        var generated = GenerateCompilingConsumer();

        Assert.Contains("GetRequestTypeInfo<global::TestApp.@event.@class>", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void NestedRequestType_IncludesContainingType()
    {
        var generated = GenerateCompilingConsumer();

        Assert.Contains("GetRequestTypeInfo<global::TestApp.Outer.Inner>", generated, StringComparison.Ordinal);
    }

    private static string GenerateCompilingConsumer()
    {
        var result = SchemaTestHarness.Run(ConsumerSource);

        Assert.Empty(result.CompileErrors);
        return result.Generated;
    }
}
