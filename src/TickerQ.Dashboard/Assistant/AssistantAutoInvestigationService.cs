using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TickerQ.Utilities;
using TickerQ.Utilities.DashboardDtos;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Models;

namespace TickerQ.Dashboard.Assistant;

internal sealed class AssistantAutoInvestigationService<TTimeTicker, TCronTicker> : BackgroundService
    where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
    where TCronTicker : CronTickerEntity, new()
{
    private readonly IServiceScopeFactory _scopes;
    private readonly AutomaticInvestigationOptionsBuilder _options;
    private readonly ILogger<AssistantAutoInvestigationService<TTimeTicker, TCronTicker>> _logger;

    public AssistantAutoInvestigationService(IServiceScopeFactory scopes,
        AssistantOptionsBuilder assistant,
        ILogger<AssistantAutoInvestigationService<TTimeTicker, TCronTicker>> logger)
    {
        _scopes = scopes;
        _options = assistant.RunInvestigator.Automatic;
        _logger = logger;
    }

    public override Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopes.CreateScope();
        if (scope.ServiceProvider.GetService<IAssistantInvestigationStore>() == null)
            throw new InvalidOperationException(
                "Automatic AI investigations require an IAssistantInvestigationStore. " +
                "With EF Core, call AddAssistantHistory() and apply the generated migration.");
        if (scope.ServiceProvider.GetService<IAssistantInvestigationCoordinator>() == null)
            throw new InvalidOperationException(
                "Automatic AI investigations require an IAssistantInvestigationCoordinator " +
                "so evidence leases and the hourly model-call budget are shared across nodes. " +
                "With EF Core, call AddAssistantHistory() and apply the generated migration.");
        return base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await RunOnceAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception exception)
                {
                    _logger.LogWarning(exception,
                        "TickerQ automatic AI investigation sweep failed; the next sweep will retry");
                }
                await Task.Delay(_options.PollInterval, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal host shutdown can cancel either delay.
        }
    }

    internal async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopes.CreateScope();
        var data = scope.ServiceProvider.GetRequiredService<ITickerDashboardDataService<TTimeTicker, TCronTicker>>();
        var store = scope.ServiceProvider.GetRequiredService<IAssistantInvestigationStore>();
        var runner = new AssistantInvestigationRunner<TTimeTicker, TCronTicker>(
            scope.ServiceProvider.GetRequiredService<TickerAssistantService>(), data, store, scope.ServiceProvider);
        var scheduler = scope.ServiceProvider.GetService<SchedulerOptionsBuilder>();
        var applicationNamespace = AssistantInvestigationFactory.ResolveNamespace(scheduler);
        var from = DateTime.UtcNow - _options.Lookback;

        if (_options.DetectRecoveries)
        {
            var successes = await data.GetExecutionsFlatAsync(new ExecutionQueryFilter
            {
                Statuses = [TickerStatus.Done, TickerStatus.DueDone],
                ExecutedFrom = from,
                PageSize = Math.Min(50, _options.BatchSize * 2),
                SortDescending = true,
            }, cancellationToken);
            foreach (var execution in successes.Items)
                await store.MarkRecoveredAsync(applicationNamespace, execution.FunctionName,
                    execution.Id, execution.ExecutedAt ?? execution.ScheduledFor, cancellationToken);
        }

        var statuses = _options.IncludeCancelled
            ? new[] { TickerStatus.Failed, TickerStatus.Cancelled }
            : new[] { TickerStatus.Failed };
        var failures = await data.GetExecutionsFlatAsync(new ExecutionQueryFilter
        {
            Statuses = statuses,
            ExecutedFrom = from,
            PageSize = _options.BatchSize,
            SortDescending = true,
        }, cancellationToken);

        foreach (var execution in failures.Items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (execution.Status == TickerStatus.Failed && execution.RetryCount < execution.Retries) continue;
            try
            {
                await runner.RunAsync(execution.Id, AssistantInvestigationValues.Automatic,
                    userId: null, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception,
                    "Automatic AI investigation failed for execution {ExecutionId}", execution.Id);
            }
        }
    }

}
