using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace TickerQ.SourceGenerator.Tests;

/// <summary>
/// Tests that the source generator emits a <c>RegisterPeriodicIntervals()</c> entry
/// for every <c>[TickerFunction(..., PeriodicInterval = "...")]</c> method, and
/// omits the registration entirely when no method declares a periodic interval.
/// </summary>
public class PeriodicIntervalGenerationTests
{
    private const string StubTypes = @"
namespace TickerQ.Utilities.Base
{
    [System.AttributeUsage(System.AttributeTargets.Method)]
    public class TickerFunctionAttribute : System.Attribute
    {
        public TickerFunctionAttribute(string functionName, string cronExpression = null, int taskPriority = 0, int maxConcurrency = 0) { }
        public string PeriodicInterval { get; set; }
    }

    public class TickerFunctionContext { }
    public class TickerFunctionContext<T> : TickerFunctionContext { public T Request { get; } }
}

namespace TickerQ.Utilities.Interfaces
{
    public interface ITickerFunctionBase { }
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
        public static void RegisterPeriodicIntervals(
            System.Collections.Generic.IDictionary<string, System.TimeSpan> intervals) { }
        public static void RegisterPeriodicIntervals(
            System.Collections.Generic.IDictionary<string, System.TimeSpan> intervals, int capacity) { }
    }
}

namespace TickerQ.Utilities.Enums
{
    public enum TickerTaskPriority { Normal = 0 }
}
";

    [Fact]
    public void PeriodicInterval_Set_GeneratesRegisterPeriodicIntervalsEntry()
    {
        var source = @"
using System.Threading;
using System.Threading.Tasks;
using TickerQ.Utilities.Base;

namespace TestApp
{
    public class HeartbeatJob
    {
        [TickerFunction(""Heartbeat"", PeriodicInterval = ""00:00:05"")]
        public Task RunAsync(TickerFunctionContext context, CancellationToken ct) => Task.CompletedTask;
    }
}";

        var generated = GetGeneratedFactorySource(source);

        Assert.Contains("RegisterPeriodicIntervals", generated);
        // Intervals are resolved to ticks at generation time (new TimeSpan(ticks)) so no raw string is
        // embedded into TimeSpan.Parse — that both validates the literal and avoids escaping issues.
        // 00:00:05 = 5s = 50,000,000 ticks.
        Assert.Contains(@"[""Heartbeat""] = new global::System.TimeSpan(50000000L)", generated);
        Assert.Contains("TickerFunctionProvider.RegisterPeriodicIntervals(periodicIntervals, 1)", generated);
    }

    [Fact]
    public void PeriodicInterval_NotSet_DoesNotGenerateEntries()
    {
        var source = @"
using System.Threading;
using System.Threading.Tasks;
using TickerQ.Utilities.Base;

namespace TestApp
{
    public class CronJob
    {
        [TickerFunction(""DailyJob"", ""0 0 * * *"")]
        public Task RunAsync(TickerFunctionContext context, CancellationToken ct) => Task.CompletedTask;
    }
}";

        var generated = GetGeneratedFactorySource(source);

        Assert.NotEmpty(generated);
        // The empty body of RegisterPeriodicIntervals() is still emitted, but no
        // dictionary entries / no call to TickerFunctionProvider.RegisterPeriodicIntervals.
        Assert.DoesNotContain("new global::System.TimeSpan(", generated);
        Assert.DoesNotContain("TickerFunctionProvider.RegisterPeriodicIntervals(periodicIntervals", generated);
    }

    [Fact]
    public void MultiplePeriodicMethods_GeneratesAllEntries()
    {
        var source = @"
using System.Threading;
using System.Threading.Tasks;
using TickerQ.Utilities.Base;

namespace TestApp
{
    public class Jobs
    {
        [TickerFunction(""Fast"", PeriodicInterval = ""00:00:01"")]
        public Task FastAsync(TickerFunctionContext context, CancellationToken ct) => Task.CompletedTask;

        [TickerFunction(""Slow"", PeriodicInterval = ""1.00:00:00"")]
        public Task SlowAsync(TickerFunctionContext context, CancellationToken ct) => Task.CompletedTask;
    }
}";

        var generated = GetGeneratedFactorySource(source);

        // 00:00:01 = 10,000,000 ticks; 1.00:00:00 = 1 day = 864,000,000,000 ticks.
        Assert.Contains(@"[""Fast""] = new global::System.TimeSpan(10000000L)", generated);
        Assert.Contains(@"[""Slow""] = new global::System.TimeSpan(864000000000L)", generated);
        Assert.Contains("TickerFunctionProvider.RegisterPeriodicIntervals(periodicIntervals, 2)", generated);
    }

    [Fact]
    public void PeriodicInterval_CoexistsWithCronInOtherMethods()
    {
        // One periodic, one cron — only the periodic one should register an interval.
        var source = @"
using System.Threading;
using System.Threading.Tasks;
using TickerQ.Utilities.Base;

namespace TestApp
{
    public class Mixed
    {
        [TickerFunction(""Periodic"", PeriodicInterval = ""00:00:30"")]
        public Task A(TickerFunctionContext c, CancellationToken ct) => Task.CompletedTask;

        [TickerFunction(""Cron"", ""*/5 * * * *"")]
        public Task B(TickerFunctionContext c, CancellationToken ct) => Task.CompletedTask;
    }
}";

        var generated = GetGeneratedFactorySource(source);

        // 00:00:30 = 300,000,000 ticks.
        Assert.Contains(@"[""Periodic""] = new global::System.TimeSpan(300000000L)", generated);
        Assert.DoesNotContain(@"[""Cron""] = new global::System.TimeSpan", generated);
        Assert.Contains("TickerFunctionProvider.RegisterPeriodicIntervals(periodicIntervals, 1)", generated);
    }

    [Fact]
    public void PeriodicInterval_Invalid_ReportsTq012Diagnostic()
    {
        // "5m" is not a valid TimeSpan. Without validation it compiles and crashes at startup with
        // FormatException; the generator must surface TQ012 instead.
        var source = @"
using System.Threading;
using System.Threading.Tasks;
using TickerQ.Utilities.Base;

namespace TestApp
{
    public class BadJob
    {
        [TickerFunction(""Bad"", PeriodicInterval = ""5m"")]
        public Task RunAsync(TickerFunctionContext context, CancellationToken ct) => Task.CompletedTask;
    }
}";

        var diagnostics = GetGeneratorDiagnostics(source);

        Assert.Contains(diagnostics, d => d.Id == "TQ012");
    }

    [Fact]
    public void PeriodicInterval_Valid_ReportsNoTq012Diagnostic()
    {
        var source = @"
using System.Threading;
using System.Threading.Tasks;
using TickerQ.Utilities.Base;

namespace TestApp
{
    public class GoodJob
    {
        [TickerFunction(""Good"", PeriodicInterval = ""00:05:00"")]
        public Task RunAsync(TickerFunctionContext context, CancellationToken ct) => Task.CompletedTask;
    }
}";

        var diagnostics = GetGeneratorDiagnostics(source);

        Assert.DoesNotContain(diagnostics, d => d.Id == "TQ012");
    }

    #region Helpers

    private static System.Collections.Immutable.ImmutableArray<Diagnostic> GetGeneratorDiagnostics(string source)
    {
        var compilation = CreateCompilation(source);
        var generator = new TickerQIncrementalSourceGenerator();
        var parseOptions = (CSharpParseOptions)compilation.SyntaxTrees.First().Options;
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new[] { generator.AsSourceGenerator() },
            parseOptions: parseOptions);
        driver = driver.RunGenerators(compilation);
        return driver.GetRunResult().Diagnostics;
    }

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

        var references = new System.Collections.Generic.List<MetadataReference>();
        var trustedPaths = ((string?)System.AppDomain.CurrentDomain.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))
            ?.Split(System.IO.Path.PathSeparator) ?? System.Array.Empty<string>();

        foreach (var path in trustedPaths)
        {
            if (System.IO.File.Exists(path))
                references.Add(MetadataReference.CreateFromFile(path));
        }

        return CSharpCompilation.Create("TestAssembly",
            new[] { syntaxTree, stubTree },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    #endregion
}

