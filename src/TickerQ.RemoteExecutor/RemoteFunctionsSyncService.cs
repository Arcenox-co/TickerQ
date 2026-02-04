using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Globalization;
using System.Security.Cryptography;
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
        _internalTickerManager = _serviceProvider.GetService<IInternalTickerManager>();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await SyncOnceAsync(stoppingToken);
    }

    public async Task SyncOnceAsync(CancellationToken stoppingToken)
    {
        // Run once on startup or on demand
        try
        {
            if (string.IsNullOrWhiteSpace(_options.HubEndpointUrl))
            {
                _logger?.LogWarning("FunctionsEndpointUrl is not configured. Skipping remote functions sync.");
                return;
            }

            _logger?.LogInformation("Starting remote functions sync from {EndpointUrl}", _options.HubEndpointUrl);

            var httpClient = _httpClientFactory.CreateClient("tickerq-hub");
            using var httpResponse =
                await httpClient.GetAsync($"{_options.HubEndpointUrl}api/apps/sync/nodes-functions",
                    stoppingToken);

            if (!httpResponse.IsSuccessStatusCode)
            {
                var errorContent = await httpResponse.Content.ReadAsStringAsync(stoppingToken);
                _logger?.LogError(
                    "Failed to fetch functions from {EndpointUrl}. Status: {StatusCode}, Response: {Response}",
                    _options.HubEndpointUrl,
                    httpResponse.StatusCode,
                    errorContent);
                return;
            }

            var responseContent = await httpResponse.Content.ReadAsStringAsync(stoppingToken);
            if (string.IsNullOrWhiteSpace(responseContent))
            {
                _logger?.LogWarning("Received empty response from functions endpoint");
                return;
            }

            var contentType = httpResponse.Content.Headers.ContentType?.MediaType ?? string.Empty;
            if (!contentType.Contains("json", StringComparison.OrdinalIgnoreCase))
            {
                _logger?.LogWarning(
                    "Received non-JSON content type from {EndpointUrl}. Content-Type: {ContentType}. Attempting to parse anyway.",
                    _options.HubEndpointUrl,
                    contentType);
            }

            RegisteredFunctionsResponse? response;
            try
            {
                response = JsonSerializer.Deserialize<RegisteredFunctionsResponse>(
                    responseContent,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch (JsonException ex)
            {
                _logger?.LogError(ex, "Failed to deserialize functions response");
                return;
            }

            if (response == null)
            {
                _logger?.LogWarning("Received null response from functions endpoint");
                return;
            }
            _options.WebHookSignature = response.WebhookSignature;
            await RegisterFunctionsFromResponse(response, stoppingToken);
            
            _logger?.LogInformation("Remote functions sync completed successfully");
        }
        catch (HttpRequestException ex)
        {
            // Transient network failure - log and continue without functions
            _logger?.LogError(ex, "Network error during remote functions sync. Status: {StatusCode}. The service will continue without remote functions.",
                ex.StatusCode);
        }
        catch (TaskCanceledException ex) when (!stoppingToken.IsCancellationRequested)
        {
            // Request timeout - log and continue
            _logger?.LogError(ex, "Timeout during remote functions sync. The service will continue without remote functions.");
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Application shutting down - normal cancellation
            _logger?.LogInformation("Remote functions sync cancelled due to application shutdown.");
        }
        // Note: Other exceptions (ArgumentException, NullReferenceException, etc.) are NOT caught
        // and will propagate - this is intentional to fail fast on programming errors
    }

    private async Task RegisterFunctionsFromResponse(RegisteredFunctionsResponse response, CancellationToken cancellationToken)
    {
        if (response.Nodes.Count == 0)
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
                    if (functionDict.Remove(function.FunctionName))
                        _logger?.LogDebug("Removed inactive function {FunctionName}", function.FunctionName);
                    else
                        _logger?.LogDebug("Skipping inactive function {FunctionName}", function.FunctionName);
                    continue;
                }

                // Capture callbackUrl in local variable to avoid closure issues
                var callbackUrl = node.CallbackUrl.TrimEnd('/');
                
                // Create function delegate similar to MapFunctionRegistration
                var functionDelegate = new TickerFunctionDelegate(async (ct, serviceProvider, context) =>
                {
                    var httpClientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();
                    var httpClient = httpClientFactory.CreateClient("tickerq-callback");

                    var payload = new
                    {
                        context.Id,
                        context.FunctionName,
                        context.Type,
                        context.RetryCount,
                        context.ScheduledFor
                    };

                    // 1. Serialize ONCE
                    var json = JsonSerializer.Serialize(payload);
                    var bodyBytes = Encoding.UTF8.GetBytes(json);

                    // 2. Build request + signature
                    var uri = new Uri($"{callbackUrl}/execute");
                    var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
                    var signature = ComputeSignature(
                        response.WebhookSignature,
                        HttpMethod.Post.Method,
                        uri.PathAndQuery,
                        timestamp,
                        bodyBytes);

                    // 3. Build request manually
                    using var request = new HttpRequestMessage(HttpMethod.Post, uri);
                    request.Content = new StringContent(json, Encoding.UTF8, "application/json");

                    request.Headers.Add("X-TickerQ-Signature", signature);
                    request.Headers.Add("X-Timestamp", timestamp);

                    using var responseCallback = await httpClient.SendAsync(request, ct);
                    responseCallback.EnsureSuccessStatusCode();
                });

                // Convert int priority to TickerTaskPriority enum
                var priority = (TickerTaskPriority)function.TaskPriority;
                
                // Use cronExpression if available
                var cronExpression = function.CronExpression ?? string.Empty;
                
                functionDict[function.FunctionName] = (cronExpression, priority, functionDelegate);
                
                if (!string.IsNullOrWhiteSpace(cronExpression))
                {
                    cronPairs.Add((function.FunctionName, cronExpression));
                }

                _logger?.LogDebug("Registered function {FunctionName} from node {NodeName}", 
                    function.FunctionName, node.NodeName);
            }
        }

        if (functionDict.Count > 0)
            TickerFunctionProvider.RegisterFunctions(functionDict);

        TickerFunctionProvider.Build();
        _logger?.LogInformation("Registered {Count} functions", functionDict.Count);

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

    private static string ComputeSignature(
        string secret,
        string method,
        string pathAndQuery,
        string timestamp,
        byte[] bodyBytes)
    {
        var header = $"{method}\n{pathAndQuery}\n{timestamp}\n";
        var headerBytes = Encoding.UTF8.GetBytes(header);
        var payload = new byte[headerBytes.Length + bodyBytes.Length];
        Buffer.BlockCopy(headerBytes, 0, payload, 0, headerBytes.Length);
        Buffer.BlockCopy(bodyBytes, 0, payload, headerBytes.Length, bodyBytes.Length);

        var secretKey = Encoding.UTF8.GetBytes(secret);
        var signatureBytes = HMACSHA256.HashData(secretKey, payload);
        return Convert.ToBase64String(signatureBytes);
    }
}
