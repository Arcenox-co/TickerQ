using NSubstitute;
using TickerQ.Dashboard;
using TickerQ.Dashboard.Infrastructure.Dashboard;
using TickerQ.Utilities;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Infrastructure;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Models;

namespace TickerQ.Tests;

public class DashboardCronRunNowTests
{
    private readonly ITickerPersistenceProvider<TimeTickerEntity, CronTickerEntity> _provider = Substitute.For<ITickerPersistenceProvider<TimeTickerEntity, CronTickerEntity>>();
    private readonly ITickerQDispatcher _dispatcher = Substitute.For<ITickerQDispatcher>();
    private readonly ITickerQNotificationHubSender _notifier = Substitute.For<ITickerQNotificationHubSender>();
    private readonly ITickerQHostScheduler _host = Substitute.For<ITickerQHostScheduler>();
    private readonly SchedulerOptionsBuilder _options = new();
    private readonly Guid _generation = Guid.NewGuid();
    private readonly CronTickerEntity _cron = new()
    {
        Id = Guid.NewGuid(), Function = "ManualCronRegression", Expression = "0 0 1 1 *",
        SkipIfAlreadyRunning = true, Retries = 2, RetryIntervals = [3, 7], TimeoutSeconds = 42, DefinitionRevision = 6,
        RequestContractVersion = 3, RequestContractFingerprint = "sha256:manual-cron-test"
    };
    private CronTickerOccurrenceEntity<CronTickerEntity>? _inserted;

    public DashboardCronRunNowTests()
    {
        _options.BindRuntimeActivationScope("manual-cron-tests", 1, true);
        _dispatcher.IsEnabled.Returns(true);
        _provider.GetCronTickerById(_cron.Id, Arg.Any<CancellationToken>()).Returns(_cron);
        _provider.InsertCronTickerOccurrences(Arg.Any<CronTickerOccurrenceEntity<CronTickerEntity>[]>(), Arg.Any<CancellationToken>())
            .Returns(call => { _inserted = call.ArgAt<CronTickerOccurrenceEntity<CronTickerEntity>[]>(0).Single(); return 1; });
        _provider.AcquireImmediateCronOccurrencesAsync(Arg.Any<Guid[]>(), Arg.Any<CancellationToken>())
            .Returns(call => new[] { new CronTickerOccurrenceEntity<CronTickerEntity>
            {
                Id = call.ArgAt<Guid[]>(0).Single(), CronTickerId = _cron.Id, CronTicker = _cron,
                AcquisitionToken = _generation, ExecutionTime = _inserted!.ExecutionTime,
                Status = TickerStatus.InProgress
            } });
    }

    private Task Run(bool bulk, CancellationToken cancellationToken = default)
    {
        if (bulk)
        {
            var repository = new TickerDashboardRepository<TimeTickerEntity, CronTickerEntity>(
                new TickerExecutionContext { RuntimePartition = _options.RuntimePartition }, _provider, _host,
                _notifier, new DashboardOptionsBuilder(), _dispatcher);
            return repository.AddOnDemandCronTickerOccurrenceAsync(_cron.Id, cancellationToken);
        }
        var service = new TickerDashboardDataService<TimeTickerEntity, CronTickerEntity>(
            _provider, _host, Substitute.For<ITickerQTaskScheduler>(), _options, _dispatcher, _notifier);
        return service.RunCronTickerOnDemandAsync(_cron.Id, cancellationToken);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SingleAndBulkRuns_DispatchAcquiredOccurrence_WithExactScopeAndExecutionSettings(bool bulk)
    {
        var before = DateTime.UtcNow;
        await Run(bulk);

        Assert.NotNull(_inserted);
        Assert.NotEqual(Guid.Empty, _inserted.Id);
        Assert.Equal(_options.RuntimePartition.StorageKey, _inserted.ApplicationNamespaceKey);
        Assert.Equal(_cron.DefinitionRevision, _inserted.DefinitionRevision);
        Assert.InRange(_inserted.ExecutionTime, before, DateTime.UtcNow);
        Assert.Equal(_inserted.ExecutionTime, _inserted.CreatedAt);
        Assert.Equal(_inserted.CreatedAt, _inserted.UpdatedAt);
        await _dispatcher.Received(1).DispatchAsync(Arg.Is<InternalFunctionContext[]>(items =>
            items.Length == 1 && items[0].TickerId == _inserted.Id && items[0].ParentId == _cron.Id &&
            items[0].Type == TickerType.CronTickerOccurrence && items[0].AcquisitionToken == _generation &&
            items[0].RuntimePartitionKey == _options.RuntimePartition.StorageKey &&
            items[0].FunctionName == _cron.Function && items[0].Retries == 2 &&
            items[0].RetryIntervals.SequenceEqual(_cron.RetryIntervals) && items[0].TimeoutSeconds == 42 && items[0].SkipIfAlreadyRunning &&
            items[0].RequestContractVersion == 3 && items[0].RequestContractFingerprint == _cron.RequestContractFingerprint),
            CancellationToken.None);
        await _notifier.Received(1).AddCronOccurrenceAsync(_cron.Id, _inserted.Id);
        _host.DidNotReceive().Restart();
        Assert.Equal("0 0 1 1 *", _cron.Expression);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LostAcquisitionRace_DoesNotDispatchAnotherOwnersOccurrence(bool bulk)
    {
        _provider.AcquireImmediateCronOccurrencesAsync(Arg.Any<Guid[]>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<CronTickerOccurrenceEntity<CronTickerEntity>>());
        await Run(bulk);
        await _dispatcher.DidNotReceiveWithAnyArgs().DispatchAsync(default!, default);
        await _notifier.Received(1).AddCronOccurrenceAsync(_cron.Id, _inserted!.Id);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RequestCancellationAfterCommit_DoesNotCancelAcquisitionOrExecution(bool bulk)
    {
        using var request = new CancellationTokenSource();
        _provider.When(x => x.InsertCronTickerOccurrences(Arg.Any<CronTickerOccurrenceEntity<CronTickerEntity>[]>(), request.Token))
            .Do(_ => request.Cancel());
        await Run(bulk, request.Token);
        Assert.True(request.IsCancellationRequested);
        await _provider.Received(1).AcquireImmediateCronOccurrencesAsync(Arg.Any<Guid[]>(), CancellationToken.None);
        await _dispatcher.Received(1).DispatchAsync(Arg.Any<InternalFunctionContext[]>(), CancellationToken.None);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DisabledDispatcher_RejectsBeforeCreatingAnOccurrence(bool bulk)
    {
        _dispatcher.IsEnabled.Returns(false);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Run(bulk));
        Assert.Null(_inserted);
        await _dispatcher.DidNotReceiveWithAnyArgs().DispatchAsync(default!, default);
    }

    [Fact]
    public async Task MissingCron_DoesNotCreateOrDispatchAnOccurrence()
    {
        _provider.GetCronTickerById(_cron.Id, Arg.Any<CancellationToken>()).Returns((CronTickerEntity)null!);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Run(false));
        Assert.Null(_inserted);
        await _dispatcher.DidNotReceiveWithAnyArgs().DispatchAsync(default!, default);
    }

    [Fact]
    public async Task FailedInsert_DoesNotAcquireOrAnnounceAnOccurrence()
    {
        _provider.InsertCronTickerOccurrences(Arg.Any<CronTickerOccurrenceEntity<CronTickerEntity>[]>(), Arg.Any<CancellationToken>()).Returns(0);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Run(false));
        await _provider.DidNotReceiveWithAnyArgs().AcquireImmediateCronOccurrencesAsync(default!, default);
        await _notifier.DidNotReceiveWithAnyArgs().AddCronOccurrenceAsync(default, default);
    }

    [Fact]
    public async Task RerunOccurrence_ReusesPersistedId_WithoutInsertingAnotherOccurrence()
    {
        var occurrenceId = Guid.NewGuid();
        var rerunGeneration = Guid.NewGuid();
        _provider.AcquireCronOccurrenceOnDemandAsync(
                occurrenceId, Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(new CronTickerOccurrenceEntity<CronTickerEntity>
            {
                Id = occurrenceId,
                CronTickerId = _cron.Id,
                CronTicker = _cron,
                AcquisitionToken = rerunGeneration,
                ExecutionTime = DateTime.UtcNow,
                Status = TickerStatus.InProgress
            });

        var accepted = await OnDemandCronOccurrenceExecution.RunAsync(
            _provider, _dispatcher, _notifier, _options.RuntimePartition.StorageKey,
            occurrenceId, DateTime.UtcNow, CancellationToken.None);

        Assert.True(accepted);
        await _provider.DidNotReceiveWithAnyArgs()
            .InsertCronTickerOccurrences(default!, default);
        await _dispatcher.Received(1).DispatchAsync(
            Arg.Is<InternalFunctionContext[]>(items =>
                items.Length == 1 && items[0].TickerId == occurrenceId &&
                items[0].AcquisitionToken == rerunGeneration &&
                items[0].Type == TickerType.CronTickerOccurrence),
            CancellationToken.None);
        await _notifier.Received(1).UpdateCronOccurrenceAsync(_cron.Id, occurrenceId);
    }

    [Fact]
    public async Task RerunOccurrence_LostAcquisition_DoesNotDispatchOrInsert()
    {
        var occurrenceId = Guid.NewGuid();
        _provider.AcquireCronOccurrenceOnDemandAsync(
                occurrenceId, Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns((CronTickerOccurrenceEntity<CronTickerEntity>)null!);

        var accepted = await OnDemandCronOccurrenceExecution.RunAsync(
            _provider, _dispatcher, _notifier, _options.RuntimePartition.StorageKey,
            occurrenceId, DateTime.UtcNow, CancellationToken.None);

        Assert.False(accepted);
        await _dispatcher.DidNotReceiveWithAnyArgs().DispatchAsync(default!, default);
        await _provider.DidNotReceiveWithAnyArgs()
            .InsertCronTickerOccurrences(default!, default);
    }
}
