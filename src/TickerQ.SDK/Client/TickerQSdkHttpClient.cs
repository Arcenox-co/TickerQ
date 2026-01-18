using System.Text;
using System.Text.Json;

namespace TickerQ.SDK.Client;

public class TickerQSdkHttpClient
{
    private readonly HttpClient _httpClient;
    private readonly TickerSdkOptions _options;
    private readonly JsonSerializerOptions _serializerOptions;
    
    public TickerQSdkHttpClient(TickerSdkOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));

        if (_options.ApiUri == null)
            throw new InvalidOperationException("TickerQ SDK options must be configured with an API URI.");

        if(_options.CallbackUri == null)
            throw new InvalidOperationException("TickerQ SDK options must be configured with an Callback URI.");
        
        _httpClient = new HttpClient
        {
            BaseAddress = _options.ApiUri
        };
        
        _httpClient.DefaultRequestHeaders.Add("X-Api-Key", _options.ApiKey);
        _httpClient.DefaultRequestHeaders.Add("X-Api-Secret", _options.ApiSecret);
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

        var requestMessage = new HttpRequestMessage(HttpMethod.Get, BuildUri(path));
        ApplyAuthentication(requestMessage);

        using var response = await _httpClient.SendAsync(requestMessage, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        if (response.Content == null)
            return null;

        return await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<TResponse?> SendAsync<TRequest, TResponse>(HttpMethod method, string path, TRequest? request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Path must be provided.", nameof(path));

        var requestMessage = new HttpRequestMessage(method, BuildUri(path));
        ApplyAuthentication(requestMessage);

        if (request is not null && method != HttpMethod.Get && method != HttpMethod.Delete)
        {
            var json = JsonSerializer.Serialize(request, _serializerOptions);
            requestMessage.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        using var response = await _httpClient.SendAsync(requestMessage, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        if (typeof(TResponse) == typeof(object) || typeof(TResponse) == typeof(void))
            return default;

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        if (stream.Length == 0)
            return default;

        return await JsonSerializer.DeserializeAsync<TResponse>(stream, _serializerOptions, cancellationToken).ConfigureAwait(false);
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
        if (!string.IsNullOrEmpty(_options.ApiKey))
            request.Headers.TryAddWithoutValidation("X-Api-Key", _options.ApiKey);

        if (!string.IsNullOrEmpty(_options.ApiSecret))
            request.Headers.TryAddWithoutValidation("X-Api-Secret", _options.ApiSecret);
    }
}
