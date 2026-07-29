using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Models;

namespace TickerQ.BackgroundServices;

/// <summary>
/// Built-in webhook failure notifier: Notify() enqueues onto a bounded channel
/// (never blocks the execution pipeline); this hosted pump POSTs each event as
/// JSON to the configured URL with a couple of retries. Registered only when
/// <c>options.NotifyFailuresViaWebhook(url)</c> was called.
/// </summary>
internal sealed class WebhookFailureNotifier : ITickerQFailureNotifier
{
    internal const int Capacity = 1000;

    internal readonly FailureWebhookOptions Options;
    private readonly ILogger<WebhookFailureNotifier> _logger;
    private readonly Channel<TickerFailureEvent> _channel;
    private long _droppedCount;

    public WebhookFailureNotifier(
        FailureWebhookOptions options,
        ILogger<WebhookFailureNotifier> logger)
    {
        Options = options;
        _logger = logger;
        _channel = Channel.CreateBounded<TickerFailureEvent>(
            new BoundedChannelOptions(Capacity)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = false,
            },
            _ => RecordDrop());
    }

    internal long DroppedCount => Interlocked.Read(ref _droppedCount);
    internal ChannelReader<TickerFailureEvent> Reader => _channel.Reader;

    public void Notify(TickerFailureEvent failureEvent)
    {
        var sanitize = Options.ReasonSanitizer ?? FailureWebhookOptions.DefaultReasonSanitizer;
        var queued = new TickerFailureEvent
        {
            Kind = failureEvent.Kind,
            TickerId = failureEvent.TickerId,
            Function = failureEvent.Function,
            TickerType = failureEvent.TickerType,
            Reason = sanitize(failureEvent.Reason),
            RetryCount = failureEvent.RetryCount,
            Retries = failureEvent.Retries,
            OccurredAtUtc = failureEvent.OccurredAtUtc,
            Node = failureEvent.Node,
        };

        _channel.Writer.TryWrite(queued);
    }

    private void RecordDrop()
    {
        var count = Interlocked.Increment(ref _droppedCount);
        if (count == 1 || (count & (count - 1)) == 0)
            _logger.LogWarning(
                "TickerQ failure webhook queue dropped {DroppedCount} events due to capacity {Capacity}",
                count, Capacity);
    }
}

internal sealed class TickerQWebhookNotifierBackgroundService : BackgroundService
{
    private static readonly TimeSpan[] SendRetryDelays = { TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5) };

    private readonly WebhookFailureNotifier _notifier;
    private readonly ILogger<TickerQWebhookNotifierBackgroundService> _logger;
    private readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(10) };

    public TickerQWebhookNotifierBackgroundService(
        WebhookFailureNotifier notifier,
        ILogger<TickerQWebhookNotifierBackgroundService> logger)
    {
        _notifier = notifier;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var evt in _notifier.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await SendWithRetriesAsync(evt, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failure webhook delivery gave up for {Function} ({Kind})", evt.Function, evt.Kind);
            }
        }
    }

    private async Task SendWithRetriesAsync(TickerFailureEvent evt, CancellationToken ct)
    {
        var payload = JsonSerializer.Serialize(evt, TickerQWebhookJsonContext.Default.TickerFailureEvent);

        for (var attempt = 0; ; attempt++)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, _notifier.Options.Url)
                {
                    Content = new StringContent(payload, Encoding.UTF8, "application/json"),
                };

                if (_notifier.Options.Headers != null)
                    foreach (var header in _notifier.Options.Headers)
                        request.Headers.TryAddWithoutValidation(header.Key, header.Value);

                using var response = await _httpClient.SendAsync(request, ct);
                response.EnsureSuccessStatusCode();
                return;
            }
            catch (Exception ex) when (ex is not OperationCanceledException && attempt < SendRetryDelays.Length)
            {
                _logger.LogDebug(ex, "Failure webhook attempt {Attempt} failed; retrying", attempt + 1);
                await Task.Delay(SendRetryDelays[attempt], ct);
            }
        }
    }

    public override void Dispose()
    {
        _httpClient.Dispose();
        base.Dispose();
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(TickerFailureEvent))]
internal partial class TickerQWebhookJsonContext : JsonSerializerContext
{
}
