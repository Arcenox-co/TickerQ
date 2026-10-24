using System;
using System.Threading;
using System.Threading.Tasks;
using NSubstitute;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Exceptions;

namespace TickerQ.Tests;

/// <summary>
/// A store that inserts zero rows used to be reported as a duplicate even when it had refused
/// the write, for example on a replica whose reconciliation epoch was superseded.
/// </summary>
public partial class TickerManagerTests
{
    [Fact]
    public async Task AddTimeTickerAsync_ZeroRowsAndNoExistingRow_ReportsDeniedWriteNotDuplicate()
    {
        var entity = new TimeTickerEntity { Function = ValidFunctionName, ExecutionTime = FixedUtcNow.AddMinutes(5) };
        _persistenceProvider
            .AddTimeTickers(Arg.Any<TimeTickerEntity[]>(), Arg.Any<CancellationToken>())
            .Returns(0);
        _persistenceProvider
            .GetTimeTickerById(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((TimeTickerEntity)null!);

        var result = await _timeTickerManager.AddAsync(entity, CancellationToken.None);

        Assert.False(result.IsSucceeded);
        var error = Assert.IsType<TickerValidatorException>(result.Exception);
        Assert.Contains("did not admit the write", error.Message);
        Assert.Contains("reconciliation epoch", error.Message);
        Assert.DoesNotContain("already exists", error.Message);
    }

    [Fact]
    public async Task AddTimeTickerAsync_ZeroRowsAndExistingRow_ReportsDuplicate()
    {
        var entity = new TimeTickerEntity { Function = ValidFunctionName, ExecutionTime = FixedUtcNow.AddMinutes(5) };
        _persistenceProvider
            .AddTimeTickers(Arg.Any<TimeTickerEntity[]>(), Arg.Any<CancellationToken>())
            .Returns(0);
        _persistenceProvider
            .GetTimeTickerById(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(call => new TimeTickerEntity { Id = call.Arg<Guid>(), Function = ValidFunctionName });

        var result = await _timeTickerManager.AddAsync(entity, CancellationToken.None);

        Assert.False(result.IsSucceeded);
        var error = Assert.IsType<TickerValidatorException>(result.Exception);
        Assert.Contains("already exists", error.Message);
    }
}
