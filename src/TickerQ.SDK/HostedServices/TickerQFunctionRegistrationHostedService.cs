using Microsoft.Extensions.Hosting;
using TickerQ.SDK.Client;
using TickerQ.SDK.Models;
using TickerQ.Utilities;

namespace TickerQ.SDK.HostedServices;

/// <summary>
/// Sends the list of registered ticker functions to the remote executor on application startup.
/// The delegate itself is not transmitted; instead, a callback name is provided.
/// </summary>
internal sealed class TickerQFunctionRegistrationHostedService : IHostedService
{
    private readonly TickerQSdkHttpClient _client;
    private readonly TickerSdkOptions _options;
    public TickerQFunctionRegistrationHostedService(TickerQSdkHttpClient client, TickerSdkOptions options)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        // Ensure the functions dictionary is built from any registered callbacks.
        if (TickerFunctionProvider.TickerFunctions == null ||
            TickerFunctionProvider.TickerFunctions.Count == 0)
        {
            return;
        }

        var node = new Node
        {
            NodeName = "webapp4",
            CallbackUrl = _options.CallbackUri?.ToString(),
            Functions = []
        };
        
        foreach (var (name, value) in TickerFunctionProvider.TickerFunctions)
        {
            TickerFunctionProvider.TickerFunctionRequestTypes.TryGetValue(name, out var requestType);

            var (cronExpression, priority, _) = value;
            node.Functions.Add(new NodeFunction
            {
                FunctionName =  name,
                RequestType = requestType.Item1 ?? string.Empty,
                TaskPriority = priority,
                Expression =  cronExpression
            });
        }

        // POST the function definitions to a remote endpoint.
        // The remote executor should expose this endpoint and interpret the callback string.
        var result = await _client
            .PostAsync<Node, SyncNodesAndFunctionsResult?>(
                "api/apps/sync/nodes-functions/batch",
                node,
                cancellationToken)
            .ConfigureAwait(false);

        _options.ApiUri = new Uri(result.ApplicationUrl);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

