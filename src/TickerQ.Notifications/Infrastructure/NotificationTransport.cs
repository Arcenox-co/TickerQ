using System.Net;
using System.Text;
using TickerQ.Utilities.Notifications;

namespace TickerQ.Notifications.Infrastructure;

internal sealed class NotificationTransport(IHttpClientFactory clients, NotificationOptions options)
{
    internal const string ClientName = "TickerQ.Notifications";
    internal async Task<NotificationDeliveryStatus> SendAsync(NotificationDestination destination, string payload, CancellationToken ct)
    {
        var uri = WebhookSecurity.ValidateUrl(destination.Url, options);
        if (Encoding.UTF8.GetByteCount(payload) > 24000) return Result("Failed", "Rendered payload exceeds 24 KB.");
        using var client = clients.CreateClient(ClientName);
        for (var attempt = 0; attempt < 3; attempt++)
        {
            TimeSpan delay = TimeSpan.FromSeconds(attempt == 0 ? 1 : 5);
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(options.RequestTimeout);
                using var request = new HttpRequestMessage(HttpMethod.Post, uri) { Content = new StringContent(payload, Encoding.UTF8, "application/json") };
                foreach (var (name, value) in destination.Headers ?? []) request.Headers.TryAddWithoutValidation(name, value);
                using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
                if (response.IsSuccessStatusCode) return Result("Sent", "Webhook accepted the request.");
                var transient = response.StatusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500;
                if (!transient || attempt == 2) return Result("Failed", $"Destination returned HTTP {(int)response.StatusCode}.");
                var retry = response.Headers.RetryAfter;
                var wait = retry?.Delta ?? (retry?.Date - DateTimeOffset.UtcNow);
                // Do not retry earlier than requested. Very long delays return a visible failure instead of blocking the queue.
                if (wait > TimeSpan.FromSeconds(60)) return Result("Failed", "Destination is rate limited; retry later.");
                if (wait > delay) delay = wait.Value;
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                if (attempt == 2) return Result("Failed", "Webhook request timed out.");
            }
            catch (HttpRequestException)
            {
                if (attempt == 2) return Result("Failed", "Webhook connection failed. Check its URL, TLS, DNS, and network access.");
            }
            await Task.Delay(delay, ct).ConfigureAwait(false);
        }
        return Result("Failed", "Webhook delivery failed.");

        NotificationDeliveryStatus Result(string outcome, string message) => new()
        {
            DestinationId = destination.Id, AtUtc = DateTimeOffset.UtcNow, Outcome = outcome, Message = message
        };
    }
}
