using TickerQ.Dashboard.Assistant;

namespace TickerQ.Tests;

public sealed class LocalCheckoutSourceContextProviderTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "tickerq-local-source-" + Guid.NewGuid().ToString("N"));

    public LocalCheckoutSourceContextProviderTests()
        => Directory.CreateDirectory(Path.Combine(_root, "Jobs"));

    [Fact]
    public async Task ReturnsBoundedExcerptFromExplicitFunctionMapping()
    {
        var path = Path.Combine(_root, "Jobs", "PaymentJob.cs");
        await File.WriteAllLinesAsync(path, Enumerable.Range(1, 100).Select(i => $"line {i}"));
        var provider = new LocalCheckoutSourceContextProvider(_root, source => source
            .WithRepository("Orders API")
            .WithRevision("abc123")
            .WithContextLines(before: 2, after: 3)
            .MapFunction("Payments.Charge", "Jobs/PaymentJob.cs"));

        var result = await provider.GetSourceContextAsync(new ExecutionSourceContextRequest
        {
            FunctionName = "Payments.Charge",
            SourceFile = "A-Different-File.cs",
            SourceLine = 50,
        });

        Assert.NotNull(result);
        Assert.Equal("Orders API", result.Repository);
        Assert.Equal("abc123", result.Revision);
        Assert.Equal("Jobs/PaymentJob.cs", result.Path);
        Assert.Equal(48, result.StartLine);
        Assert.Equal("line 48\nline 49\nline 50\nline 51\nline 52\nline 53", result.Code);
    }

    [Fact]
    public async Task UsesFilenameMappingAndReturnsNullForUnknownOrOutOfRangeSource()
    {
        await File.WriteAllLinesAsync(Path.Combine(_root, "Jobs", "InvoiceJob.cs"), ["one", "two", "three"]);
        var provider = new LocalCheckoutSourceContextProvider(_root, source => source
            .WithContextLines(1, 1)
            .MapSourceFile("InvoiceJob.cs", "Jobs/InvoiceJob.cs"));

        var result = await provider.GetSourceContextAsync(new ExecutionSourceContextRequest
        {
            FunctionName = "Invoices.Create",
            SourceFile = "InvoiceJob.cs",
            SourceLine = 2,
        });
        Assert.NotNull(result);
        Assert.Equal(1, result.StartLine);
        Assert.Equal("one\ntwo\nthree", result.Code);

        Assert.Null(await provider.GetSourceContextAsync(new ExecutionSourceContextRequest
        {
            SourceFile = "OtherJob.cs",
            SourceLine = 2,
        }));
        Assert.Null(await provider.GetSourceContextAsync(new ExecutionSourceContextRequest
        {
            SourceFile = "InvoiceJob.cs",
            SourceLine = 99,
        }));
    }

    [Fact]
    public void RejectsTraversalAbsolutePathsAndUnboundedConfiguration()
    {
        Assert.Throws<ArgumentException>(() => new LocalCheckoutSourceContextProvider(_root,
            source => source.MapSourceFile("Job.cs", "../Job.cs")));
        Assert.Throws<ArgumentException>(() => new LocalCheckoutSourceContextProvider(_root,
            source => source.MapSourceFile("nested/Job.cs", "Jobs/Job.cs")));
        Assert.Throws<ArgumentException>(() => new LocalCheckoutSourceContextProvider(_root,
            source => source.MapSourceFile("nested\\Job.cs", "Jobs/Job.cs")));
        Assert.Throws<ArgumentOutOfRangeException>(() => new LocalCheckoutSourceContextProvider(_root,
            source => source.WithContextLines(101, 0).MapSourceFile("Job.cs", "Jobs/Job.cs")));
        Assert.Throws<InvalidOperationException>(() =>
            new LocalCheckoutSourceContextProvider(_root, _ => { }));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
