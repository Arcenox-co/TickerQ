using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Globalization;
using Microsoft.Extensions.Logging;

namespace TickerQ.SDK.Client;

public class TickerQSdkHttpClient
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly TickerSdkOptions _options;
    private readonly JsonSerializerOptions _serializerOptions;
    private readonly ILogger<TickerQSdkHttpClient>? _logger;

    /// <summary>
    /// HTTP client name for Hub requests (function registration).
    /// </summary>
    internal const string HubClientName = "tickerq-sdk-hub";

    /// <summary>
    /// HTTP client name for Scheduler requests (job operations).
    /// </summary>
    internal const string SchedulerClientName = "tickerq-sdk-scheduler";

    public TickerQSdkHttpClient(
        IHttpClientFactory httpClientFactory,
        TickerSdkOptions options,
        ILogger<TickerQSdkHttpClient>? logger = null)
    {
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger;

        _serializerOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
    }

    public Task<TResponse?> GetAsync<TResponse>(string path, CancellationToken cancellationToken = default)
    {
        return SendAsync<object, TResponse>(HttpMethod.Get, path, null, cancellationToken);
    }

    public Task<TResponse?> PostAsync<TRequest, TResponse>(string path, TRequest request, CancellationToken cancellationToken = default)
    {
        return SendAsync<TRequest, TResponse>(HttpMethod.Post, path, request, cancellationToken);
    }

    public Task<TResponse?> PutAsync<TRequest, TResponse>(string path, TRequest request, CancellationToken cancellationToken = default)
    {
        return SendAsync<TRequest, TResponse>(HttpMethod.Put, path, request, cancellationToken);
    }

    public Task DeleteAsync(string path, CancellationToken cancellationToken = default)
    {
        return SendAsync<object, object?>(HttpMethod.Delete, path, null, cancellationToken);
    }

    public async Task<byte[]?> GetBytesAsync(string path, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Path must be provided.", nameof(path));

        var uri = BuildUri(path);
        var requestMessage = new HttpRequestMessage(HttpMethod.Get, uri);
        ApplyAuthentication(requestMessage);
        ApplySignature(requestMessage, string.Empty);

        var httpClient = GetHttpClient(uri);

        try
        {
            _logger?.LogDebug("TickerQ SDK sending {Method} {Uri}", requestMessage.Method, requestMessage.RequestUri);

            using var response = await httpClient.SendAsync(requestMessage, cancellationToken).ConfigureAwait(false);
            _logger?.LogDebug("TickerQ SDK received {StatusCode} for {Method} {Uri}",
                (int)response.StatusCode, requestMessage.Method, requestMessage.RequestUri);

            response.EnsureSuccessStatusCode();

            if (response.Content == null)
                return null;

            return await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            _logger?.LogError(ex, "TickerQ SDK HTTP request failed {Method} {Uri} - Status: {StatusCode}",
                requestMessage.Method, requestMessage.RequestUri, ex.StatusCode);
            throw;
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger?.LogError(ex, "TickerQ SDK request timed out {Method} {Uri}", requestMessage.Method, requestMessage.RequestUri);
            throw;
        }
    }

    private async Task<TResponse?> SendAsync<TRequest, TResponse>(HttpMethod method, string path, TRequest? request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Path must be provided.", nameof(path));

        var uri = BuildUri(path);
        var requestMessage = new HttpRequestMessage(method, uri);
        ApplyAuthentication(requestMessage);

        var body = string.Empty;
        if (request is not null && method != HttpMethod.Get && method != HttpMethod.Delete)
        {
            body = JsonSerializer.Serialize(request, _serializerOptions);
            requestMessage.Content = new StringContent(body, Encoding.UTF8, "application/json");
        }
        ApplySignature(requestMessage, body);

        var httpClient = GetHttpClient(uri);

        try
        {
            _logger?.LogDebug("TickerQ SDK sending {Method} {Uri}", requestMessage.Method, requestMessage.RequestUri);

            using var response = await httpClient.SendAsync(requestMessage, cancellationToken).ConfigureAwait(false);
            _logger?.LogDebug("TickerQ SDK received {StatusCode} for {Method} {Uri}",
                (int)response.StatusCode, requestMessage.Method, requestMessage.RequestUri);

            response.EnsureSuccessStatusCode();

            if (typeof(TResponse) == typeof(object) || typeof(TResponse) == typeof(void))
                return default;

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            if (stream.Length == 0)
                return default;

            return await JsonSerializer.DeserializeAsync<TResponse>(stream, _serializerOptions, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            _logger?.LogError(ex, "TickerQ SDK HTTP request failed {Method} {Uri} - Status: {StatusCode}",
                requestMessage.Method, requestMessage.RequestUri, ex.StatusCode);
            throw;
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger?.LogError(ex, "TickerQ SDK request timed out {Method} {Uri}", requestMessage.Method, requestMessage.RequestUri);
            throw;
        }
    }

    private HttpClient GetHttpClient(Uri uri)
    {
        var clientName = IsHubRequest(uri) ? HubClientName : SchedulerClientName;
        return _httpClientFactory.CreateClient(clientName);
    }

    private Uri BuildUri(string path)
    {
        if (_options.ApiUri == null)
            throw new InvalidOperationException("TickerQ SDK options must be configured with an API URI.");

        if (Uri.TryCreate(path, UriKind.Absolute, out var absolute))
            return absolute;

        return new Uri(_options.ApiUri, path);
    }

    private void ApplyAuthentication(HttpRequestMessage request)
    {
        if (!IsHubRequest(request.RequestUri))
            return;

        if (!string.IsNullOrEmpty(_options.ApiKey))
            request.Headers.TryAddWithoutValidation("X-Api-Key", _options.ApiKey);

        if (!string.IsNullOrEmpty(_options.ApiSecret))
            request.Headers.TryAddWithoutValidation("X-Api-Secret", _options.ApiSecret);
    }

    private void ApplySignature(HttpRequestMessage request, string body)
    {
        if (IsHubRequest(request.RequestUri))
            return;

        if (string.IsNullOrWhiteSpace(_options.WebhookSignature))
            return;

        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        request.Headers.TryAddWithoutValidation("X-Timestamp", timestamp);

        var key = Encoding.UTF8.GetBytes(_options.WebhookSignature);
        var pathAndQuery = request.RequestUri?.PathAndQuery ?? "/";
        var header = $"{request.Method.Method}\n{pathAndQuery}\n{timestamp}\n";
        var headerBytes = Encoding.UTF8.GetBytes(header);
        var bodyBytes = Encoding.UTF8.GetBytes(body ?? string.Empty);
        var payload = new byte[headerBytes.Length + bodyBytes.Length];
        Buffer.BlockCopy(headerBytes, 0, payload, 0, headerBytes.Length);
        Buffer.BlockCopy(bodyBytes, 0, payload, headerBytes.Length, bodyBytes.Length);

        var signatureBytes = HMACSHA256.HashData(key, payload);
        var signature = Convert.ToBase64String(signatureBytes);

        request.Headers.TryAddWithoutValidation("X-TickerQ-Signature", signature);
    }

    private static bool IsHubRequest(Uri? requestUri)
    {
        if (requestUri is null)
            return false;

        if (!requestUri.Host.Equals(
                TickerQSdkConstants.HubHostname,
                StringComparison.OrdinalIgnoreCase))
            return false;

        return (requestUri.Scheme == Uri.UriSchemeHttp  && requestUri.Port == 80)
               || (requestUri.Scheme == Uri.UriSchemeHttps && requestUri.Port == 443);
    }
}
