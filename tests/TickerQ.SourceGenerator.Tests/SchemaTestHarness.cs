using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using TickerQ.SourceGenerator;

namespace TickerQ.SourceGenerator.Tests;

/// <summary>
/// Shared harness for compile-time schema-generation tests. The stub mirrors the real canonical
/// registration API (TickerRequestContract convenience constructor + TickerRequestExample) so emitted
/// descriptor code with an embedded schema + example actually COMPILES, not merely matches strings.
/// Real System.Text.Json / DataAnnotations attributes resolve from the trusted platform assemblies.
/// </summary>
internal static class SchemaTestHarness
{
    public const string Stub = @"
namespace TickerQ.Utilities.Base
{
    [System.AttributeUsage(System.AttributeTargets.Method)]
    public class TickerFunctionAttribute : System.Attribute
    {
        public TickerFunctionAttribute(string functionName, string cronExpression = null, int taskPriority = 0, int maxConcurrency = 0) { }
        public System.Type ResultType { get; set; }
    }

    public class TickerFunctionContext
    {
        public void SetResult<T>(T value, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo, TickerQ.Utilities.Models.TickerResultContract contract) { }
    }
    public class TickerFunctionContext<T> : TickerFunctionContext { public T Request { get; } }
}

namespace TickerQ.Utilities.Enums
{
    public enum TickerTaskPriority { Normal = 0 }
}

namespace TickerQ.Utilities.Models
{
    public sealed class TickerRequestExample
    {
        public TickerRequestExample(string key, string summary, string valueJson) { Key = key; Summary = summary; }
        public string Key { get; }
        public string Summary { get; }
    }

    public sealed class TickerRequestContract
    {
        public TickerRequestContract(
            string typeName,
            string mediaType = ""application/json"",
            bool required = true,
            string schemaDialect = ""https://json-schema.org/draft/2020-12/schema"",
            string schemaJson = null,
            System.Collections.Generic.IReadOnlyList<TickerRequestExample> examples = null)
        {
            TypeName = typeName; SchemaJson = schemaJson;
        }
        public string TypeName { get; }
        public string SchemaJson { get; }
    }

    public sealed class TickerFunctionDescriptor
    {
        public TickerFunctionDescriptor(
            string functionName,
            TickerQ.Utilities.Enums.TickerTaskPriority priority = TickerQ.Utilities.Enums.TickerTaskPriority.Normal,
            string cronExpression = null,
            int contractVersion = 1,
            TickerRequestContract request = null,
            TickerResultContract result = null)
        {
            FunctionName = functionName; Priority = priority; CronExpression = cronExpression;
            ContractVersion = contractVersion; Request = request; Result = result;
        }
        public string FunctionName { get; }
        public TickerQ.Utilities.Enums.TickerTaskPriority Priority { get; }
        public string CronExpression { get; }
        public int ContractVersion { get; }
        public TickerRequestContract Request { get; }
        public TickerResultContract Result { get; }
    }

    public sealed class TickerResultContract
    {
        public TickerResultContract(string typeName, string mediaType = ""application/json"", int contractVersion = 1, string schemaDialect = ""https://json-schema.org/draft/2020-12/schema"", string schemaJson = null)
        {
            TypeName = typeName; MediaType = mediaType; ContractVersion = contractVersion; SchemaJson = schemaJson;
        }
        public string TypeName { get; }
        public string MediaType { get; }
        public int ContractVersion { get; }
        public string SchemaJson { get; }
    }
}

namespace TickerQ.Utilities
{
    public delegate System.Threading.Tasks.Task TickerFunctionDelegate(
        System.Threading.CancellationToken cancellationToken,
        System.IServiceProvider serviceProvider,
        TickerQ.Utilities.Base.TickerFunctionContext context);

    public static class TickerFunctionProvider
    {
        public static void RegisterFunctions(
            System.Collections.Generic.Dictionary<string, (string, TickerQ.Utilities.Enums.TickerTaskPriority, TickerFunctionDelegate, int)> f, int c) { }
        public static void RegisterRequestType(
            System.Collections.Generic.Dictionary<string, (string, System.Type)> t, int c) { }
        public static void RegisterRequestTypeInfoResolver(
            System.Collections.Generic.IDictionary<string, (System.Type, System.Func<System.Text.Json.JsonSerializerOptions, System.Text.Json.Serialization.Metadata.JsonTypeInfo>)> t) { }
        public static System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> GetRequestTypeInfo<T>(string functionName) => default;
        public static void RegisterResultTypeInfoResolver(
            System.Collections.Generic.IDictionary<string, (System.Type, System.Func<System.Text.Json.JsonSerializerOptions, System.Text.Json.Serialization.Metadata.JsonTypeInfo>)> t) { }
        public static System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> GetResultTypeInfo<T>(string functionName) => default;
        public static TickerQ.Utilities.Models.TickerResultContract GetResultContract(string functionName) => default;
        public static void RegisterDescriptors(
            System.Collections.Generic.Dictionary<string, TickerQ.Utilities.Models.TickerFunctionDescriptor> d, string origin = null) { }
    }

    public static class TickerRequestProvider
    {
        public static System.Threading.Tasks.Task<TickerQ.Utilities.Base.TickerFunctionContext<T>> ToGenericContextAsync<T>(
            TickerQ.Utilities.Base.TickerFunctionContext ctx, System.Threading.CancellationToken ct) => default;
        public static System.Threading.Tasks.Task<TickerQ.Utilities.Base.TickerFunctionContext<T>> ToGenericContextAsync<T>(
            TickerQ.Utilities.Base.TickerFunctionContext ctx, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo, System.Threading.CancellationToken ct) => default;
    }
}

namespace Microsoft.Extensions.DependencyInjection
{
}
";

    public sealed class GenResult
    {
        public string Generated { get; init; } = "";
        public Compilation Output { get; init; } = null!;
        public ImmutableArray<Diagnostic> GeneratorDiagnostics { get; init; }

        public IEnumerable<Diagnostic> CompileErrors =>
            Output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error);

        /// <summary>The embedded schema JSON decoded through Roslyn's literal value semantics.</summary>
        public string? SchemaFor(string requestTypeFullName)
        {
            var root = CSharpSyntaxTree.ParseText(Generated).GetRoot();
            foreach (var creation in root.DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.ObjectCreationExpressionSyntax>())
            {
                if (!creation.ToString().Contains($"typeof({requestTypeFullName}).FullName", StringComparison.Ordinal))
                    continue;

                var schemaArgument = creation.ArgumentList?.Arguments.FirstOrDefault(argument =>
                    argument.NameColon?.Name.Identifier.ValueText == "schemaJson");
                if (schemaArgument?.Expression is Microsoft.CodeAnalysis.CSharp.Syntax.LiteralExpressionSyntax literal)
                    return literal.Token.ValueText;
            }

            return null;
        }

        /// <summary>The generated default example JSON decoded through Roslyn's literal semantics.</summary>
        public string? ExampleForDefault()
        {
            var root = CSharpSyntaxTree.ParseText(Generated).GetRoot();
            foreach (var creation in root.DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.ObjectCreationExpressionSyntax>())
            {
                if (!creation.Type.ToString().EndsWith("TickerRequestExample", StringComparison.Ordinal))
                    continue;
                var arguments = creation.ArgumentList?.Arguments;
                if (arguments == null || arguments.Value.Count < 3) continue;
                if (arguments.Value[0].Expression is Microsoft.CodeAnalysis.CSharp.Syntax.LiteralExpressionSyntax key
                    && key.Token.ValueText == "default"
                    && arguments.Value[2].Expression is Microsoft.CodeAnalysis.CSharp.Syntax.LiteralExpressionSyntax value)
                    return value.Token.ValueText;
            }
            return null;
        }
    }

    public static GenResult Run(string source)
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.Preview);
        var syntaxTree = CSharpSyntaxTree.ParseText(source, parseOptions);
        var stubTree = CSharpSyntaxTree.ParseText(Stub, parseOptions);

        var references = new List<MetadataReference>();
        var trustedPaths = ((string?)AppDomain.CurrentDomain.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))
            ?.Split(Path.PathSeparator) ?? Array.Empty<string>();
        foreach (var path in trustedPaths)
            if (File.Exists(path))
                references.Add(MetadataReference.CreateFromFile(path));

        var compilation = CSharpCompilation.Create("TestAssembly",
            new[] { syntaxTree, stubTree },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var generator = new TickerQIncrementalSourceGenerator();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new[] { generator.AsSourceGenerator() },
            parseOptions: parseOptions);

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var diagnostics);

        var generated = output.SyntaxTrees
            .Select(t => t.ToString())
            .FirstOrDefault(s => s.Contains("TickerQInstanceFactory")) ?? "";

        return new GenResult { Generated = generated, Output = output, GeneratorDiagnostics = diagnostics };
    }
}
