using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TickerQ.RemoteExecutor.Models;
using TickerQ.Utilities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Interfaces.Managers;

namespace TickerQ.RemoteExecutor;

public class RemoteFunctionsSyncService : BackgroundService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly TickerQRemoteExecutionOptions _options;
    private readonly IInternalTickerManager? _internalTickerManager;
    private readonly ILogger<RemoteFunctionsSyncService>? _logger;
    private readonly IServiceProvider _serviceProvider;

    public RemoteFunctionsSyncService(
        IHttpClientFactory httpClientFactory,
        TickerQRemoteExecutionOptions options,
        IServiceProvider serviceProvider,
        ILogger<RemoteFunctionsSyncService>? logger = null)
    {
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = logger;
        
        // Try to get IInternalTickerManager, but it's optional
        _internalTickerManager = _serviceProvider.GetService<IInternalTickerManager>();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Run once on startup
        try
        {
            if (string.IsNullOrWhiteSpace(_options.FunctionsEndpointUrl))
            {
                _logger?.LogWarning("FunctionsEndpointUrl is not configured. Skipping remote functions sync.");
                return;
            }

            _logger?.LogInformation("Starting remote functions sync from {EndpointUrl}", _options.FunctionsEndpointUrl);

            var httpClient = _httpClientFactory.CreateClient("tickerq-callback");
            using var httpResponse =
                await httpClient.GetAsync($"{_options.FunctionsEndpointUrl}api/apps/sync/nodes-functions",
                    stoppingToken);

            if (!httpResponse.IsSuccessStatusCode)
            {
                var errorContent = await httpResponse.Content.ReadAsStringAsync(stoppingToken);
                _logger?.LogError(
                    "Failed to fetch functions from {EndpointUrl}. Status: {StatusCode}, Response: {Response}",
                    _options.FunctionsEndpointUrl,
                    httpResponse.StatusCode,
                    errorContent);
                return;
            }

            var contentType = httpResponse.Content.Headers.ContentType?.MediaType ?? string.Empty;
            if (!contentType.Contains("json", StringComparison.OrdinalIgnoreCase))
            {
                var responseContent = await httpResponse.Content.ReadAsStringAsync(stoppingToken);
                _logger?.LogError(
                    "Received non-JSON response from {EndpointUrl}. Content-Type: {ContentType}, Response (first 500 chars): {Response}",
                    _options.FunctionsEndpointUrl,
                    contentType,
                    responseContent.Length > 500 ? responseContent.Substring(0, 500) : responseContent);
                return;
            }

            var response = await httpResponse.Content.ReadFromJsonAsync<RegisteredFunctionsResponse>(
                cancellationToken: stoppingToken);

            if (response == null)
            {
                _logger?.LogWarning("Received null response from functions endpoint");
                return;
            }

            await RegisterFunctionsFromResponse(response, stoppingToken);
            
            _logger?.LogInformation("Remote functions sync completed successfully");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error during remote functions sync. The service will continue without remote functions.");
            // Don't throw - allow the service to complete gracefully even if sync fails
        }
    }

    private async Task RegisterFunctionsFromResponse(RegisteredFunctionsResponse response, CancellationToken cancellationToken)
    {
        if (response.Nodes == null || response.Nodes.Count == 0)
        {
            _logger?.LogInformation("No nodes found in response");
            return;
        }

        var functionDict = TickerFunctionProvider.TickerFunctions.ToDictionary();
        var cronPairs = new List<(string Name, string CronExpression)>();

        foreach (var node in response.Nodes)
        {
            if (string.IsNullOrWhiteSpace(node.CallbackUrl))
            {
                _logger?.LogWarning("Node {NodeName} has no callback URL, skipping", node.NodeName);
                continue;
            }

            if (node.Functions == null || node.Functions.Count == 0)
            {
                _logger?.LogInformation("Node {NodeName} has no functions", node.NodeName);
                continue;
            }

            foreach (var function in node.Functions)
            {
                if (!function.IsActive)
                {
                    _logger?.LogDebug("Skipping inactive function {FunctionName}", function.FunctionName);
                    continue;
                }

                if (string.IsNullOrWhiteSpace(function.FunctionName))
                {
                    _logger?.LogWarning("Function has no name, skipping");
                    continue;
                }

                // Capture callbackUrl in local variable to avoid closure issues
                var callbackUrl = node.CallbackUrl.TrimEnd('/');
                
                // Create function delegate similar to MapFunctionRegistration
                var functionDelegate = new TickerFunctionDelegate(async (ct, serviceProvider, context) =>
                {
                    var httpClientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();
                    var httpClient = httpClientFactory.CreateClient("tickerq-callback");
                    
                    // Build a minimal payload describing the execution
                    var payload = new
                    {
                        context.Id,
                        context.FunctionName,
                        context.Type,
                        context.RetryCount,
                        context.ScheduledFor
                    };

                    // Use the node's callbackUrl for the remote endpoint
                    using var response = await httpClient.PostAsJsonAsync(
                        new Uri($"{callbackUrl}/execute"),
                        payload,
                        ct);

                    response.EnsureSuccessStatusCode();
                });

                // Convert int priority to TickerTaskPriority enum
                var priority = (TickerTaskPriority)function.TaskPriority;
                
                // Use nodeExpression as cron expression if available
                var cronExpression = function.NodeExpression ?? string.Empty;
                
                functionDict.TryAdd(function.FunctionName, (cronExpression, priority, functionDelegate));
                
                if (!string.IsNullOrWhiteSpace(cronExpression))
                {
                    cronPairs.Add((function.FunctionName, cronExpression));
                }

                _logger?.LogDebug("Registered function {FunctionName} from node {NodeName}", 
                    function.FunctionName, node.NodeName);
            }
        }

        if (functionDict.Count > 0)
        {
            TickerFunctionProvider.RegisterFunctions(functionDict);
            TickerFunctionProvider.Build();
            
            _logger?.LogInformation("Registered {Count} functions", functionDict.Count);
        }

        // Migrate cron tickers if we have cron expressions and the manager is available
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
