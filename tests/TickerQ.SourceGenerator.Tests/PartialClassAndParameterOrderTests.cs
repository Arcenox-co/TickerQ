using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace TickerQ.SourceGenerator.Tests;

public class PartialClassAndParameterOrderTests
{
    [Fact]
    public void PartialJob_GeneratesOneFactory_AndFindsConstructorInAnotherDeclaration()
    {
        var result = SchemaTestHarness.Run("""
            using System.Threading;
            using System.Threading.Tasks;
            using TickerQ.Utilities.Base;
            namespace TestApp {
                public partial class Jobs {
                    [TickerFunction("One")]
                    public Task One(TickerFunctionContext context, CancellationToken token) => Task.CompletedTask;
                }
                partial class Jobs {
                    [TickerFunction("Two")]
                    public Task Two(CancellationToken token, TickerFunctionContext context) => Task.CompletedTask;
                }
                public sealed class Dependency { }
                partial class Jobs { public Jobs(Dependency dependency) { } }
            }
            namespace Microsoft.Extensions.DependencyInjection {
                public static class ServiceExtensions {
                    public static T GetService<T>(this System.IServiceProvider provider) => default;
                }
            }
            """);
        Assert.Empty(result.CompileErrors);
        var factories = CSharpSyntaxTree.ParseText(result.Generated).GetRoot().DescendantNodes()
            .OfType<MethodDeclarationSyntax>().Where(x => x.Identifier.ValueText == "Create_TestApp_Jobs");
        Assert.Single(factories);
        Assert.Contains("GetService<global::TestApp.Dependency>()", result.Generated);
    }

    [Theory]
    [InlineData("TickerFunctionContext", "")]
    [InlineData("TickerFunctionContext<Request>", "public class Request { public int Value { get; set; } }")]
    public void TokenBeforeContext_UsesDeclaredArgumentOrder(string contextType, string model)
    {
        var result = SchemaTestHarness.Run($$"""
            using System.Threading;
            using System.Threading.Tasks;
            using TickerQ.Utilities.Base;
            namespace TestApp {
                {{model}}
                public static class Jobs {
                    [TickerFunction("One")]
                    public static Task One(CancellationToken token, {{contextType}} context) => Task.CompletedTask;
                }
            }
            """);
        Assert.Empty(result.CompileErrors);
        Assert.Contains(contextType.Contains('<') ? ".One(cancellationToken, genericContext)" : ".One(cancellationToken, context)", result.Generated);
    }
}
