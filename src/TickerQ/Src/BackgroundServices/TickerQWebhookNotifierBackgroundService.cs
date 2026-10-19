using System;
using System.Net;
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

    internal void Complete() => _channel.Writer.TryComplete();

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

        if (!_channel.Writer.TryWrite(queued)) RecordDrop();
    }

    private void RecordDrop()
    {
        var count = Interlocked.Increment(ref _droppedCount);
        if (count == 1 || (count & (count - 1)) == 0)
            _logger.LogWarning(
                "TickerQ failure webhook queue dropped {DroppedCount} events (queue full or closed; capacity {Capacity})",
                count, Capacity);
    }
}

internal sealed class TickerQWebhookNotifierBackgroundService : BackgroundService
{
    private static readonly TimeSpan[] SendRetryDelays = { TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5) };

    private readonly WebhookFailureNotifier _notifier;
    private readonly ILogger<TickerQWebhookNotifierBackgroundService> _logger;
    private readonly HttpClient _httpClient;

    public TickerQWebhookNotifierBackgroundService(
        WebhookFailureNotifier notifier,
        ILogger<TickerQWebhookNotifierBackgroundService> logger)
        : this(notifier, logger, new HttpClient { Timeout = TimeSpan.FromSeconds(10) }) { }

    internal TickerQWebhookNotifierBackgroundService(WebhookFailureNotifier notifier,
        ILogger<TickerQWebhookNotifierBackgroundService> logger, HttpClient httpClient)
    {
        _httpClient = httpClient;
        _notifier = notifier;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
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
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        // DI registration keeps this pump alive until scheduler/recovery producers stop.
        // Complete the writer first, allowing ReadAllAsync to finish the existing queue.
        _notifier.Complete();
        using var drain = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        drain.CancelAfter(_notifier.Options.ShutdownDrainTimeout);
        if (ExecuteTask != null)
        {
            try { await ExecuteTask.WaitAsync(drain.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) when (drain.IsCancellationRequested)
            {
                _logger.LogWarning("Failure webhook shutdown drain timed out; undelivered notifications may be lost");
            }
        }
        await base.StopAsync(drain.Token).ConfigureAwait(false);
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
            catch (Exception ex) when (attempt < SendRetryDelays.Length && IsTransient(ex, ct))
            {
                _logger.LogDebug(ex, "Failure webhook attempt {Attempt} failed; retrying", attempt + 1);
                await Task.Delay(SendRetryDelays[attempt], ct);
            }
        }
    }

    private static bool IsTransient(Exception exception, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested) return false;
        // HttpClient timeouts surface as OperationCanceledException even when the caller is active.
        if (exception is OperationCanceledException) return true;
        return exception is HttpRequestException request &&
            (request.StatusCode == null || request.StatusCode == HttpStatusCode.RequestTimeout ||
             request.StatusCode == HttpStatusCode.TooManyRequests || (int)request.StatusCode >= 500);
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
