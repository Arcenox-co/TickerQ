using TickerQ.SDK.Client;
using TickerQ.SDK.Models;
using TickerQ.Grpc.Contracts;
using TickerQ.Utilities;

namespace TickerQ.SDK.Infrastructure;

internal sealed class TickerQFunctionSyncService
{
    private readonly TickerQSdkGrpcClient _grpcClient;
    private readonly TickerQGrpcChannelProvider _channelProvider;
    private readonly TickerSdkOptions _options;

    public TickerQFunctionSyncService(
        TickerQSdkGrpcClient grpcClient,
        TickerQGrpcChannelProvider channelProvider,
        TickerSdkOptions options)
    {
        _grpcClient = grpcClient ?? throw new ArgumentNullException(nameof(grpcClient));
        _channelProvider = channelProvider ?? throw new ArgumentNullException(nameof(channelProvider));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public async Task<SyncNodesAndFunctionsResult?> SyncAsync(CancellationToken cancellationToken)
    {
        if (TickerFunctionProvider.TickerFunctions == null ||
            TickerFunctionProvider.TickerFunctions.Count == 0)
        {
            return null;
        }

        // Step 1: Build sync request with all local functions
        var syncRequest = new SyncNodesFunctionsRequest
        {
            NodeName = _options.NodeName ?? "node",
            CallbackUrl = _options.CallbackUri?.ToString() ?? string.Empty
        };

        var grpcFunctions = new List<FunctionDescriptor>();

        foreach (var (name, value) in TickerFunctionProvider.TickerFunctions)
        {
            TickerFunctionProvider.TickerFunctionRequestTypes.TryGetValue(name, out var requestType);
            var exampleJson = string.Empty;
            if (requestType.Item2 != null)
                JsonExampleGenerator.TryGenerateExampleJson(requestType.Item2, out exampleJson);

            var (cronExpression, priority, _, _) = value;

            syncRequest.Functions.Add(new SyncFunctionDescriptor
            {
                FunctionName = name,
                RequestType = requestType.Item1 ?? string.Empty,
                RequestExampleJson = exampleJson ?? string.Empty,
                TaskPriority = (TickerQ.Grpc.Contracts.TickerTaskPriority)(int)priority,
                Expression = cronExpression ?? string.Empty
            });

            grpcFunctions.Add(new FunctionDescriptor
            {
                Name = name,
                CronExpression = cronExpression ?? string.Empty,
                RequestType = requestType.Item1 ?? string.Empty,
                RequestExampleJson = exampleJson ?? string.Empty,
                Priority = (TickerQ.Grpc.Contracts.TickerTaskPriority)(int)priority,
                IsActive = true
            });
        }

        // Step 2: Sync with Hub via gRPC
        var response = await _grpcClient.SyncNodesFunctionsAsync(syncRequest, cancellationToken)
            .ConfigureAwait(false);

        SyncNodesAndFunctionsResult? result = null;

        if (response != null)
        {
            result = new SyncNodesAndFunctionsResult
            {
                ApplicationUrl = response.ApplicationUrl,
                WebhookSignature = response.WebhookSignature
            };

            if (!string.IsNullOrWhiteSpace(response.ApplicationUrl))
            {
                _options.ApiUri = new Uri(response.ApplicationUrl.TrimEnd('/') + "/");
            }

            if (!string.IsNullOrWhiteSpace(response.WebhookSignature))
            {
                _options.WebhookSignature = response.WebhookSignature;
            }
        }

        // Step 3: Initialize gRPC channel now that we have the scheduler URL
        _channelProvider.Initialize();

        // Step 4: Register functions with RemoteExecutor via gRPC
        if (grpcFunctions.Count > 0)
        {
            await _grpcClient.RegisterFunctionsAsync(
                grpcFunctions.ToArray(),
                _options.NodeName ?? "node",
                cancellationToken).ConfigureAwait(false);
        }

        return result;
    }
}
