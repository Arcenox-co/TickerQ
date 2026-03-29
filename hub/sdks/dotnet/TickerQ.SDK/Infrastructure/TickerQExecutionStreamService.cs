using TickerQ.Grpc.Contracts;
using TickerQ.SDK.Client;
using TickerQ.Utilities;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Models;
using TickerType = TickerQ.Utilities.Enums.TickerType;
using TickerStatus = TickerQ.Utilities.Enums.TickerStatus;
using RunCondition = TickerQ.Utilities.Enums.RunCondition;

namespace TickerQ.SDK.Infrastructure;

internal sealed class TickerQExecutionStreamService : BackgroundService
{
    private readonly TickerQSdkGrpcClient _grpcClient;
    private readonly TickerSdkOptions _options;
    private readonly TickerQGrpcChannelProvider _channelProvider;
    private readonly TickerQFunctionSyncService _syncService;
    private readonly ILogger<TickerQExecutionStreamService>? _logger;
    private readonly ITickerFunctionConcurrencyGate? _concurrencyGate;
    private readonly ITickerQTaskScheduler? _scheduler;
    private readonly ITickerExecutionTaskHandler _taskHandler;

    private static readonly TimeSpan InitialReconnectDelay = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan MaxReconnectDelay = TimeSpan.FromSeconds(30);

    public TickerQExecutionStreamService(
        TickerQSdkGrpcClient grpcClient,
        TickerSdkOptions options,
        TickerQGrpcChannelProvider channelProvider,
        IServiceProvider serviceProvider,
        TickerQFunctionSyncService syncService,
        ILogger<TickerQExecutionStreamService>? logger = null)
    {
        _grpcClient = grpcClient ?? throw new ArgumentNullException(nameof(grpcClient));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _channelProvider = channelProvider ?? throw new ArgumentNullException(nameof(channelProvider));
        _syncService = syncService ?? throw new ArgumentNullException(nameof(syncService));
        _logger = logger;
        _concurrencyGate = serviceProvider.GetService<ITickerFunctionConcurrencyGate>();
        _scheduler = serviceProvider.GetService<ITickerQTaskScheduler>();
        _taskHandler = serviceProvider.GetRequiredService<ITickerExecutionTaskHandler>();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await _channelProvider.GetChannelAsync().ConfigureAwait(false);

        var delay = InitialReconnectDelay;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunStreamAsync(stoppingToken).ConfigureAwait(false);
                delay = InitialReconnectDelay;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(
                    "ExecutionStream disconnected from RemoteExecutor ({RemoteExecutorUrl}). " +
                    "Reconnecting in {Delay}s... Error: {Error}",
                    _options.ApiUri, delay.TotalSeconds, ex.Message);
                await Task.Delay(delay, stoppingToken).ConfigureAwait(false);
                delay = TimeSpan.FromSeconds(Math.Min(delay.TotalSeconds * 2, MaxReconnectDelay.TotalSeconds));
            }
        }
    }

    private async Task RunStreamAsync(CancellationToken stoppingToken)
    {
        using var stream = _grpcClient.OpenExecutionStream();

        await stream.RequestStream.WriteAsync(new ExecutionResult
        {
            NodeReady = new NodeReady { NodeName = _options.NodeName ?? "sdk-node" }
        }, stoppingToken);

        _logger?.LogInformation("ExecutionStream connected to RemoteExecutor ({RemoteExecutorUrl})", _options.ApiUri);

        while (await stream.ResponseStream.MoveNext(stoppingToken))
        {
            var command = stream.ResponseStream.Current;
            switch (command.CommandCase)
            {
                case ExecutionCommand.CommandOneofCase.DispatchTask:
                    HandleDispatch(command.DispatchTask);
                    break;

                case ExecutionCommand.CommandOneofCase.Resync:
                    _ = HandleResyncAsync(stoppingToken);
                    break;
            }
        }
    }

    private void HandleDispatch(DispatchTask task)
    {
        var functionContext = new InternalFunctionContext
        {
            FunctionName = task.FunctionName,
            TickerId = Guid.Parse(task.Id),
            ParentId = null,
            Type = (TickerType)(int)task.Type,
            Retries = 0,
            RetryCount = task.RetryCount,
            Status = TickerStatus.Idle,
            ExecutionTime = task.ScheduledFor.ToDateTime(),
            RunCondition = RunCondition.OnSuccess
        };

        if (TickerFunctionProvider.TickerFunctions.TryGetValue(functionContext.FunctionName, out var tickerItem))
        {
            functionContext.CachedDelegate = tickerItem.Delegate;
            functionContext.CachedPriority = tickerItem.Priority;
            functionContext.CachedMaxConcurrency = tickerItem.MaxConcurrency;
        }

        if (_scheduler is null || _scheduler.IsDisposed || _scheduler.IsFrozen) 
            return;
        
        var semaphore = _concurrencyGate?.GetSemaphoreOrNull(functionContext.FunctionName, functionContext.CachedMaxConcurrency);

        _ = _scheduler.QueueAsync(
            async ct =>
            {
                if (semaphore != null)
                    await semaphore.WaitAsync(ct).ConfigureAwait(false);
                try
                {
                    await _taskHandler.ExecuteTaskAsync(functionContext, task.IsDue, ct).ConfigureAwait(false);
                }
                finally
                {
                    semaphore?.Release();
                }
            },
            tickerItem.Priority,
            cancellationToken: CancellationToken.None);
    }

    private async Task HandleResyncAsync(CancellationToken stoppingToken)
    {
        try
        {
            await _syncService.SyncAsync(stoppingToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Resync failed");
        }
    }
}
