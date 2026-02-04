using TickerQ.SDK.Client;
using TickerQ.SDK.Models;
using TickerQ.Utilities;

namespace TickerQ.SDK.Infrastructure;

internal sealed class TickerQFunctionSyncService
{
    private readonly TickerQSdkHttpClient _client;
    private readonly TickerSdkOptions _options;

    public TickerQFunctionSyncService(TickerQSdkHttpClient client, TickerSdkOptions options)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public async Task<SyncNodesAndFunctionsResult?> SyncAsync(CancellationToken cancellationToken)
    {
        if (TickerFunctionProvider.TickerFunctions == null ||
            TickerFunctionProvider.TickerFunctions.Count == 0)
        {
            return null;
        }

        var node = new Node
        {
            NodeName = _options.NodeName ?? "node",
            CallbackUrl = _options.CallbackUri?.ToString(),
            Functions = []
        };

        foreach (var (name, value) in TickerFunctionProvider.TickerFunctions)
        {
            TickerFunctionProvider.TickerFunctionRequestTypes.TryGetValue(name, out var requestType);
            var exampleJson = string.Empty;
            if (requestType.Item2 != null)
                JsonExampleGenerator.TryGenerateExampleJson(requestType.Item2, out exampleJson);

            var (cronExpression, priority, _) = value;
            node.Functions.Add(new NodeFunction
            {
                FunctionName = name,
                RequestType = requestType.Item1 ?? string.Empty,
                RequestExampleJson = exampleJson,
                TaskPriority = priority,
                CronExpression = cronExpression
            });
        }

        var hubBase = new Uri(TickerQSdkConstants.HubBaseUrl);
        var syncUri = new Uri(hubBase, "api/apps/sync/nodes-functions/batch");

        var result = await _client
            .PostAsync<Node, SyncNodesAndFunctionsResult?>(
                syncUri.ToString(),
                node,
                cancellationToken)
            .ConfigureAwait(false);

        if (result != null)
        {
            if (!string.IsNullOrWhiteSpace(result.ApplicationUrl))
            {
                _options.ApiUri = new Uri(result.ApplicationUrl.TrimEnd('/') + "/");
            }

            if (!string.IsNullOrWhiteSpace(result.WebhookSignature))
            {
                _options.WebhookSignature = result.WebhookSignature;
            }
        }

        return result;
    }
}
