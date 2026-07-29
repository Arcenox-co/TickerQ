using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using TickerQ.SourceGenerator;

namespace TickerQ.SourceGenerator.Tests;

/// <summary>
/// Verifies the generator emits canonical descriptor registrations and that the generated code
/// actually COMPILES against stubs mirroring the canonical model/registration API — not merely
/// that certain strings are present.
/// </summary>
public class DescriptorGenerationTests
{
    // Stubs now include the canonical models + RegisterDescriptors so generated descriptor code compiles.
    private const string StubTypes = @"
namespace TickerQ.Utilities.Base
{
    [System.AttributeUsage(System.AttributeTargets.Method)]
    public class TickerFunctionAttribute : System.Attribute
    {
        public TickerFunctionAttribute(string functionName, string cronExpression = null, int taskPriority = 0, int maxConcurrency = 0) { }
    }

    public class TickerFunctionContext { }
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
        public TickerRequestExample(string key, string summary, string valueJson) { }
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
        { TypeName = typeName; }
        public string TypeName { get; }
    }

    public sealed class TickerFunctionDescriptor
    {
        public TickerFunctionDescriptor(
            string functionName,
            TickerQ.Utilities.Enums.TickerTaskPriority priority = TickerQ.Utilities.Enums.TickerTaskPriority.Normal,
            string cronExpression = null,
            int contractVersion = 1,
            TickerRequestContract request = null)
        {
            FunctionName = functionName; Priority = priority; CronExpression = cronExpression;
            ContractVersion = contractVersion; Request = request;
        }
        public string FunctionName { get; }
        public TickerQ.Utilities.Enums.TickerTaskPriority Priority { get; }
        public string CronExpression { get; }
        public int ContractVersion { get; }
        public TickerRequestContract Request { get; }
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

    private const string Source = @"
using System;
using System.Threading;
using System.Threading.Tasks;
using TickerQ.Utilities.Base;

namespace TestApp
{
    public class OrderRequest { public string Id { get; set; } }

    public class Jobs
    {
        [TickerFunction(""Typed"")]
        public Task Typed(TickerFunctionContext<OrderRequest> context, CancellationToken ct) => Task.CompletedTask;

        [TickerFunction(""RequestLess"")]
        public Task RequestLess(TickerFunctionContext context, CancellationToken ct) => Task.CompletedTask;
    }
}";

    [Fact]
    public void Generator_EmitsDescriptorRegistrations_AndOutputCompiles()
    {
        var compilation = CreateCompilation(Source);
        var generator = new TickerQIncrementalSourceGenerator();
        var parseOptions = (CSharpParseOptions)compilation.SyntaxTrees.First().Options;
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new[] { generator.AsSourceGenerator() },
            parseOptions: parseOptions);

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var genDiagnostics);

        // Generator itself reported no errors.
        Assert.DoesNotContain(genDiagnostics, d => d.Severity == DiagnosticSeverity.Error);

        // The FULL updated compilation (source + stubs + generated descriptor code) compiles clean.
        var compileErrors = outputCompilation.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();
        Assert.True(compileErrors.Count == 0,
            "Generated code failed to compile:\n" + string.Join("\n", compileErrors.Select(e => e.ToString())));

        // And the generated descriptor registrations cover typed + request-less shapes.
        var generated = outputCompilation.SyntaxTrees
            .Select(t => t.ToString())
            .First(s => s.Contains("TickerQInstanceFactory"));

        Assert.Contains("RegisterDescriptors(", generated);
        Assert.Contains("new global::TickerQ.Utilities.Models.TickerFunctionDescriptor(\"Typed\"", generated);
        // Typed function now carries an embedded compile-time schema + default example (Task 4).
        Assert.Contains("new global::TickerQ.Utilities.Models.TickerRequestContract(typeof(global::TestApp.OrderRequest).FullName, schemaJson:", generated);
        Assert.Contains("new global::TickerQ.Utilities.Models.TickerFunctionDescriptor(\"RequestLess\"", generated);
    }

    private static CSharpCompilation CreateCompilation(string source)
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.Preview);
        var syntaxTree = CSharpSyntaxTree.ParseText(source, parseOptions);
        var stubTree = CSharpSyntaxTree.ParseText(StubTypes, parseOptions);

        var references = new List<MetadataReference>();
        var trustedPaths = ((string?)AppDomain.CurrentDomain.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))
            ?.Split(Path.PathSeparator) ?? Array.Empty<string>();
        foreach (var path in trustedPaths)
            if (File.Exists(path))
                references.Add(MetadataReference.CreateFromFile(path));

        return CSharpCompilation.Create("TestAssembly",
            new[] { syntaxTree, stubTree },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }
}
