using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Models;
using TickerQ.Utilities.Notifications;

namespace TickerQ.Notifications.Infrastructure;

internal sealed class NotificationEngine : BackgroundService, ITickerQFailureNotifier, ITickerNotificationAdminService
{
    private readonly NotificationOptions _options;
    private readonly NotificationConfigurationService _configuration;
    private readonly NotificationTransport _transport;
    private readonly ILogger<NotificationEngine> _logger;
    private readonly Channel<NotificationMessage> _queue;
    private readonly ConcurrentQueue<NotificationDeliveryStatus> _recent = new();
    private readonly Dictionary<(Guid, string, string), DateTimeOffset> _cooldowns = [];
    private long _dropped, _suppressed;
    private readonly SemaphoreSlim _testGate = new(1, 1);

    public NotificationEngine(NotificationOptions options, NotificationConfigurationService configuration,
        NotificationTransport transport, ILogger<NotificationEngine> logger)
    {
        _options = options; _configuration = configuration; _transport = transport; _logger = logger;
        _queue = Channel.CreateBounded<NotificationMessage>(new BoundedChannelOptions(options.QueueCapacity)
        { SingleReader = true, SingleWriter = false, FullMode = BoundedChannelFullMode.Wait });
    }

    public void Notify(TickerFailureEvent e)
    {
        string reason;
        try { reason = _options.ReasonSanitizer is { } sanitize ? sanitize(e.Reason ?? "") ?? "" : e.Reason ?? ""; }
        catch { reason = "Failure reason unavailable: configured redaction failed."; }
        var copy = new TickerFailureEvent
        {
            Kind = Limit(e.Kind, 80), TickerId = e.TickerId, Function = Limit(e.Function, 256),
            TickerType = Limit(e.TickerType, 80), Reason = Limit(reason.Replace('\r', ' ').Replace('\n', ' '), 512),
            RetryCount = e.RetryCount, Retries = e.Retries, OccurredAtUtc = e.OccurredAtUtc, Node = Limit(e.Node, 256)
        };
        if (!_queue.Writer.TryWrite(new(Guid.NewGuid(), _options.ApplicationName, _options.EnvironmentName, copy, _options.DashboardUrl ?? "")))
        {
            var dropped = Interlocked.Increment(ref _dropped);
            if (dropped == 1 || (dropped & (dropped - 1)) == 0) _logger.LogWarning("TickerQ notification queue dropped {Count} events", dropped);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var message in _queue.Reader.ReadAllAsync(stoppingToken).ConfigureAwait(false))
            {
                try { await DispatchAsync(message, stoppingToken).ConfigureAwait(false); }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
                catch (Exception ex)
                {
                    // Never log payloads, URLs, custom header values, or exception messages containing them.
                    _logger.LogWarning("TickerQ notification dispatch failed ({ErrorType})", ex.GetType().Name);
                    Record(new() { AtUtc = DateTimeOffset.UtcNow, Outcome = "Failed", Message = "Could not load settings or format notification. Check the notification store and encryption keys." });
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }

    internal async Task DispatchAsync(NotificationMessage message, CancellationToken ct)
    {
        // Read on dispatch so edits and disabled destinations apply across replicas without restart.
        var config = await _configuration.LoadAsync(ct).ConfigureAwait(false);
        var matching = config.Rules.Where(r => NotificationConfigurationService.Matches(r, message.Event)).ToArray();
        var deliveries = new List<NotificationDestination>();
        foreach (var destination in config.Destinations.Where(d => d.Enabled))
        {
            var rules = matching.Where(r => r.DestinationIds.Contains(destination.Id)).ToArray();
            if (rules.Length == 0) continue;
            // Overlapping rules produce one delivery; the least restrictive matching cooldown wins.
            var key = (destination.Id, message.Event.Kind, message.Event.Function);
            var now = DateTimeOffset.UtcNow;
            if (_cooldowns.TryGetValue(key, out var previous) && now - previous < TimeSpan.FromSeconds(rules.Min(r => r.CooldownSeconds)))
            { Interlocked.Increment(ref _suppressed); continue; }
            if (_cooldowns.Count >= 10000)
                foreach (var old in _cooldowns.OrderBy(x => x.Value).Take(1000).Select(x => x.Key).ToArray()) _cooldowns.Remove(old);
            _cooldowns[key] = now;
            deliveries.Add(destination);
        }
        await Parallel.ForEachAsync(deliveries, new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = ct }, async (destination, token) =>
        {
            try
            {
                var payload = _configuration.Formatters[destination.Provider].Format(destination, message).ToJsonString();
                Record(await _transport.SendAsync(destination, payload, token).ConfigureAwait(false));
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch { Record(new() { DestinationId = destination.Id, AtUtc = DateTimeOffset.UtcNow, Outcome = "Failed", Message = "Could not format or send this destination's notification." }); }
        }).ConfigureAwait(false);
    }

    public async Task<NotificationSnapshot> GetAsync(CancellationToken cancellationToken = default) => new()
    {
        Available = true, Persistent = _configuration.Persistent,
        Configuration = NotificationConfigurationService.Redact(await _configuration.LoadAsync(cancellationToken).ConfigureAwait(false)),
        Providers = _configuration.Formatters.Keys.Order().ToList(), TemplateTokens = PayloadTemplates.Tokens.ToList(),
        RecentDeliveries = _recent.Reverse().Take(50).ToList(), Dropped = Interlocked.Read(ref _dropped), Suppressed = Interlocked.Read(ref _suppressed)
    };

    public Task SaveAsync(NotificationConfiguration configuration, CancellationToken cancellationToken = default)
        => _configuration.SaveAsync(configuration, cancellationToken);

    public async Task<NotificationDeliveryStatus> TestAsync(Guid destinationId, CancellationToken cancellationToken = default)
    {
        if (!await _testGate.WaitAsync(0, cancellationToken).ConfigureAwait(false)) throw new ArgumentException("A test notification is already in progress on this node.");
        try
        {
            var config = await _configuration.LoadAsync(cancellationToken).ConfigureAwait(false);
            var destination = config.Destinations.SingleOrDefault(d => d.Id == destinationId) ?? throw new ArgumentException("Save the destination before testing it.");
            var payload = _configuration.Formatters[destination.Provider].Format(destination, Sample(_options)).ToJsonString();
            var result = await _transport.SendAsync(destination, payload, cancellationToken).ConfigureAwait(false);
            Record(result); return result;
        }
        finally { _testGate.Release(); }
    }

    public NotificationPreviewResponse Preview(NotificationPreviewRequest request)
    {
        if (request.Provider == null || !_configuration.Formatters.TryGetValue(request.Provider, out var formatter)) throw new ArgumentException("Unknown provider.");
        return new() { Payload = formatter.Format(new() { Provider = request.Provider, PayloadTemplate = request.PayloadTemplate }, Sample(_options)).ToJsonString() };
    }

    internal static NotificationMessage Sample(NotificationOptions options) => new(Guid.Empty, options.ApplicationName, options.EnvironmentName,
        new TickerFailureEvent { Kind = "failed", Function = "Example.ProcessOrders", Reason = "Test notification — no job has failed.", TickerId = Guid.Empty,
            TickerType = "TimeTicker", Node = "example-node", RetryCount = 3, Retries = 3, OccurredAtUtc = DateTime.UtcNow }, options.DashboardUrl ?? "");

    private void Record(NotificationDeliveryStatus status)
    {
        _recent.Enqueue(status);
        while (_recent.Count > 50) _recent.TryDequeue(out _);
    }
    private static string Limit(string? s, int length) => s == null ? "" : s.Length <= length ? s : s[..length];
}
