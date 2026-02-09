using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using TickerQ.Utilities;

namespace TickerQ.RemoteExecutor;

internal static class RemoteExecutionDelegateFactory
{
    public static TickerFunctionDelegate Create(
        string callbackUrl,
        Func<IServiceProvider, string?> secretProvider,
        bool allowEmptySecret)
    {
        if (string.IsNullOrWhiteSpace(callbackUrl))
            throw new ArgumentException("Callback URL is required.", nameof(callbackUrl));

        return async (ct, serviceProvider, context) =>
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

            var json = JsonSerializer.Serialize(payload);
            var bodyBytes = Encoding.UTF8.GetBytes(json);

            var uri = new Uri($"{callbackUrl}/execute");
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
            var secret = secretProvider(serviceProvider);
            var signature = ComputeSignature(secret, HttpMethod.Post.Method, uri.PathAndQuery, timestamp, bodyBytes, allowEmptySecret);

            using var request = new HttpRequestMessage(HttpMethod.Post, uri);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
            request.Headers.Add("X-TickerQ-Signature", signature);
            request.Headers.Add("X-Timestamp", timestamp);

            using var response = await httpClient.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();
        };
    }

    private static string ComputeSignature(
        string? secret,
        string method,
        string pathAndQuery,
        string timestamp,
        byte[] bodyBytes,
        bool allowEmptySecret)
    {
        if (allowEmptySecret && string.IsNullOrWhiteSpace(secret))
            return string.Empty;

        var safeSecret = secret ?? string.Empty;
        var header = $"{method}\n{pathAndQuery}\n{timestamp}\n";
        var headerBytes = Encoding.UTF8.GetBytes(header);
        var payload = new byte[headerBytes.Length + bodyBytes.Length];
        Buffer.BlockCopy(headerBytes, 0, payload, 0, headerBytes.Length);
        Buffer.BlockCopy(bodyBytes, 0, payload, headerBytes.Length, bodyBytes.Length);

        var secretKey = Encoding.UTF8.GetBytes(safeSecret);
        var signatureBytes = HMACSHA256.HashData(secretKey, payload);
        return Convert.ToBase64String(signatureBytes);
    }
}
