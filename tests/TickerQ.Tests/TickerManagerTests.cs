using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NSubstitute;
using TickerQ.Utilities;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Exceptions;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Interfaces.Managers;
using TickerQ.Utilities.Managers;
using TickerQ.Utilities.Models;

namespace TickerQ.Tests;

[Collection("TickerFunctionProviderState")]
public class TickerManagerTests : IDisposable
{
    private const string ValidFunctionName = "TestFunction";
    private const string InvalidFunctionName = "NonExistentFunction";
    private const string ValidCronExpression = "0 0 * * * *"; // every hour (6-part with seconds)
    private const string InvalidCronExpression = "not-a-cron";

    private readonly ITickerPersistenceProvider<TimeTickerEntity, CronTickerEntity> _persistenceProvider;
    private readonly ITickerQHostScheduler _hostScheduler;
    private readonly ITickerClock _clock;
    private readonly ITickerQNotificationHubSender _notificationHubSender;
    private readonly ITickerQDispatcher _dispatcher;
    private readonly TickerExecutionContext _executionContext;

    private readonly ITimeTickerManager<TimeTickerEntity> _timeTickerManager;
    private readonly ICronTickerManager<CronTickerEntity> _cronTickerManager;

    private static readonly DateTime FixedUtcNow = new(2026, 3, 13, 12, 0, 0, DateTimeKind.Utc);

    public TickerManagerTests()
    {
        _persistenceProvider = Substitute.For<ITickerPersistenceProvider<TimeTickerEntity, CronTickerEntity>>();
        _hostScheduler = Substitute.For<ITickerQHostScheduler>();
        _clock = Substitute.For<ITickerClock>();
        _notificationHubSender = Substitute.For<ITickerQNotificationHubSender>();
        _dispatcher = Substitute.For<ITickerQDispatcher>();
        _executionContext = new TickerExecutionContext();

        _clock.UtcNow.Returns(FixedUtcNow);
        _dispatcher.IsEnabled.Returns(false);

        // Register the test function in TickerFunctionProvider before Build()
        TickerFunctionProvider.RegisterFunctions(
            new Dictionary<string, (string, TickerTaskPriority, TickerFunctionDelegate, int)>
            {
                [ValidFunctionName] = ("", TickerTaskPriority.Normal, (_, _, _) => Task.CompletedTask, 0)
            });
        TickerFunctionProvider.Build();

        var manager = new TickerManager<TimeTickerEntity, CronTickerEntity>(
            _persistenceProvider,
            _hostScheduler,
            _clock,
            _notificationHubSender,
            _executionContext,
            _dispatcher);

        _timeTickerManager = manager;
        _cronTickerManager = manager;
    }

    public void Dispose()
    {
        // Rebuild with empty functions so static state does not leak between test classes.
        TickerFunctionProvider.Build();
    }

    // ---------------------------------------------------------------
    // AddTimeTickerAsync
    // ---------------------------------------------------------------

    [Fact]
    public async Task AddTimeTickerAsync_ValidTicker_ReturnsSuccessWithCreatedTicker()
    {
        var entity = new TimeTickerEntity
        {
            Function = ValidFunctionName,
            ExecutionTime = FixedUtcNow.AddMinutes(10)
        };

        _persistenceProvider
            .AddTimeTickers(Arg.Any<TimeTickerEntity[]>(), Arg.Any<CancellationToken>())
            .Returns(1);
        _notificationHubSender
            .AddTimeTickerNotifyAsync(Arg.Any<Guid>())
            .Returns(Task.CompletedTask);

        var result = await _timeTickerManager.AddAsync(entity, CancellationToken.None);

        Assert.True(result.IsSucceeded);
        Assert.NotNull(result.Result);
        Assert.Equal(ValidFunctionName, result.Result.Function);
        Assert.NotEqual(Guid.Empty, result.Result.Id);
        Assert.Equal(FixedUtcNow, result.Result.CreatedAt);
        Assert.Equal(FixedUtcNow, result.Result.UpdatedAt);
    }

    [Fact]
    public async Task AddTimeTickerAsync_InvalidFunctionName_ReturnsFailureWithValidatorException()
    {
        var entity = new TimeTickerEntity
        {
            Function = InvalidFunctionName,
            ExecutionTime = FixedUtcNow.AddMinutes(10)
        };

        var result = await _timeTickerManager.AddAsync(entity, CancellationToken.None);

        Assert.False(result.IsSucceeded);
        Assert.IsType<TickerValidatorException>(result.Exception);
        Assert.Contains(InvalidFunctionName, result.Exception.Message);
    }

    [Fact]
    public async Task AddTimeTickerAsync_NullExecutionTime_DefaultsToUtcNow()
    {
        var entity = new TimeTickerEntity
        {
            Function = ValidFunctionName,
            ExecutionTime = null
        };

        _persistenceProvider
            .AddTimeTickers(Arg.Any<TimeTickerEntity[]>(), Arg.Any<CancellationToken>())
            .Returns(1);
        _notificationHubSender
            .AddTimeTickerNotifyAsync(Arg.Any<Guid>())
            .Returns(Task.CompletedTask);

        var result = await _timeTickerManager.AddAsync(entity, CancellationToken.None);

        Assert.True(result.IsSucceeded);
        Assert.Equal(FixedUtcNow, result.Result.ExecutionTime);
    }

    [Fact]
    public async Task AddTimeTickerAsync_EmptyGuid_AssignsNewId()
    {
        var entity = new TimeTickerEntity
        {
            Id = Guid.Empty,
            Function = ValidFunctionName,
            ExecutionTime = FixedUtcNow.AddMinutes(10)
        };

        _persistenceProvider
            .AddTimeTickers(Arg.Any<TimeTickerEntity[]>(), Arg.Any<CancellationToken>())
            .Returns(1);
        _notificationHubSender
            .AddTimeTickerNotifyAsync(Arg.Any<Guid>())
            .Returns(Task.CompletedTask);

        var result = await _timeTickerManager.AddAsync(entity, CancellationToken.None);

        Assert.True(result.IsSucceeded);
        Assert.NotEqual(Guid.Empty, result.Result.Id);
    }

    [Fact]
    public async Task AddTimeTickerAsync_ValidTicker_CallsPersistenceProviderAddTimeTickers()
    {
        var entity = new TimeTickerEntity
        {
            Function = ValidFunctionName,
            ExecutionTime = FixedUtcNow.AddMinutes(10)
        };

        _persistenceProvider
            .AddTimeTickers(Arg.Any<TimeTickerEntity[]>(), Arg.Any<CancellationToken>())
            .Returns(1);
        _notificationHubSender
            .AddTimeTickerNotifyAsync(Arg.Any<Guid>())
            .Returns(Task.CompletedTask);

        await _timeTickerManager.AddAsync(entity, CancellationToken.None);

        await _persistenceProvider.Received(1)
            .AddTimeTickers(Arg.Any<TimeTickerEntity[]>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AddTimeTickerAsync_ValidTicker_CallsNotificationHub()
    {
        var entity = new TimeTickerEntity
        {
            Function = ValidFunctionName,
            ExecutionTime = FixedUtcNow.AddMinutes(10)
        };

        _persistenceProvider
            .AddTimeTickers(Arg.Any<TimeTickerEntity[]>(), Arg.Any<CancellationToken>())
            .Returns(1);
        _notificationHubSender
            .AddTimeTickerNotifyAsync(Arg.Any<Guid>())
            .Returns(Task.CompletedTask);

        await _timeTickerManager.AddAsync(entity, CancellationToken.None);

        await _notificationHubSender.Received(1)
            .AddTimeTickerNotifyAsync(Arg.Any<Guid>());
    }

    [Fact]
    public async Task AddTimeTickerAsync_FutureExecution_CallsRestartIfNeeded()
    {
        var futureTime = FixedUtcNow.AddMinutes(10);
        var entity = new TimeTickerEntity
        {
            Function = ValidFunctionName,
            ExecutionTime = futureTime
        };

        _persistenceProvider
            .AddTimeTickers(Arg.Any<TimeTickerEntity[]>(), Arg.Any<CancellationToken>())
            .Returns(1);
        _notificationHubSender
            .AddTimeTickerNotifyAsync(Arg.Any<Guid>())
            .Returns(Task.CompletedTask);

        await _timeTickerManager.AddAsync(entity, CancellationToken.None);

        _hostScheduler.Received(1).RestartIfNeeded(futureTime);
    }

    [Fact]
    public async Task AddTimeTickerAsync_PersistenceThrows_ReturnsFailure()
    {
        var entity = new TimeTickerEntity
        {
            Function = ValidFunctionName,
            ExecutionTime = FixedUtcNow.AddMinutes(10)
        };

        _persistenceProvider
            .AddTimeTickers(Arg.Any<TimeTickerEntity[]>(), Arg.Any<CancellationToken>())
            .Returns<int>(_ => throw new InvalidOperationException("DB error"));

        var result = await _timeTickerManager.AddAsync(entity, CancellationToken.None);

        Assert.False(result.IsSucceeded);
        Assert.IsType<InvalidOperationException>(result.Exception);
        Assert.Equal("DB error", result.Exception.Message);
    }

    // ---------------------------------------------------------------
    // AddCronTickerAsync
    // ---------------------------------------------------------------

    [Fact]
    public async Task AddCronTickerAsync_ValidTicker_ReturnsSuccess()
    {
        var entity = new CronTickerEntity
        {
            Function = ValidFunctionName,
            Expression = ValidCronExpression
        };

        _persistenceProvider
            .InsertCronTickers(Arg.Any<CronTickerEntity[]>(), Arg.Any<CancellationToken>())
            .Returns(1);
        _notificationHubSender
            .AddCronTickerNotifyAsync(Arg.Any<object>())
            .Returns(Task.CompletedTask);

        var result = await _cronTickerManager.AddAsync(entity, CancellationToken.None);

        Assert.True(result.IsSucceeded);
        Assert.NotNull(result.Result);
        Assert.Equal(ValidFunctionName, result.Result.Function);
        Assert.NotEqual(Guid.Empty, result.Result.Id);
    }

    [Fact]
    public async Task AddCronTickerAsync_InvalidCronExpression_ReturnsFailure()
    {
        var entity = new CronTickerEntity
        {
            Function = ValidFunctionName,
            Expression = InvalidCronExpression
        };

        var result = await _cronTickerManager.AddAsync(entity, CancellationToken.None);

        Assert.False(result.IsSucceeded);
        Assert.IsType<TickerValidatorException>(result.Exception);
        Assert.Contains(InvalidCronExpression, result.Exception.Message);
    }

    [Fact]
    public async Task AddCronTickerAsync_InvalidFunctionName_ReturnsFailure()
    {
        var entity = new CronTickerEntity
        {
            Function = InvalidFunctionName,
            Expression = ValidCronExpression
        };

        var result = await _cronTickerManager.AddAsync(entity, CancellationToken.None);

        Assert.False(result.IsSucceeded);
        Assert.IsType<TickerValidatorException>(result.Exception);
        Assert.Contains(InvalidFunctionName, result.Exception.Message);
    }

    [Fact]
    public async Task AddCronTickerAsync_ValidTicker_CallsPersistenceProviderInsertCronTickers()
    {
        var entity = new CronTickerEntity
        {
            Function = ValidFunctionName,
            Expression = ValidCronExpression
        };

        _persistenceProvider
            .InsertCronTickers(Arg.Any<CronTickerEntity[]>(), Arg.Any<CancellationToken>())
            .Returns(1);
        _notificationHubSender
            .AddCronTickerNotifyAsync(Arg.Any<object>())
            .Returns(Task.CompletedTask);

        await _cronTickerManager.AddAsync(entity, CancellationToken.None);

        await _persistenceProvider.Received(1)
            .InsertCronTickers(Arg.Any<CronTickerEntity[]>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AddCronTickerAsync_ValidTicker_CallsNotificationHub()
    {
        var entity = new CronTickerEntity
        {
            Function = ValidFunctionName,
            Expression = ValidCronExpression
        };

        _persistenceProvider
            .InsertCronTickers(Arg.Any<CronTickerEntity[]>(), Arg.Any<CancellationToken>())
            .Returns(1);
        _notificationHubSender
            .AddCronTickerNotifyAsync(Arg.Any<object>())
            .Returns(Task.CompletedTask);

        await _cronTickerManager.AddAsync(entity, CancellationToken.None);

        await _notificationHubSender.Received(1)
            .AddCronTickerNotifyAsync(Arg.Any<object>());
    }

    [Fact]
    public async Task AddCronTickerAsync_ValidTicker_SetsTimestamps()
    {
        var entity = new CronTickerEntity
        {
            Function = ValidFunctionName,
            Expression = ValidCronExpression
        };

        _persistenceProvider
            .InsertCronTickers(Arg.Any<CronTickerEntity[]>(), Arg.Any<CancellationToken>())
            .Returns(1);
        _notificationHubSender
            .AddCronTickerNotifyAsync(Arg.Any<object>())
            .Returns(Task.CompletedTask);

        var result = await _cronTickerManager.AddAsync(entity, CancellationToken.None);

        Assert.Equal(FixedUtcNow, result.Result.CreatedAt);
        Assert.Equal(FixedUtcNow, result.Result.UpdatedAt);
    }

    [Fact]
    public async Task AddCronTickerAsync_EmptyGuid_AssignsNewId()
    {
        var entity = new CronTickerEntity
        {
            Id = Guid.Empty,
            Function = ValidFunctionName,
            Expression = ValidCronExpression
        };

        _persistenceProvider
            .InsertCronTickers(Arg.Any<CronTickerEntity[]>(), Arg.Any<CancellationToken>())
            .Returns(1);
        _notificationHubSender
            .AddCronTickerNotifyAsync(Arg.Any<object>())
            .Returns(Task.CompletedTask);

        var result = await _cronTickerManager.AddAsync(entity, CancellationToken.None);

        Assert.NotEqual(Guid.Empty, result.Result.Id);
    }

    [Fact]
    public async Task AddCronTickerAsync_PersistenceThrows_ReturnsFailure()
    {
        var entity = new CronTickerEntity
        {
            Function = ValidFunctionName,
            Expression = ValidCronExpression
        };

        _persistenceProvider
            .InsertCronTickers(Arg.Any<CronTickerEntity[]>(), Arg.Any<CancellationToken>())
            .Returns<int>(_ => throw new InvalidOperationException("DB error"));

        var result = await _cronTickerManager.AddAsync(entity, CancellationToken.None);

        Assert.False(result.IsSucceeded);
        Assert.IsType<InvalidOperationException>(result.Exception);
    }

    // ---------------------------------------------------------------
    // UpdateTimeTickerAsync
    // ---------------------------------------------------------------

    [Fact]
    public async Task UpdateTimeTickerAsync_ValidTicker_ReturnsSuccess()
    {
        var ticker = new TimeTickerEntity
        {
            Id = Guid.NewGuid(),
            Function = ValidFunctionName,
            ExecutionTime = FixedUtcNow.AddMinutes(30)
        };

        _persistenceProvider
            .UpdateTimeTickers(Arg.Any<TimeTickerEntity[]>(), Arg.Any<CancellationToken>())
            .Returns(1);

        var result = await _timeTickerManager.UpdateAsync(ticker, CancellationToken.None);

        Assert.True(result.IsSucceeded);
        Assert.NotNull(result.Result);
        Assert.Equal(1, result.AffectedRows);
        Assert.Equal(FixedUtcNow, result.Result.UpdatedAt);
    }

    [Fact]
    public async Task UpdateTimeTickerAsync_NullTicker_ReturnsFailureWithValidatorException()
    {
        var result = await _timeTickerManager.UpdateAsync(null!, CancellationToken.None);

        Assert.False(result.IsSucceeded);
        Assert.IsType<TickerValidatorException>(result.Exception);
        Assert.Contains("null", result.Exception.Message);
    }

    [Fact]
    public async Task UpdateTimeTickerAsync_NullExecutionTime_ReturnsFailure()
    {
        var ticker = new TimeTickerEntity
        {
            Id = Guid.NewGuid(),
            Function = ValidFunctionName,
            ExecutionTime = null
        };

        var result = await _timeTickerManager.UpdateAsync(ticker, CancellationToken.None);

        Assert.False(result.IsSucceeded);
        Assert.IsType<TickerValidatorException>(result.Exception);
        Assert.Contains("ExecutionTime", result.Exception.Message);
    }

    [Fact]
    public async Task UpdateTimeTickerAsync_ValidTicker_CallsPersistenceProviderUpdateTimeTickers()
    {
        var ticker = new TimeTickerEntity
        {
            Id = Guid.NewGuid(),
            Function = ValidFunctionName,
            ExecutionTime = FixedUtcNow.AddMinutes(30)
        };

        _persistenceProvider
            .UpdateTimeTickers(Arg.Any<TimeTickerEntity[]>(), Arg.Any<CancellationToken>())
            .Returns(1);

        await _timeTickerManager.UpdateAsync(ticker, CancellationToken.None);

        await _persistenceProvider.Received(1)
            .UpdateTimeTickers(Arg.Any<TimeTickerEntity[]>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateTimeTickerAsync_TickerNotInExecutionContext_CallsRestartIfNeeded()
    {
        var ticker = new TimeTickerEntity
        {
            Id = Guid.NewGuid(),
            Function = ValidFunctionName,
            ExecutionTime = FixedUtcNow.AddMinutes(30)
        };

        _persistenceProvider
            .UpdateTimeTickers(Arg.Any<TimeTickerEntity[]>(), Arg.Any<CancellationToken>())
            .Returns(1);

        await _timeTickerManager.UpdateAsync(ticker, CancellationToken.None);

        _hostScheduler.Received(1).RestartIfNeeded(Arg.Any<DateTime?>());
    }

    [Fact]
    public async Task UpdateTimeTickerAsync_TickerInExecutionContext_CallsRestart()
    {
        var tickerId = Guid.NewGuid();
        _executionContext.Functions = new[]
        {
            new InternalFunctionContext { TickerId = tickerId, FunctionName = ValidFunctionName }
        };

        var ticker = new TimeTickerEntity
        {
            Id = tickerId,
            Function = ValidFunctionName,
            ExecutionTime = FixedUtcNow.AddMinutes(30)
        };

        _persistenceProvider
            .UpdateTimeTickers(Arg.Any<TimeTickerEntity[]>(), Arg.Any<CancellationToken>())
            .Returns(1);

        await _timeTickerManager.UpdateAsync(ticker, CancellationToken.None);

        _hostScheduler.Received(1).Restart();
    }

    [Fact]
    public async Task UpdateTimeTickerAsync_PersistenceThrows_ReturnsFailure()
    {
        var ticker = new TimeTickerEntity
        {
            Id = Guid.NewGuid(),
            Function = ValidFunctionName,
            ExecutionTime = FixedUtcNow.AddMinutes(30)
        };

        _persistenceProvider
            .UpdateTimeTickers(Arg.Any<TimeTickerEntity[]>(), Arg.Any<CancellationToken>())
            .Returns<int>(_ => throw new InvalidOperationException("DB error"));

        var result = await _timeTickerManager.UpdateAsync(ticker, CancellationToken.None);

        Assert.False(result.IsSucceeded);
        Assert.IsType<InvalidOperationException>(result.Exception);
    }

    // ---------------------------------------------------------------
    // UpdateCronTickerAsync
    // ---------------------------------------------------------------

    [Fact]
    public async Task UpdateCronTickerAsync_ValidTicker_ReturnsSuccess()
    {
        var ticker = new CronTickerEntity
        {
            Id = Guid.NewGuid(),
            Function = ValidFunctionName,
            Expression = ValidCronExpression
        };

        _persistenceProvider
            .UpdateCronTickers(Arg.Any<CronTickerEntity[]>(), Arg.Any<CancellationToken>())
            .Returns(1);

        var result = await _cronTickerManager.UpdateAsync(ticker, CancellationToken.None);

        Assert.True(result.IsSucceeded);
        Assert.NotNull(result.Result);
        Assert.Equal(1, result.AffectedRows);
        Assert.Equal(FixedUtcNow, result.Result.UpdatedAt);
    }

    [Fact]
    public async Task UpdateCronTickerAsync_NullTicker_ReturnsFailure()
    {
        var result = await _cronTickerManager.UpdateAsync(null!, CancellationToken.None);

        Assert.False(result.IsSucceeded);
        Assert.NotNull(result.Exception);
        Assert.Contains("null", result.Exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UpdateCronTickerAsync_InvalidFunctionName_ReturnsFailure()
    {
        var ticker = new CronTickerEntity
        {
            Id = Guid.NewGuid(),
            Function = InvalidFunctionName,
            Expression = ValidCronExpression
        };

        var result = await _cronTickerManager.UpdateAsync(ticker, CancellationToken.None);

        Assert.False(result.IsSucceeded);
        Assert.IsType<TickerValidatorException>(result.Exception);
        Assert.Contains(InvalidFunctionName, result.Exception.Message);
    }

    [Fact]
    public async Task UpdateCronTickerAsync_InvalidExpression_ReturnsFailure()
    {
        var ticker = new CronTickerEntity
        {
            Id = Guid.NewGuid(),
            Function = ValidFunctionName,
            Expression = InvalidCronExpression
        };

        var result = await _cronTickerManager.UpdateAsync(ticker, CancellationToken.None);

        Assert.False(result.IsSucceeded);
        Assert.IsType<TickerValidatorException>(result.Exception);
        Assert.Contains(InvalidCronExpression, result.Exception.Message);
    }

    [Fact]
    public async Task UpdateCronTickerAsync_ValidTicker_CallsPersistenceProviderUpdateCronTickers()
    {
        var ticker = new CronTickerEntity
        {
            Id = Guid.NewGuid(),
            Function = ValidFunctionName,
            Expression = ValidCronExpression
        };

        _persistenceProvider
            .UpdateCronTickers(Arg.Any<CronTickerEntity[]>(), Arg.Any<CancellationToken>())
            .Returns(1);

        await _cronTickerManager.UpdateAsync(ticker, CancellationToken.None);

        await _persistenceProvider.Received(1)
            .UpdateCronTickers(Arg.Any<CronTickerEntity[]>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateCronTickerAsync_PersistenceThrows_ReturnsFailure()
    {
        var ticker = new CronTickerEntity
        {
            Id = Guid.NewGuid(),
            Function = ValidFunctionName,
            Expression = ValidCronExpression
        };

        _persistenceProvider
            .UpdateCronTickers(Arg.Any<CronTickerEntity[]>(), Arg.Any<CancellationToken>())
            .Returns<int>(_ => throw new InvalidOperationException("DB error"));

        var result = await _cronTickerManager.UpdateAsync(ticker, CancellationToken.None);

        Assert.False(result.IsSucceeded);
        Assert.IsType<InvalidOperationException>(result.Exception);
    }

    [Fact]
    public async Task UpdateCronTickerAsync_TickerInExecutionContext_UpdatesOccurrenceAndRestarts()
    {
        var cronTickerId = Guid.NewGuid();
        var internalFunc = new InternalFunctionContext
        {
            ParentId = cronTickerId,
            FunctionName = ValidFunctionName
        };
        _executionContext.Functions = new[] { internalFunc };

        var ticker = new CronTickerEntity
        {
            Id = cronTickerId,
            Function = ValidFunctionName,
            Expression = ValidCronExpression
        };

        _persistenceProvider
            .UpdateCronTickers(Arg.Any<CronTickerEntity[]>(), Arg.Any<CancellationToken>())
            .Returns(1);
        _persistenceProvider
            .UpdateCronTickerOccurrence(Arg.Any<InternalFunctionContext>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var result = await _cronTickerManager.UpdateAsync(ticker, CancellationToken.None);

        Assert.True(result.IsSucceeded);
        _hostScheduler.Received(1).Restart();
        await _persistenceProvider.Received(1)
            .UpdateCronTickerOccurrence(Arg.Any<InternalFunctionContext>(), Arg.Any<CancellationToken>());
    }

    // ---------------------------------------------------------------
    // DeleteTimeTickerAsync
    // ---------------------------------------------------------------

    [Fact]
    public async Task DeleteTimeTickerAsync_ExistingTicker_ReturnsSuccess()
    {
        var tickerId = Guid.NewGuid();

        _persistenceProvider
            .RemoveTimeTickers(Arg.Any<Guid[]>(), Arg.Any<CancellationToken>())
            .Returns(1);

        var result = await _timeTickerManager.DeleteAsync(tickerId, CancellationToken.None);

        Assert.True(result.IsSucceeded);
        Assert.Equal(1, result.AffectedRows);
    }

    [Fact]
    public async Task DeleteTimeTickerAsync_CallsPersistenceProviderRemoveTimeTickers()
    {
        var tickerId = Guid.NewGuid();

        _persistenceProvider
            .RemoveTimeTickers(Arg.Any<Guid[]>(), Arg.Any<CancellationToken>())
            .Returns(1);

        await _timeTickerManager.DeleteAsync(tickerId, CancellationToken.None);

        await _persistenceProvider.Received(1)
            .RemoveTimeTickers(Arg.Any<Guid[]>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteTimeTickerAsync_NoRowsAffected_DoesNotRestart()
    {
        var tickerId = Guid.NewGuid();

        _persistenceProvider
            .RemoveTimeTickers(Arg.Any<Guid[]>(), Arg.Any<CancellationToken>())
            .Returns(0);

        await _timeTickerManager.DeleteAsync(tickerId, CancellationToken.None);

        _hostScheduler.DidNotReceive().Restart();
    }

    [Fact]
    public async Task DeleteTimeTickerAsync_TickerInExecutionContext_Restarts()
    {
        var tickerId = Guid.NewGuid();
        _executionContext.Functions = new[]
        {
            new InternalFunctionContext { TickerId = tickerId, FunctionName = ValidFunctionName }
        };

        _persistenceProvider
            .RemoveTimeTickers(Arg.Any<Guid[]>(), Arg.Any<CancellationToken>())
            .Returns(1);

        await _timeTickerManager.DeleteAsync(tickerId, CancellationToken.None);

        _hostScheduler.Received(1).Restart();
    }

    // ---------------------------------------------------------------
    // DeleteCronTickerAsync
    // ---------------------------------------------------------------

    [Fact]
    public async Task DeleteCronTickerAsync_ExistingTicker_ReturnsSuccess()
    {
        var tickerId = Guid.NewGuid();

        _persistenceProvider
            .RemoveCronTickers(Arg.Any<Guid[]>(), Arg.Any<CancellationToken>())
            .Returns(1);

        var result = await _cronTickerManager.DeleteAsync(tickerId, CancellationToken.None);

        Assert.True(result.IsSucceeded);
        Assert.Equal(1, result.AffectedRows);
    }

    [Fact]
    public async Task DeleteCronTickerAsync_CallsPersistenceProviderRemoveCronTickers()
    {
        var tickerId = Guid.NewGuid();

        _persistenceProvider
            .RemoveCronTickers(Arg.Any<Guid[]>(), Arg.Any<CancellationToken>())
            .Returns(1);

        await _cronTickerManager.DeleteAsync(tickerId, CancellationToken.None);

        await _persistenceProvider.Received(1)
            .RemoveCronTickers(Arg.Any<Guid[]>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteCronTickerAsync_NoRowsAffected_DoesNotRestart()
    {
        var tickerId = Guid.NewGuid();

        _persistenceProvider
            .RemoveCronTickers(Arg.Any<Guid[]>(), Arg.Any<CancellationToken>())
            .Returns(0);

        await _cronTickerManager.DeleteAsync(tickerId, CancellationToken.None);

        _hostScheduler.DidNotReceive().Restart();
    }

    [Fact]
    public async Task DeleteCronTickerAsync_TickerInExecutionContext_Restarts()
    {
        var cronTickerId = Guid.NewGuid();
        _executionContext.Functions = new[]
        {
            new InternalFunctionContext { ParentId = cronTickerId, FunctionName = ValidFunctionName }
        };

        _persistenceProvider
            .RemoveCronTickers(Arg.Any<Guid[]>(), Arg.Any<CancellationToken>())
            .Returns(1);

        await _cronTickerManager.DeleteAsync(cronTickerId, CancellationToken.None);

        _hostScheduler.Received(1).Restart();
    }

    // ---------------------------------------------------------------
    // ConvertToUtcIfNeeded (tested indirectly through AddTimeTickerAsync / UpdateTimeTickerAsync)
    // ---------------------------------------------------------------

    [Fact]
    public async Task ConvertToUtcIfNeeded_UtcTime_PassesThroughUnchanged()
    {
        var utcTime = new DateTime(2026, 6, 15, 10, 0, 0, DateTimeKind.Utc);
        var entity = new TimeTickerEntity
        {
            Function = ValidFunctionName,
            ExecutionTime = utcTime
        };

        _persistenceProvider
            .AddTimeTickers(Arg.Any<TimeTickerEntity[]>(), Arg.Any<CancellationToken>())
            .Returns(1);
        _notificationHubSender
            .AddTimeTickerNotifyAsync(Arg.Any<Guid>())
            .Returns(Task.CompletedTask);

        var result = await _timeTickerManager.AddAsync(entity, CancellationToken.None);

        Assert.True(result.IsSucceeded);
        Assert.Equal(utcTime, result.Result.ExecutionTime);
        Assert.Equal(DateTimeKind.Utc, result.Result.ExecutionTime!.Value.Kind);
    }

    [Fact]
    public async Task ConvertToUtcIfNeeded_LocalTime_GetsConvertedToUtc()
    {
        var localTime = new DateTime(2026, 6, 15, 10, 0, 0, DateTimeKind.Local);
        var expectedUtc = localTime.ToUniversalTime();

        var entity = new TimeTickerEntity
        {
            Function = ValidFunctionName,
            ExecutionTime = localTime
        };

        _persistenceProvider
            .AddTimeTickers(Arg.Any<TimeTickerEntity[]>(), Arg.Any<CancellationToken>())
            .Returns(1);
        _notificationHubSender
            .AddTimeTickerNotifyAsync(Arg.Any<Guid>())
            .Returns(Task.CompletedTask);

        var result = await _timeTickerManager.AddAsync(entity, CancellationToken.None);

        Assert.True(result.IsSucceeded);
        Assert.Equal(expectedUtc, result.Result.ExecutionTime);
        Assert.Equal(DateTimeKind.Utc, result.Result.ExecutionTime!.Value.Kind);
    }

    [Fact]
    public async Task ConvertToUtcIfNeeded_UnspecifiedKind_ConvertedUsingSystemTimezone()
    {
        // Unspecified is treated as the system timezone via CronScheduleCache.TimeZoneInfo
        var unspecifiedTime = new DateTime(2026, 6, 15, 10, 0, 0, DateTimeKind.Unspecified);
        var expectedUtc = TimeZoneInfo.ConvertTimeToUtc(unspecifiedTime, CronScheduleCache.TimeZoneInfo);

        var entity = new TimeTickerEntity
        {
            Function = ValidFunctionName,
            ExecutionTime = unspecifiedTime
        };

        _persistenceProvider
            .AddTimeTickers(Arg.Any<TimeTickerEntity[]>(), Arg.Any<CancellationToken>())
            .Returns(1);
        _notificationHubSender
            .AddTimeTickerNotifyAsync(Arg.Any<Guid>())
            .Returns(Task.CompletedTask);

        var result = await _timeTickerManager.AddAsync(entity, CancellationToken.None);

        Assert.True(result.IsSucceeded);
        Assert.Equal(expectedUtc, result.Result.ExecutionTime);
    }

    [Fact]
    public async Task UpdateTimeTickerAsync_LocalExecutionTime_GetsConvertedToUtc()
    {
        var localTime = new DateTime(2026, 6, 15, 14, 0, 0, DateTimeKind.Local);
        var expectedUtc = localTime.ToUniversalTime();

        var ticker = new TimeTickerEntity
        {
            Id = Guid.NewGuid(),
            Function = ValidFunctionName,
            ExecutionTime = localTime
        };

        _persistenceProvider
            .UpdateTimeTickers(Arg.Any<TimeTickerEntity[]>(), Arg.Any<CancellationToken>())
            .Returns(1);

        var result = await _timeTickerManager.UpdateAsync(ticker, CancellationToken.None);

        Assert.True(result.IsSucceeded);
        Assert.Equal(expectedUtc, result.Result.ExecutionTime);
        Assert.Equal(DateTimeKind.Utc, result.Result.ExecutionTime!.Value.Kind);
    }

    // ---------------------------------------------------------------
    // Dispatcher integration (immediate dispatch path)
    // ---------------------------------------------------------------

    [Fact]
    public async Task AddTimeTickerAsync_DispatcherEnabledAndImmediateExecution_DispatchesTicker()
    {
        _dispatcher.IsEnabled.Returns(true);

        // Execution time is within 1 second of "now"
        var entity = new TimeTickerEntity
        {
            Function = ValidFunctionName,
            ExecutionTime = FixedUtcNow
        };

        _persistenceProvider
            .AddTimeTickers(Arg.Any<TimeTickerEntity[]>(), Arg.Any<CancellationToken>())
            .Returns(1);
        _persistenceProvider
            .AcquireImmediateTimeTickersAsync(Arg.Any<Guid[]>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<TimeTickerEntity>());
        _notificationHubSender
            .AddTimeTickerNotifyAsync(Arg.Any<Guid>())
            .Returns(Task.CompletedTask);

        var result = await _timeTickerManager.AddAsync(entity, CancellationToken.None);

        Assert.True(result.IsSucceeded);
        await _persistenceProvider.Received(1)
            .AcquireImmediateTimeTickersAsync(Arg.Any<Guid[]>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AddTimeTickerAsync_DispatcherDisabledAndImmediateExecution_DoesNotDispatch()
    {
        _dispatcher.IsEnabled.Returns(false);

        var entity = new TimeTickerEntity
        {
            Function = ValidFunctionName,
            ExecutionTime = FixedUtcNow
        };

        _persistenceProvider
            .AddTimeTickers(Arg.Any<TimeTickerEntity[]>(), Arg.Any<CancellationToken>())
            .Returns(1);
        _notificationHubSender
            .AddTimeTickerNotifyAsync(Arg.Any<Guid>())
            .Returns(Task.CompletedTask);

        await _timeTickerManager.AddAsync(entity, CancellationToken.None);

        await _persistenceProvider.DidNotReceive()
            .AcquireImmediateTimeTickersAsync(Arg.Any<Guid[]>(), Arg.Any<CancellationToken>());
    }
}
