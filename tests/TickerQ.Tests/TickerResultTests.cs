using TickerQ.Utilities.Models;

namespace TickerQ.Tests;

public class TickerResultTests
{
    // TickerResult has internal constructors, so we test via internal access (InternalsVisibleTo)
    // The test project references TickerQ.Utilities, which has InternalsVisibleTo for the test assembly.
    // If not accessible, we can use reflection.

    [Fact]
    public void Constructor_WithResult_SetsIsSucceeded()
    {
        var result = CreateSuccessResult("value");

        Assert.True(result.IsSucceeded);
        Assert.Equal("value", result.Result);
        Assert.Null(result.Exception);
    }

    [Fact]
    public void Constructor_WithException_SetsIsSucceededFalse()
    {
        var ex = new InvalidOperationException("fail");
        var result = CreateFailureResult(ex);

        Assert.False(result.IsSucceeded);
        Assert.Same(ex, result.Exception);
        Assert.Null(result.Result);
    }

    [Fact]
    public void Constructor_WithAffectedRows_SetsIsSucceeded()
    {
        var result = CreateAffectedRowsResult(5);

        Assert.True(result.IsSucceeded);
        Assert.Equal(5, result.AffectedRows);
    }

    [Fact]
    public void Constructor_WithResultAndAffectedRows_SetsBoth()
    {
        var result = CreateResultWithRows("value", 3);

        Assert.True(result.IsSucceeded);
        Assert.Equal("value", result.Result);
        Assert.Equal(3, result.AffectedRows);
    }

    // Use reflection to create instances since constructors are internal
    private static TickerResult<string> CreateSuccessResult(string value)
    {
        var ctor = typeof(TickerResult<string>)
            .GetConstructor(
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic,
                null,
                [typeof(string)],
                null);
        return (TickerResult<string>)ctor!.Invoke([value]);
    }

    private static TickerResult<string> CreateFailureResult(Exception exception)
    {
        var ctor = typeof(TickerResult<string>)
            .GetConstructor(
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic,
                null,
                [typeof(Exception)],
                null);
        return (TickerResult<string>)ctor!.Invoke([exception]);
    }

    private static TickerResult<string> CreateAffectedRowsResult(int rows)
    {
        var ctor = typeof(TickerResult<string>)
            .GetConstructor(
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic,
                null,
                [typeof(int)],
                null);
        return (TickerResult<string>)ctor!.Invoke([rows]);
    }

    private static TickerResult<string> CreateResultWithRows(string value, int rows)
    {
        var ctor = typeof(TickerResult<string>)
            .GetConstructor(
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic,
                null,
                [typeof(string), typeof(int)],
                null);
        return (TickerResult<string>)ctor!.Invoke([value, rows]);
    }
}
