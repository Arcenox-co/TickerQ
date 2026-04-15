using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using TickerQ.SourceGenerator;

namespace TickerQ.SourceGenerator.Tests;

/// <summary>
/// Tests that the source generator produces collision-free factory methods
/// when constructor parameters share names with generated identifiers.
/// Regression tests for GitHub issue #812 (CS0136).
/// </summary>
public class ConstructorCollisionTests
{
    /// <summary>
    /// Stub types so the generator can discover [TickerFunction] methods.
    /// </summary>
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
    }

    public static class TickerRequestProvider
    {
        public static System.Threading.Tasks.Task<TickerQ.Utilities.Base.TickerFunctionContext<T>> ToGenericContextAsync<T>(
            TickerQ.Utilities.Base.TickerFunctionContext ctx, System.Threading.CancellationToken ct) => default;
    }
}

namespace TickerQ.Utilities.Enums
{
    public enum TickerTaskPriority { Normal = 0 }
}
";

    #region Issue #812 — IServiceProvider constructor parameter collision

    [Fact]
    public void Constructor_WithIServiceProvider_NoVarServiceProviderCollision()
    {
        var source = @"
using System;
using System.Threading;
using System.Threading.Tasks;
using TickerQ.Utilities.Base;

namespace TestApp
{
    public class SearchIndexJobs
    {
        public SearchIndexJobs(IServiceProvider serviceProvider) { }

        [TickerFunction(""RebuildSearchIndexes"")]
        public Task Execute(TickerFunctionContext context, CancellationToken ct) => Task.CompletedTask;
    }
}";

        var generated = GetGeneratedFactorySource(source);
        Assert.NotEmpty(generated);

        // The factory method parameter must be __serviceProvider, not serviceProvider
        Assert.Contains("(IServiceProvider __serviceProvider)", generated);
        Assert.DoesNotContain("(IServiceProvider serviceProvider)", generated);

        // Service resolution must use __serviceProvider, not the old collision-prone pattern
        Assert.Contains("__serviceProvider.GetService", generated);
        // Must NOT have the collision pattern: 'var serviceProvider = serviceProvider.GetService'
        Assert.DoesNotContain("= serviceProvider.GetService", generated);
    }

    [Fact]
    public void PrimaryConstructor_WithIServiceProvider_NoCollision()
    {
        var source = @"
using System;
using System.Threading;
using System.Threading.Tasks;
using TickerQ.Utilities.Base;

namespace TestApp
{
    public class SearchIndexJobs(IServiceProvider serviceProvider)
    {
        [TickerFunction(""RebuildSearchIndexes"")]
        public Task Execute(TickerFunctionContext context, CancellationToken ct) => Task.CompletedTask;
    }
}";

        var generated = GetGeneratedFactorySource(source);
        Assert.NotEmpty(generated);
        Assert.Contains("(IServiceProvider __serviceProvider)", generated);
        Assert.DoesNotContain("(IServiceProvider serviceProvider)", generated);
        Assert.Contains("__serviceProvider.GetService", generated);
        Assert.DoesNotContain("= serviceProvider.GetService", generated);
    }

    #endregion

    #region Multiple parameters — one collides

    [Fact]
    public void Constructor_WithServiceProviderAndOtherParams_CorrectArgOrder()
    {
        var source = @"
using System;
using System.Threading;
using System.Threading.Tasks;
using TickerQ.Utilities.Base;

namespace TestApp
{
    public interface IMyLogger { }

    public class MyJob
    {
        public MyJob(IServiceProvider serviceProvider, IMyLogger logger) { }

        [TickerFunction(""DoWork"")]
        public Task Run(TickerFunctionContext context, CancellationToken ct) => Task.CompletedTask;
    }
}";

        var generated = GetGeneratedFactorySource(source);
        Assert.NotEmpty(generated);

        // Both parameters must be resolved via __serviceProvider
        Assert.Contains("var serviceProvider = __serviceProvider.GetService", generated);
        Assert.Contains("var logger = __serviceProvider.GetService", generated);

        // Constructor call should use both parameters in order
        Assert.Contains("serviceProvider, logger", generated);
    }

    #endregion

    #region Parameterless constructor — no regression

    [Fact]
    public void ParameterlessConstructor_StillWorks()
    {
        var source = @"
using System;
using System.Threading;
using System.Threading.Tasks;
using TickerQ.Utilities.Base;

namespace TestApp
{
    public class SimpleJob
    {
        [TickerFunction(""SimpleTask"")]
        public Task Run(TickerFunctionContext context, CancellationToken ct) => Task.CompletedTask;
    }
}";

        var generated = GetGeneratedFactorySource(source);
        Assert.NotEmpty(generated);

        // Should still generate a factory method
        Assert.Contains("Create_", generated);
        Assert.Contains("new global::TestApp.SimpleJob()", generated);
    }

    #endregion

    #region Non-colliding parameters — no regression

    [Fact]
    public void Constructor_WithNonCollidingParams_UsesOriginalNames()
    {
        var source = @"
using System;
using System.Threading;
using System.Threading.Tasks;
using TickerQ.Utilities.Base;

namespace TestApp
{
    public interface IMyConfig { }
    public interface IMyLogger { }

    public class NormalJob
    {
        public NormalJob(IMyConfig config, IMyLogger logger) { }

        [TickerFunction(""NormalTask"")]
        public Task Run(TickerFunctionContext context, CancellationToken ct) => Task.CompletedTask;
    }
}";

        var generated = GetGeneratedFactorySource(source);
        Assert.NotEmpty(generated);

        // Parameters should keep their original names
        Assert.Contains("var config = __serviceProvider.GetService", generated);
        Assert.Contains("var logger = __serviceProvider.GetService", generated);
        Assert.Contains("config, logger", generated);
    }

    #endregion

    #region Edge case: parameter named serviceProvider of a non-IServiceProvider type

    [Fact]
    public void Constructor_WithServiceProviderNamedParam_DifferentType_NoCollision()
    {
        var source = @"
using System;
using System.Threading;
using System.Threading.Tasks;
using TickerQ.Utilities.Base;

namespace TestApp
{
    public interface ICustomProvider { }

    public class EdgeJob
    {
        public EdgeJob(ICustomProvider serviceProvider) { }

        [TickerFunction(""EdgeTask"")]
        public Task Run(TickerFunctionContext context, CancellationToken ct) => Task.CompletedTask;
    }
}";

        var generated = GetGeneratedFactorySource(source);
        Assert.NotEmpty(generated);

        // The local 'serviceProvider' should be resolved from '__serviceProvider' — no collision
        Assert.Contains("var serviceProvider = __serviceProvider.GetService", generated);
        Assert.DoesNotContain("(IServiceProvider serviceProvider)", generated);
    }

    #endregion

    #region Delegate body still uses lambda serviceProvider

    [Fact]
    public void DelegateRegistration_PassesLambdaServiceProvider_ToFactoryMethod()
    {
        var source = @"
using System;
using System.Threading;
using System.Threading.Tasks;
using TickerQ.Utilities.Base;

namespace TestApp
{
    public class SomeJob
    {
        public SomeJob(IServiceProvider serviceProvider) { }

        [TickerFunction(""SomeTask"")]
        public Task Run(TickerFunctionContext context, CancellationToken ct) => Task.CompletedTask;
    }
}";

        var generated = GetGeneratedFactorySource(source);
        Assert.NotEmpty(generated);

        // The delegate lambda should still pass 'serviceProvider' (lambda param) to the Create method
        Assert.Contains("(serviceProvider)", generated);
    }

    #endregion

    #region Helpers

    private string GetGeneratedFactorySource(string source)
    {
        var compilation = CreateCompilation(source);
        var generator = new TickerQIncrementalSourceGenerator();
        var parseOptions = (CSharpParseOptions)compilation.SyntaxTrees.First().Options;
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new[] { generator.AsSourceGenerator() },
            parseOptions: parseOptions);
        driver = driver.RunGenerators(compilation);

        var results = driver.GetRunResult();
        var generatedSource = results.Results
            .SelectMany(r => r.GeneratedSources)
            .FirstOrDefault(s => s.HintName == "TickerQInstanceFactory.g.cs");

        return generatedSource.SourceText?.ToString() ?? string.Empty;
    }

    private static CSharpCompilation CreateCompilation(string source)
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.Preview);
        var syntaxTree = CSharpSyntaxTree.ParseText(source, parseOptions);
        var stubTree = CSharpSyntaxTree.ParseText(StubTypes, parseOptions);

        var references = new List<MetadataReference>();

        // Use trusted platform assemblies to get all necessary runtime references
        var trustedPaths = ((string?)AppDomain.CurrentDomain.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))
            ?.Split(Path.PathSeparator) ?? Array.Empty<string>();

        foreach (var path in trustedPaths)
        {
            if (File.Exists(path))
                references.Add(MetadataReference.CreateFromFile(path));
        }

        return CSharpCompilation.Create("TestAssembly",
            new[] { syntaxTree, stubTree },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    #endregion
}
