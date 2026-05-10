using Grpc.Core;
using Grpc.Net.Client;
using TickerQ.RemoteExecutor.Hub;
using TickerQ.Utilities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Interfaces.Managers;

namespace TickerQ.RemoteExecutor;

/// <summary>
/// Pulls active nodes/functions from the Hub on startup via gRPC and registers
/// each as a TickerFunction whose delegate POSTs to the SDK's callback URL.
/// </summary>
public class RemoteFunctionsSyncService : BackgroundService
{
    private readonly TickerQRemoteExecutionOptions _options;
    private readonly IInternalTickerManager? _internalTickerManager;
    private readonly ILogger<RemoteFunctionsSyncService>? _logger;

    public RemoteFunctionsSyncService(
        TickerQRemoteExecutionOptions options,
        IServiceProvider serviceProvider,
        ILogger<RemoteFunctionsSyncService>? logger = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _internalTickerManager = serviceProvider.GetService<IInternalTickerManager>();
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await SyncOnceAsync(stoppingToken);
    }

    public async Task SyncOnceAsync(CancellationToken stoppingToken)
    {
        try
        {
            var grpcUrl = _options.HubGrpcEndpointUrl;
            if (string.IsNullOrWhiteSpace(grpcUrl))
            {
                _logger?.LogWarning("HubGrpcEndpointUrl is not configured. Skipping remote functions sync.");
                return;
            }

            // Allow gRPC over plaintext for local dev (http://) Hub deployments.
            if (grpcUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
                AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);

            _logger?.LogInformation("Starting remote functions sync from {EndpointUrl} (gRPC)", grpcUrl);

            using var channel = GrpcChannel.ForAddress(grpcUrl.TrimEnd('/'), new GrpcChannelOptions
            {
                MaxReceiveMessageSize = 16 * 1024 * 1024
            });
            var client = new HubService.HubServiceClient(channel);

            var headers = new Metadata
            {
                { "x-api-key", _options.ApiKey ?? string.Empty }
            };

            GetRegisteredFunctionsResponse response;
            try
            {
                response = await client.GetRegisteredFunctionsAsync(
                    new GetRegisteredFunctionsRequest(),
                    headers,
                    deadline: DateTime.UtcNow.AddSeconds(30),
                    cancellationToken: stoppingToken);
            }
            catch (RpcException rpcEx)
            {
                _logger?.LogError(rpcEx,
                    "gRPC GetRegisteredFunctions failed: {Status} - {Detail}",
                    rpcEx.StatusCode, rpcEx.Status.Detail);
                return;
            }

            _options.WebHookSignature = response.WebhookSignature;
            await RegisterFunctionsFromResponse(response, stoppingToken);

            _logger?.LogInformation("Remote functions sync completed successfully");
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _logger?.LogInformation("Remote functions sync cancelled due to application shutdown.");
        }
        // Other exceptions are NOT caught and will propagate — fail fast on programming errors.
    }

    private async Task RegisterFunctionsFromResponse(GetRegisteredFunctionsResponse response, CancellationToken cancellationToken)
    {
        var functionDict = TickerFunctionProvider.TickerFunctions.ToDictionary();
        var cronPairs = new List<(string Name, string CronExpression)>();
        var requestInfoDict = new Dictionary<string, (string RequestType, string RequestExampleJson)>();

        // Track every (active) remote function we see in this sync so we can reconcile the
        // local registry afterwards: anything previously known but not in the response was
        // disabled/removed at the Hub and must be unapplied locally.
        var seenFunctionNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var node in response.Nodes)
        {
            // CallbackUrl is no longer required — pure-client SDKs don't expose one. Dispatch
            // happens over the worker stream the SDK opens to the scheduler. Skip empty-name
            // nodes only.
            if (string.IsNullOrWhiteSpace(node.NodeName))
            {
                _logger?.LogWarning("Node has no name, skipping");
                continue;
            }

            if (node.Functions.Count == 0)
            {
                _logger?.LogInformation("Node {NodeName} has no functions", node.NodeName);
                continue;
            }

            foreach (var function in node.Functions)
            {
                if (string.IsNullOrWhiteSpace(function.FunctionName))
                {
                    _logger?.LogWarning("Function has no name, skipping");
                    continue;
                }

                // Node-qualify the registry key so two SDKs can host functions with the same
                // bare name without colliding. Tickers persist the qualified name too (qualified
                // at every creation entry point), so dispatch lookups by ticker.Function still hit.
                // Bare->node mapping is tracked separately via RemoteFunctionRegistry for
                // server-side resolution at ticker creation time.
                var qualifiedName = $"{function.FunctionName}@{node.NodeName}";

                if (!function.IsActive)
                {
                    if (RemoteFunctionRegistry.IsRemote(function.FunctionName) &&
                        functionDict.Remove(qualifiedName))
                    {
                        requestInfoDict.Remove(qualifiedName);
                        RemoteFunctionRegistry.Remove(function.FunctionName);
                        _logger?.LogDebug("Removed inactive remote function {FunctionName}@{NodeName}",
                            function.FunctionName, node.NodeName);
                    }
                    else
                    {
                        _logger?.LogDebug("Skipping inactive function {FunctionName}@{NodeName}",
                            function.FunctionName, node.NodeName);
                    }
                    continue;
                }

                // Dispatch goes via the worker stream — no callback URL is dialed; the SDK
                // is identified by NodeName and reached through its open stream registered
                // in WorkerStreamRegistry.
                var functionDelegate = RemoteExecutionDelegateFactory.Create(node.NodeName);

                var priority = (TickerTaskPriority)(int)function.TaskPriority;
                var cronExpression = function.NodeExpression ?? string.Empty;

                functionDict[qualifiedName] = (cronExpression, priority, functionDelegate, 0);
                RemoteFunctionRegistry.MarkRemote(function.FunctionName, node.NodeName);
                requestInfoDict[qualifiedName] = (
                    function.RequestType,
                    function.RequestExampleJson ?? string.Empty);
                seenFunctionNames.Add(qualifiedName);

                if (node.AutoMigrateExpressions && !string.IsNullOrWhiteSpace(cronExpression))
                {
                    cronPairs.Add((qualifiedName, cronExpression));
                }

                _logger?.LogDebug("Registered function {QualifiedName}", qualifiedName);
            }
        }

        // Reconcile: any function we previously registered as remote that did NOT appear
        // in the response was disabled or removed at the Hub. functionDict is keyed by the
        // qualified name (`bare@node`) but RemoteFunctionRegistry tracks bare names — combine
        // the two so we know which qualified key to remove.
        foreach (var bareName in RemoteFunctionRegistry.SnapshotFunctionNames())
        {
            var node = RemoteFunctionRegistry.GetNodeName(bareName);
            var qualified = string.IsNullOrEmpty(node) ? bareName : $"{bareName}@{node}";
            if (seenFunctionNames.Contains(qualified)) continue;
            if (functionDict.Remove(qualified))
            {
                requestInfoDict.Remove(qualified);
                RemoteFunctionRegistry.Remove(bareName);
                _logger?.LogInformation(
                    "Unapplied stale remote function {QualifiedName} (no longer reported by Hub)",
                    qualified);
            }
        }

        // SDK connection liveness is tracked by WorkerStreamRegistry now (one entry per open
        // worker stream). The Hub knows about each SDK via the Sdks tunnel report which is
        // sourced from that registry — no separate cache to reconcile here.

        // Replace (not merge) so functions removed/disabled in the Hub are also removed locally.
        TickerFunctionProvider.ReplaceFunctions(functionDict);
        TickerFunctionProvider.ReplaceRequestInfo(requestInfoDict);
        _logger?.LogInformation("Registered {Count} functions", functionDict.Count);

        if (cronPairs.Count > 0 && _internalTickerManager != null)
        {
            await _internalTickerManager.MigrateDefinedCronTickers(
                cronPairs.ToArray(),
                cancellationToken)
                .ConfigureAwait(false);

            _logger?.LogInformation("Migrated {Count} cron tickers", cronPairs.Count);
        }
    }
}
