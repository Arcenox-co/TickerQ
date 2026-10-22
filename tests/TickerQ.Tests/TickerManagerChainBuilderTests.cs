using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using NSubstitute;
using TickerQ.Utilities;
using TickerQ.Utilities.Base;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Infrastructure;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Models;

namespace TickerQ.Tests;

public partial class TickerManagerTests
{
    public sealed record ChainRequest([property: JsonPropertyName("OrderId")] string OrderId);

    public sealed class TypedFunction : ITickerFunction<ChainRequest>
    {
        public Task ExecuteAsync(
            TickerFunctionContext<ChainRequest> context,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    public sealed class TestFunction : ITickerFunction
    {
        public Task ExecuteAsync(
            TickerFunctionContext context,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    [Fact]
    public async Task TimeChainBuilder_ThenMany_CreatesSiblingBranchesThatCanContinueIndependently()
    {
        _persistenceProvider.AddTimeTickers(
                Arg.Any<TimeTickerEntity[]>(),
                Arg.Any<CancellationToken>())
            .Returns(5);

        var result = await _timeTickerManager.AddChainAsync(chain => chain
            .WithDescription("Process the order")
            .RunAt(FixedUtcNow.AddMinutes(5))
            .WithDeleteBehavior(ChainDeleteBehavior.Restrict)
            .StartWith<TypedFunction, ChainRequest>(new ChainRequest("A-42"), step =>
            {
                step.Retries = 2;
                step.RetryIntervals = [3, 8];
                step.TimeoutSeconds = 30;
            })
            .ThenMany(branches =>
            {
                branches.Add<TestFunction>(step => step.Description = "Send receipt")
                    .Then<TestFunction>(step => step.Description = "Archive receipt");
                branches.Add<TestFunction>(step =>
                {
                    step.Description = "Record failure audit";
                    step.RunCondition = RunCondition.OnFailure;
                });
            }));

        Assert.True(result.IsSucceeded, result.Exception?.ToString());
        var root = result.Result;
        Assert.Equal(nameof(TypedFunction), root.Function);
        Assert.Equal("Process the order", root.Description);
        Assert.Equal(FixedUtcNow.AddMinutes(5), root.ExecutionTime);
        Assert.Equal(ChainDeleteBehavior.Restrict, root.DeleteBehavior);
        Assert.Equal(2, root.Retries);
        Assert.Equal([3, 8], root.RetryIntervals);
        Assert.Equal(30, root.TimeoutSeconds);
        Assert.Equal("A-42", TickerHelper.ReadTickerRequest<ChainRequest>(root.Request).OrderId);

        var group = Assert.Single(root.Children);
        Assert.Equal(TickerChainInfrastructure.ParallelGroupFunction, group.Function);
        Assert.Equal(TickerChainInfrastructure.ParallelGroupDescription, group.Description);
        Assert.Equal(root.Id, group.ParentId);
        Assert.Equal(RunCondition.OnAnyTerminalStatus, group.RunCondition);

        var children = group.Children.OrderBy(child => child.Description).ToArray();
        Assert.Equal(2, children.Length);
        Assert.All(children, child => Assert.Equal(group.Id, child.ParentId));
        Assert.All(children, child => Assert.Null(child.ExecutionTime));
        Assert.All(children, child => Assert.Equal(ChainDeleteBehavior.Cascade, child.DeleteBehavior));
        Assert.Equal(RunCondition.GroupOnFailure,
            Assert.Single(children, child => child.Description == "Record failure audit").RunCondition);
        Assert.Equal(RunCondition.GroupOnSuccess,
            Assert.Single(children, child => child.Description == "Send receipt").RunCondition);

        var audit = Assert.Single(children, child => child.Description == "Record failure audit");
        Assert.Empty(audit.Children);

        var receipt = Assert.Single(children, child => child.Description == "Send receipt");
        var archive = Assert.Single(receipt.Children);
        Assert.Equal(receipt.Id, archive.ParentId);
        Assert.Equal("Archive receipt", archive.Description);
    }

    [Fact]
    public async Task CronChainBuilder_ThenMany_CreatesRecurringTemplate()
    {
        _persistenceProvider.SupportsCronChains.Returns(true);
        _persistenceProvider.InsertCronTickers(
                Arg.Any<CronTickerEntity[]>(),
                Arg.Any<CancellationToken>())
            .Returns(1);

        var result = await _cronTickerManager.AddChainAsync(chain => chain
            .WithExpression("0 0 8 * * *")
            .WithDescription("Daily order processing")
            .SkipIfAlreadyRunning()
            .WithDeleteBehavior(ChainDeleteBehavior.Cascade)
            .StartWith<TestFunction>(step => step.TimeoutSeconds = 60)
            .ThenMany(branches =>
            {
                branches.Add<TestFunction>(step => step.Description = "Generate report")
                    .Then<TestFunction>(step => step.Description = "Publish report");
                branches.Add<TestFunction>(step => step.Description = "Update metrics");
            }));

        Assert.True(result.IsSucceeded, result.Exception?.ToString());
        var cron = result.Result;
        Assert.Equal(nameof(TestFunction), cron.Function);
        Assert.Equal("0 0 8 * * *", cron.Expression);
        Assert.Equal("Daily order processing", cron.Description);
        Assert.True(cron.SkipIfAlreadyRunning);
        Assert.True(cron.IsEnabled);
        Assert.Equal(60, cron.TimeoutSeconds);
        Assert.Equal(ChainDeleteBehavior.Cascade, cron.Chain.DeleteBehavior);
        var group = Assert.Single(cron.Chain.Children);
        Assert.Equal(TickerChainInfrastructure.ParallelGroupFunction, group.Function);
        Assert.Equal(RunCondition.OnAnyTerminalStatus, group.RunCondition);
        Assert.Equal(2, group.Children.Count);

        var report = Assert.Single(group.Children, child => child.Description == "Generate report");
        Assert.Equal("Publish report", Assert.Single(report.Children).Description);
        Assert.Single(group.Children, child => child.Description == "Update metrics");
        Assert.All(group.Children, child => Assert.Equal(RunCondition.GroupOnSuccess, child.RunCondition));
    }

    [Fact]
    public async Task TimeChainBuilder_ParallelContinuations_UseAggregateConditions()
    {
        _persistenceProvider.AddTimeTickers(
                Arg.Any<TimeTickerEntity[]>(),
                Arg.Any<CancellationToken>())
            .Returns(7);

        var result = await _timeTickerManager.AddChainAsync(chain =>
        {
            var group = chain.StartWith<TestFunction>()
                .ThenMany(branches =>
                {
                    branches.Add<TestFunction>(step => step.Description = "A");
                    branches.Add<TestFunction>(step => step.Description = "B");
                });

            group.ThenAfterAll<TestFunction>(step => step.Description = "Success");
            group.ThenAfterAnyFailed<TestFunction>(step => step.Description = "Failure");
            group.ThenAfterAnyFailedOrCancelled<TestFunction>(step => step.Description = "Failure or cancellation");
            group.ThenAfterAllCompleted<TestFunction>(step => step.Description = "Finally");
        });

        Assert.True(result.IsSucceeded, result.Exception?.ToString());
        var parallel = Assert.Single(result.Result.Children);
        Assert.Equal(6, parallel.Children.Count);
        Assert.Equal(RunCondition.OnSuccess,
            Assert.Single(parallel.Children, child => child.Description == "Success").RunCondition);
        Assert.Equal(RunCondition.OnFailure,
            Assert.Single(parallel.Children, child => child.Description == "Failure").RunCondition);
        Assert.Equal(RunCondition.OnFailureOrCancelled,
            Assert.Single(parallel.Children, child => child.Description == "Failure or cancellation").RunCondition);
        Assert.Equal(RunCondition.OnAnyTerminalStatus,
            Assert.Single(parallel.Children, child => child.Description == "Finally").RunCondition);
    }

    [Fact]
    public async Task ChainBuilder_RequiresStartAndThenManyRequiresAChild()
    {
        var missingRoot = await _timeTickerManager.AddChainAsync(chain =>
            chain.WithDescription("Missing root"));
        var emptyBranches = await _timeTickerManager.AddChainAsync(chain => chain
            .StartWith<TestFunction>()
            .ThenMany(_ => { }));

        Assert.False(missingRoot.IsSucceeded);
        Assert.IsType<InvalidOperationException>(missingRoot.Exception);
        Assert.False(emptyBranches.IsSucceeded);
        Assert.IsType<InvalidOperationException>(emptyBranches.Exception);
        await _persistenceProvider.DidNotReceiveWithAnyArgs()
            .AddTimeTickers(default!, default);
    }

    [Fact]
    public async Task RetryFromAsync_PreservesOriginalChainAndDispatchesFromSelectedJob()
    {
        var oldRoot = new TimeTickerEntity
        {
            Id = Guid.NewGuid(), Function = ValidFunctionName,
            Status = TickerStatus.Done, ExecutionTime = FixedUtcNow.AddHours(-1)
        };
        var selected = new TimeTickerEntity
        {
            Id = Guid.NewGuid(), ParentId = oldRoot.Id, Function = TypedFunctionName,
            RunCondition = RunCondition.OnSuccess, Status = TickerStatus.Failed,
            Request = Encoding.UTF8.GetBytes("{\"OrderId\":\"A-42\"}"),
            RequestContractVersion = 7,
            Retries = 2,
            RetryIntervals = [5, 15]
        };
        var descendant = new TimeTickerEntity
        {
            Id = Guid.NewGuid(), ParentId = selected.Id, Function = ValidFunctionName,
            RunCondition = RunCondition.OnFailure, Status = TickerStatus.Skipped
        };
        oldRoot.Children.Add(selected);
        selected.Children.Add(descendant);
        var generation = Guid.NewGuid();
        oldRoot.Status = TickerStatus.InProgress;
        oldRoot.AcquisitionToken = generation;
        oldRoot.ChainRootId = oldRoot.Id;
        oldRoot.ChainGeneration = generation;
        selected.Status = TickerStatus.Idle;
        selected.ChainRootId = oldRoot.Id;
        selected.ChainGeneration = generation;
        descendant.Status = TickerStatus.Idle;
        descendant.ChainRootId = oldRoot.Id;
        descendant.ChainGeneration = generation;
        _dispatcher.IsEnabled.Returns(true);
        _persistenceProvider.AcquireTimeTickerChainRetryAsync(
                selected.Id, FixedUtcNow, Arg.Any<CancellationToken>())
            .Returns(new TimeTickerChainRetryAcquisition<TimeTickerEntity>
            {
                Root = oldRoot,
                SelectedId = selected.Id,
                PreviousRootStatus = TickerStatus.Done
            });
        InternalFunctionContext[] dispatched = null;
        _dispatcher.DispatchAsync(Arg.Do<InternalFunctionContext[]>(items => dispatched = items), CancellationToken.None)
            .Returns(Task.CompletedTask);

        var result = await _timeTickerManager.RetryFromAsync(selected.Id);

        Assert.True(result.IsSucceeded, result.Exception?.ToString());
        Assert.Same(oldRoot, result.Result);
        var execution = Assert.Single(dispatched);
        Assert.Equal(oldRoot.Id, execution.TickerId);
        Assert.True(execution.IsChainRetryRoot);
        Assert.True(execution.IsChainRetryCoordinator);
        Assert.True(execution.IsChainRetryPassThrough);
        var selectedExecution = Assert.Single(execution.TimeTickerChildren);
        Assert.Equal(selected.Id, selectedExecution.TickerId);
        Assert.False(selectedExecution.IsChainRetryPassThrough);
        Assert.Equal(RunCondition.InProgress, selectedExecution.RunCondition);
        Assert.Equal(descendant.Id, Assert.Single(selectedExecution.TimeTickerChildren).TickerId);
        await _persistenceProvider.DidNotReceiveWithAnyArgs()
            .AddTimeTickers(default!, default);
    }

    [Fact]
    public async Task RetryFromAsync_AbortsAcquiredGenerationWhenDispatchFails()
    {
        var root = new TimeTickerEntity
        {
            Id = Guid.NewGuid(), Function = ValidFunctionName,
            Status = TickerStatus.InProgress, ExecutionTime = FixedUtcNow
        };
        var selected = new TimeTickerEntity
        {
            Id = Guid.NewGuid(), ParentId = root.Id, Function = TypedFunctionName,
            Status = TickerStatus.Idle
        };
        var generation = Guid.NewGuid();
        root.Children.Add(selected);
        root.AcquisitionToken = generation;
        root.ChainRootId = root.Id;
        root.ChainGeneration = generation;
        selected.ChainRootId = root.Id;
        selected.ChainGeneration = generation;
        _dispatcher.IsEnabled.Returns(true);
        _persistenceProvider.AcquireTimeTickerChainRetryAsync(
                selected.Id, FixedUtcNow, Arg.Any<CancellationToken>())
            .Returns(new TimeTickerChainRetryAcquisition<TimeTickerEntity>
            {
                Root = root,
                SelectedId = selected.Id,
                PreviousRootStatus = TickerStatus.Failed
            });
        _dispatcher.DispatchAsync(Arg.Any<InternalFunctionContext[]>(), CancellationToken.None)
            .Returns(Task.FromException(new InvalidOperationException("queue full")));
        _persistenceProvider.AbortTimeTickerChainRetryAsync(
                root.Id, generation, Arg.Any<string>(), FixedUtcNow, CancellationToken.None)
            .Returns(true);

        var result = await _timeTickerManager.RetryFromAsync(selected.Id);

        Assert.False(result.IsSucceeded);
        Assert.Contains("could not be queued", result.Exception!.Message,
            StringComparison.OrdinalIgnoreCase);
        await _persistenceProvider.Received(1).AbortTimeTickerChainRetryAsync(
            root.Id, generation, "The chain retry could not be dispatched.",
            FixedUtcNow, CancellationToken.None);
    }
}
