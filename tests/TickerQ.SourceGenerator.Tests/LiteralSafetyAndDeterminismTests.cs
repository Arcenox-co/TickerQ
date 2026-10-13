using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using TickerQ.SourceGenerator;

namespace TickerQ.SourceGenerator.Tests;

public class LiteralSafetyAndDeterminismTests
{
    [Fact]
    public void Generator_CompilesAndPreservesHostileAttributeStrings()
    {
        const string functionName = "job\"\\\r\n\u2028\u2029done";
        const string cron = "* * \"\\\r\n\u2028\u2029 * * *";
        const string serviceKey = "key\"\\\r\n\u2028\u2029done";
        var source = $$"""
            using TickerQ.Utilities.Base;
            using Microsoft.Extensions.DependencyInjection;
            using System.Threading.Tasks;

            namespace Hostile;

            public interface IService { }

            public sealed class Jobs
            {
                public Jobs([FromKeyedServices({{SymbolDisplay.FormatLiteral(serviceKey, true)}})] IService service) { }

                [TickerFunction({{SymbolDisplay.FormatLiteral(functionName, true)}}, {{SymbolDisplay.FormatLiteral(cron, true)}})]
                public Task Run() => Task.CompletedTask;
            }
            """;

        var result = Run(source);

        AssertNoCompileErrors(result.Output, result.GeneratorDiagnostics);
        var literalValues = result.GeneratedTrees
            .SelectMany(tree => tree.GetRoot().DescendantTokens())
            .Where(token => token.IsKind(SyntaxKind.StringLiteralToken))
            .Select(token => token.ValueText)
            .ToList();
        Assert.Contains(functionName, literalValues);
        Assert.Contains(cron, literalValues);
        Assert.Contains(serviceKey, literalValues);
    }

    [Fact]
    public void Generator_OutputIsIdenticalWhenSyntaxTreeOrderIsReversed()
    {
        const string alpha = """
            using TickerQ.Utilities.Base;
            using System.Threading.Tasks;
            namespace A;
            public sealed class Alpha
            {
                [TickerFunction("z-name")]
                public Task Zed() => Task.CompletedTask;
                [TickerFunction("a-name")]
                public Task Aye() => Task.CompletedTask;
            }
            """;
        const string zeta = """
            using TickerQ.Utilities.Base;
            using System.Threading.Tasks;
            namespace Z;
            public sealed class Zeta
            {
                [TickerFunction("middle")]
                public Task Middle() => Task.CompletedTask;
            }
            """;
        const string alphaReversed = """
            using TickerQ.Utilities.Base;
            using System.Threading.Tasks;
            namespace A;
            public sealed class Alpha
            {
                [TickerFunction("a-name")]
                public Task Aye() => Task.CompletedTask;
                [TickerFunction("z-name")]
                public Task Zed() => Task.CompletedTask;
            }
            """;

        var forward = Run(alpha, zeta);
        var reverse = Run(zeta, alphaReversed);

        AssertNoCompileErrors(forward.Output, forward.GeneratorDiagnostics);
        AssertNoCompileErrors(reverse.Output, reverse.GeneratorDiagnostics);
        Assert.Equal(forward.GeneratedSources, reverse.GeneratedSources);
    }

    private static RunResult Run(params string[] sources)
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.Preview);
        var sourceTrees = sources.Select(source => CSharpSyntaxTree.ParseText(source, parseOptions));
        var stubTree = CSharpSyntaxTree.ParseText(Stub, parseOptions);
        var references = (((string?)AppDomain.CurrentDomain.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))
                ?.Split(Path.PathSeparator) ?? Array.Empty<string>())
            .Where(File.Exists)
            .Select(path => MetadataReference.CreateFromFile(path))
            .ToList();
        var compilation = CSharpCompilation.Create(
            "LiteralSafetyTests",
            sourceTrees.Append(stubTree),
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new[] { new TickerQIncrementalSourceGenerator().AsSourceGenerator() },
            parseOptions: parseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var diagnostics);
        var generatedTrees = output.SyntaxTrees.Skip(compilation.SyntaxTrees.Count()).ToImmutableArray();
        var generatedSources = generatedTrees
            .OrderBy(tree => tree.FilePath, StringComparer.Ordinal)
            .Select(tree => tree.ToString())
            .ToArray();
        return new RunResult(output, diagnostics, generatedTrees, generatedSources);
    }

    private static void AssertNoCompileErrors(Compilation output, ImmutableArray<Diagnostic> generatorDiagnostics)
    {
        var errors = generatorDiagnostics.Concat(output.GetDiagnostics())
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error && diagnostic.Id != "TQ003")
            .ToList();
        Assert.True(errors.Count == 0, "Compilation failed:\n" + string.Join("\n", errors));
    }

    private sealed record RunResult(
        Compilation Output,
        ImmutableArray<Diagnostic> GeneratorDiagnostics,
        ImmutableArray<SyntaxTree> GeneratedTrees,
        string[] GeneratedSources);

    private const string Stub = """
        namespace TickerQ.Utilities.Base
        {
            [System.AttributeUsage(System.AttributeTargets.Method)]
            public sealed class TickerFunctionAttribute : System.Attribute
            {
                public TickerFunctionAttribute(string functionName, string cronExpression = null, int taskPriority = 0, int maxConcurrency = 0) { }
            }
            public class TickerFunctionContext { }
            public class TickerFunctionContext<T> : TickerFunctionContext { }
        }
        namespace TickerQ.Utilities.Enums
        {
            public enum TickerTaskPriority { Normal = 0 }
        }
        namespace TickerQ.Utilities.Models
        {
            public sealed class TickerRequestExample
            {
                public TickerRequestExample(string key, string summary, string valueJson) { }
            }
            public sealed class TickerRequestContract
            {
                public TickerRequestContract(string typeName, string mediaType = "application/json", bool required = true,
                    string schemaDialect = "https://json-schema.org/draft/2020-12/schema", string schemaJson = null,
                    System.Collections.Generic.IReadOnlyList<TickerRequestExample> examples = null) { }
            }
            public sealed class TickerFunctionDescriptor
            {
                public TickerFunctionDescriptor(string functionName,
                    TickerQ.Utilities.Enums.TickerTaskPriority priority = TickerQ.Utilities.Enums.TickerTaskPriority.Normal,
                    string cronExpression = null, int contractVersion = 1, TickerRequestContract request = null) { }
            }
        }
        namespace TickerQ.Utilities
        {
            public delegate System.Threading.Tasks.Task TickerFunctionDelegate(
                System.Threading.CancellationToken cancellationToken, System.IServiceProvider serviceProvider,
                TickerQ.Utilities.Base.TickerFunctionContext context);
            public static class TickerFunctionProvider
            {
                public static void RegisterFunctions(System.Collections.Generic.Dictionary<string,
                    (string, TickerQ.Utilities.Enums.TickerTaskPriority, TickerFunctionDelegate, int)> functions, int count) { }
                public static void RegisterRequestType(System.Collections.Generic.Dictionary<string, (string, System.Type)> types, int count) { }
                public static void RegisterDescriptors(System.Collections.Generic.Dictionary<string,
                    TickerQ.Utilities.Models.TickerFunctionDescriptor> descriptors, string origin = null) { }
            }
            public sealed class TickerFunctionRef
            {
                public TickerFunctionRef(string name) { }
            }
            public sealed class TickerFunctionRef<T>
            {
                public TickerFunctionRef(string name) { }
            }
        }
        namespace Microsoft.Extensions.DependencyInjection
        {
            [System.AttributeUsage(System.AttributeTargets.Parameter)]
            public sealed class FromKeyedServicesAttribute : System.Attribute
            {
                public FromKeyedServicesAttribute(object key) { }
            }
            public static class ServiceProviderExtensions
            {
                public static T GetService<T>(this System.IServiceProvider provider) => default;
                public static T GetKeyedService<T>(this System.IServiceProvider provider, object key) => default;
            }
        }
        """;
}
