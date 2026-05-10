using Grpc.Core;
using Grpc.Net.Client;
using TickerQ.SDK.Hub;
using TickerQ.Utilities;

namespace TickerQ.SDK.Infrastructure;

/// <summary>
/// Boot-time function-sync. Calls Hub's <c>HubService.SyncNodesFunctions</c> over gRPC
/// to register this SDK's node + function manifest with the env identified by the
/// API token, then captures the env's ApplicationUrl + WebhookSignature into options
/// so the worker stream knows where to dial the Scheduler.
/// </summary>
internal sealed class TickerQFunctionSyncService
{
    private readonly TickerSdkOptions _options;
    private GrpcChannel? _channel;

    public TickerQFunctionSyncService(TickerSdkOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public async Task<SyncNodesFunctionsResponse?> SyncAsync(CancellationToken cancellationToken)
    {
        if (TickerFunctionProvider.TickerFunctions == null ||
            TickerFunctionProvider.TickerFunctions.Count == 0)
        {
            return null;
        }

        var request = new SyncNodesFunctionsRequest
        {
            NodeName = _options.NodeName,
            // Pure-client SDK — no callback URL. Field kept on the wire for compat
            // with existing Hub deployments; will be ignored server-side.
            CallbackUrl = string.Empty,
            SdkType = TickerSdkOptions.SdkType
        };

        foreach (var (name, value) in TickerFunctionProvider.TickerFunctions)
        {
            TickerFunctionProvider.TickerFunctionRequestTypes.TryGetValue(name, out var requestType);
            var exampleJson = string.Empty;
            if (requestType.Item2 != null)
                JsonExampleGenerator.TryGenerateExampleJson(requestType.Item2, out exampleJson);

            var (cronExpression, priority, _, _) = value;
            request.Functions.Add(new SyncFunctionDescriptor
            {
                FunctionName = name,
                RequestType = requestType.Item1 ?? string.Empty,
                RequestExampleJson = exampleJson ?? string.Empty,
                TaskPriority = (HubTaskPriority)(int)priority,
                Expression = cronExpression ?? string.Empty
            });
        }

        var client = new HubService.HubServiceClient(GetChannel());
        var headers = new Metadata { { "x-api-key", _options.ApiKey ?? string.Empty } };

        var response = await client
            .SyncNodesFunctionsAsync(request, headers, cancellationToken: cancellationToken)
            .ResponseAsync
            .ConfigureAwait(false);

        if (response != null)
        {
            if (!string.IsNullOrWhiteSpace(response.ApplicationUrl))
            {
                _options.ApiUri = new Uri(response.ApplicationUrl.TrimEnd('/') + "/");
            }

            if (!string.IsNullOrWhiteSpace(response.WebhookSignature))
            {
                _options.WebhookSignature = response.WebhookSignature;
            }
        }

        return response;
    }

    private GrpcChannel GetChannel()
    {
        if (_channel != null) return _channel;
        _channel = GrpcChannel.ForAddress(_options.HubControlUri, new GrpcChannelOptions
        {
            MaxReceiveMessageSize = 16 * 1024 * 1024
        });
        return _channel;
    }
}
