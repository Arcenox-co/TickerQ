using Grpc.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TickerQ.Grpc.Contracts;
using TickerQ.RemoteExecutor.Execution;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Interfaces.Managers;
using TickerQ.Utilities.Models;

namespace TickerQ.RemoteExecutor.GrpcServices;

internal sealed class ExecutionGrpcService : ExecutionService.ExecutionServiceBase
{
    private readonly GrpcNodeConnectionManager _connectionManager;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ExecutionGrpcService>? _logger;

    public ExecutionGrpcService(
        GrpcNodeConnectionManager connectionManager,
        IServiceProvider serviceProvider,
        ILogger<ExecutionGrpcService>? logger = null)
    {
        _connectionManager = connectionManager ?? throw new ArgumentNullException(nameof(connectionManager));
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = logger;
    }

    public override async Task ExecutionStream(
        IAsyncStreamReader<ExecutionResult> requestStream,
        IServerStreamWriter<ExecutionCommand> responseStream,
        ServerCallContext context)
    {
        string? nodeName = null;

        try
        {
            // First message must be NodeReady
            if (!await requestStream.MoveNext(context.CancellationToken))
                return;

            var first = requestStream.Current;
            if (first.ResultCase != ExecutionResult.ResultOneofCase.NodeReady)
            {
                _logger?.LogWarning("First message on ExecutionStream was not NodeReady, closing stream");
                return;
            }

            nodeName = first.NodeReady.NodeName;
            var maxConcurrency = first.NodeReady.MaxConcurrency;

            _connectionManager.RegisterNode(nodeName, responseStream, maxConcurrency, context.CancellationToken);
            _logger?.LogInformation("SDK node {NodeName} connected (maxConcurrency={MaxConcurrency})",
                nodeName, maxConcurrency);

            // Read loop: process results from the SDK node
            while (await requestStream.MoveNext(context.CancellationToken))
            {
                var msg = requestStream.Current;

                switch (msg.ResultCase)
                {
                    case ExecutionResult.ResultOneofCase.TaskCompleted:
                        _logger?.LogDebug("Task {TickerId} completed from node {NodeName}",
                            msg.TaskCompleted.TickerId, nodeName);
                        _connectionManager.RecordTaskResult(nodeName, success: true);
                        await HandleTaskCompletedAsync(msg.TaskCompleted, context.CancellationToken);
                        break;

                    case ExecutionResult.ResultOneofCase.TaskFailed:
                        _logger?.LogWarning("Task {TickerId} failed from node {NodeName}: {Error}",
                            msg.TaskFailed.TickerId, nodeName, msg.TaskFailed.ExceptionDetails);
                        _connectionManager.RecordTaskResult(nodeName, success: false);
                        await HandleTaskFailedAsync(msg.TaskFailed, context.CancellationToken);
                        break;

                    case ExecutionResult.ResultOneofCase.CapacityUpdate:
                        _connectionManager.UpdateNodeCapacity(
                            nodeName,
                            msg.CapacityUpdate.ActiveTasks,
                            msg.CapacityUpdate.MaxConcurrency);
                        break;

                    case ExecutionResult.ResultOneofCase.DrainSignal:
                        _connectionManager.HandleDrainSignalFromNode(nodeName);
                        break;

                    case ExecutionResult.ResultOneofCase.DrainComplete:
                        _connectionManager.HandleDrainComplete(nodeName, msg.DrainComplete.TasksDrained);
                        break;
                }
            }
        }
        catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)
        {
            _logger?.LogInformation("ExecutionStream for node '{NodeName}' closed (server shutdown)", nodeName);
        }
        catch (IOException)
        {
            _logger?.LogWarning("SDK node '{NodeName}' disconnected (connection lost or node shutdown)", nodeName);
        }
        catch (Exception ex)
        {
            _logger?.LogError("ExecutionStream error for node '{NodeName}': {Error}", nodeName, ex.Message);
        }
        finally
        {
            if (nodeName != null)
            {
                _connectionManager.UnregisterNode(nodeName);
                _logger?.LogInformation("SDK node {NodeName} disconnected from ExecutionStream", nodeName);
            }
        }
    }

    private async Task HandleTaskCompletedAsync(TaskCompleted result, CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = _serviceProvider.CreateAsyncScope();
            var tickerManager = scope.ServiceProvider.GetService<IInternalTickerManager>();
            if (tickerManager == null) return;

            var ctx = new InternalFunctionContext
            {
                TickerId = Guid.Parse(result.TickerId),
                FunctionName = result.FunctionName,
                Type = (Utilities.Enums.TickerType)(int)result.Type
            };

            ctx.SetProperty(x => x.Status, (Utilities.Enums.TickerStatus)(int)result.Status)
               .SetProperty(x => x.ElapsedTime, result.ElapsedMs)
               .SetProperty(x => x.ExecutedAt, result.ExecutedAt?.ToDateTime() ?? DateTime.UtcNow);

            await tickerManager.UpdateTickerAsync(ctx, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to process TaskCompleted for {TickerId}", result.TickerId);
        }
    }

    private async Task HandleTaskFailedAsync(TaskFailed result, CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = _serviceProvider.CreateAsyncScope();
            var tickerManager = scope.ServiceProvider.GetService<IInternalTickerManager>();
            if (tickerManager == null) return;

            var ctx = new InternalFunctionContext
            {
                TickerId = Guid.Parse(result.TickerId),
                FunctionName = result.FunctionName,
                Type = (Utilities.Enums.TickerType)(int)result.Type
            };

            ctx.SetProperty(x => x.Status, Utilities.Enums.TickerStatus.Failed)
               .SetProperty(x => x.ExceptionDetails, result.ExceptionDetails)
               .SetProperty(x => x.ElapsedTime, result.ElapsedMs)
               .SetProperty(x => x.ExecutedAt, result.ExecutedAt?.ToDateTime() ?? DateTime.UtcNow);

            await tickerManager.UpdateTickerAsync(ctx, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to process TaskFailed for {TickerId}", result.TickerId);
        }
    }

    public override async Task<LogAck> SendLogs(
        IAsyncStreamReader<LogEntry> requestStream,
        ServerCallContext context)
    {
        while (await requestStream.MoveNext(context.CancellationToken))
        {
            var log = requestStream.Current;
            _logger?.LogDebug("[{Level}] [{ExecutionId}] {FunctionName}: {Message}",
                log.Level, log.ExecutionId, log.FunctionName, log.Message);

            // TODO: Forward to dashboard via SignalR / persist as needed
        }

        return new LogAck();
    }
}
