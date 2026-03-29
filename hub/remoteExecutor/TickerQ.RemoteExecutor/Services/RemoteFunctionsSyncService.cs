using TickerQ.Grpc.Contracts;
using TickerQ.RemoteExecutor.Client;
using TickerQ.Utilities;
using TickerQ.Utilities.Interfaces.Managers;
using TickerTaskPriority = TickerQ.Utilities.Enums.TickerTaskPriority;

namespace TickerQ.RemoteExecutor;

internal class RemoteFunctionsSyncService : BackgroundService
{
    private readonly TickerQHubGrpcChannelProvider _hubChannelProvider;
    private readonly TickerQRemoteExecutionOptions _options;
    private readonly IInternalTickerManager? _internalTickerManager;
    private readonly ILogger<RemoteFunctionsSyncService>? _logger;
    private readonly IServiceProvider _serviceProvider;

    private HubService.HubServiceClient? _hubClient;

    public RemoteFunctionsSyncService(
        TickerQHubGrpcChannelProvider hubChannelProvider,
        TickerQRemoteExecutionOptions options,
        IServiceProvider serviceProvider,
        ILogger<RemoteFunctionsSyncService>? logger = null)
    {
        _hubChannelProvider = hubChannelProvider ?? throw new ArgumentNullException(nameof(hubChannelProvider));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = logger;
        _internalTickerManager = _serviceProvider.GetService<IInternalTickerManager>();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await SyncOnceAsync(stoppingToken);
    }

    public async Task SyncOnceAsync(CancellationToken stoppingToken)
    {
        try
        {
            _logger?.LogInformation("Starting remote functions sync from Hub via gRPC");

            var client = GetHubClient();
            var response = await client.GetRegisteredFunctionsAsync(
                new GetRegisteredFunctionsRequest(),
                cancellationToken: stoppingToken);

            if (response == null)
            {
                _logger?.LogWarning("Received null response from Hub gRPC sync");
                return;
            }

            _options.WebHookSignature = response.WebhookSignature;
            await RegisterFunctionsFromResponse(response, stoppingToken);

            _logger?.LogInformation("Remote functions sync completed successfully");
        }
        catch (global::Grpc.Core.RpcException ex)
        {
            _logger?.LogError(ex,
                "gRPC error during remote functions sync. Status: {StatusCode}. The service will continue without remote functions.",
                ex.StatusCode);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _logger?.LogInformation("Remote functions sync cancelled due to application shutdown.");
        }
    }

    private async Task RegisterFunctionsFromResponse(GetRegisteredFunctionsResponse response, CancellationToken cancellationToken)
    {
        if (response.Nodes.Count == 0)
        {
            _logger?.LogInformation("No nodes found in response");
            return;
        }

        var functionDict = TickerFunctionProvider.TickerFunctions.ToDictionary();
        var cronPairs = new List<(string Name, string CronExpression)>();
        var requestInfoDict = new Dictionary<string, (string RequestType, string RequestExampleJson)>();

        foreach (var node in response.Nodes)
        {
            if (string.IsNullOrWhiteSpace(node.CallbackUrl))
            {
                _logger?.LogWarning("Node {NodeName} has no callback URL, skipping", node.NodeName);
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

                if (!function.IsActive)
                {
                    if (RemoteFunctionRegistry.IsRemote(function.FunctionName) &&
                        functionDict.Remove(function.FunctionName))
                    {
                        requestInfoDict.Remove(function.FunctionName);
                        RemoteFunctionRegistry.Remove(function.FunctionName);
                        _logger?.LogDebug("Removed inactive remote function {FunctionName}", function.FunctionName);
                    }
                    else
                    {
                        _logger?.LogDebug("Skipping inactive function {FunctionName}", function.FunctionName);
                    }
                    continue;
                }

                var nodeName = node.NodeName;
                var functionDelegate = RemoteExecutionDelegateFactory.CreateForGrpcStream(
                    nodeName,
                    sp => sp.GetRequiredService<Execution.GrpcNodeConnectionManager>());

                var priority = (TickerTaskPriority)(int)function.TaskPriority;
                var cronExpression = function.NodeExpression ?? string.Empty;

                functionDict[function.FunctionName] = (cronExpression, priority, functionDelegate, 0);
                RemoteFunctionRegistry.MarkRemote(function.FunctionName);
                requestInfoDict[function.FunctionName] = (
                    function.RequestType,
                    function.RequestExampleJson ?? string.Empty);

                if (node.AutoMigrateExpressions && !string.IsNullOrWhiteSpace(cronExpression))
                {
                    cronPairs.Add((function.FunctionName, cronExpression));
                }

                _logger?.LogDebug("Registered function {FunctionName} from node {NodeName}",
                    function.FunctionName, node.NodeName);
            }
        }

        if (functionDict.Count > 0)
            TickerFunctionProvider.RegisterFunctions(functionDict);

        var existingRequestTypes = TickerFunctionProvider.TickerFunctionRequestTypes;
        if (existingRequestTypes != null && existingRequestTypes.Count > 0)
            TickerFunctionProvider.RegisterRequestType(existingRequestTypes.ToDictionary());

        if (requestInfoDict.Count > 0)
            TickerFunctionProvider.RegisterRequestInfo(requestInfoDict);

        TickerFunctionProvider.Build();
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

    private HubService.HubServiceClient GetHubClient()
    {
        return _hubClient ??= new HubService.HubServiceClient(_hubChannelProvider.GetChannel());
    }
}
