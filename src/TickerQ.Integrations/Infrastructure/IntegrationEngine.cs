using System.Collections.Concurrent;
using TickerQ.Integrations.Monitoring;
using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Models;
using TickerQ.Utilities.Integrations;

namespace TickerQ.Integrations.Infrastructure;

internal sealed class IntegrationEngine : BackgroundService, ITickerQFailureNotifier, ITickerIntegrationAdminService
{
    private readonly IntegrationOptions _options;
    private readonly MonitoringStateStore? _monitoring;
    private readonly IntegrationConfigurationService _configuration;
    private readonly IntegrationTransport _transport;
    private readonly ILogger<IntegrationEngine> _logger;
    private readonly Channel<IntegrationMessage> _queue;
    private readonly ConcurrentQueue<IntegrationDeliveryStatus> _recent = new();
    private readonly Dictionary<(Guid, string, string), DateTimeOffset> _cooldowns = [];
    private long _dropped, _suppressed;
    private readonly SemaphoreSlim _testGate = new(1, 1);

    public IntegrationEngine(IntegrationOptions options, IntegrationConfigurationService configuration,
        IntegrationTransport transport, ILogger<IntegrationEngine> logger, MonitoringStateStore? monitoring = null)
    {
        _monitoring = monitoring;
        _options = options; _configuration = configuration; _transport = transport; _logger = logger;
        _queue = Channel.CreateBounded<IntegrationMessage>(new BoundedChannelOptions(options.QueueCapacity)
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
                    Record(new() { AtUtc = DateTimeOffset.UtcNow, Outcome = "Failed", Message = "Could not load settings or format notification. Check the integration settings store and encryption keys." });
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        // Stop accepting new events, then let ExecuteAsync finish the queue it already holds.
        // Its stopping token is still live here, so in-flight sends are not cancelled until the
        // drain window or the host's shutdown deadline runs out.
        _queue.Writer.TryComplete();
        var execute = ExecuteTask;
        if (execute != null && !execute.IsCompleted && _options.ShutdownDrainTimeout > TimeSpan.Zero)
        {
            using var drain = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            drain.CancelAfter(_options.ShutdownDrainTimeout);
            try { await execute.WaitAsync(drain.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) when (drain.IsCancellationRequested)
            {
                _logger.LogWarning(
                    "TickerQ integration shutdown drain timed out; {Count} queued notification(s) were not delivered",
                    _queue.Reader.Count);
            }
        }
        await base.StopAsync(cancellationToken).ConfigureAwait(false);
    }

    internal async Task DispatchAsync(IntegrationMessage message, CancellationToken ct)
    {
        // Read on dispatch so edits and disabled destinations apply across replicas without restart.
        var config = await _configuration.LoadAsync(ct).ConfigureAwait(false);
        var matching = config.Rules.Where(r => IntegrationConfigurationService.Matches(r, message.Event)).ToArray();
        var deliveries = new List<IntegrationDestination>();
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

    public async Task<IntegrationSnapshot> GetAsync(CancellationToken cancellationToken = default)
    {
        var config = await _configuration.LoadAsync(cancellationToken).ConfigureAwait(false);
        var monitoring = _monitoring?.Available == true ? (await _monitoring.ReadAsync(cancellationToken).ConfigureAwait(false)).State : null;
        if (monitoring != null)
            foreach (var state in monitoring.Monitors)
                if (state.View.LastCheckedUtc.HasValue && DateTime.UtcNow - state.View.LastCheckedUtc.Value > _options.MonitoringInterval * 2 + TimeSpan.FromMinutes(1))
                {
                    state.View.Status = state.IncidentId != null ? "incident" : "unknown";
                    state.View.Message = "Monitoring checks are delayed; current health has not been confirmed.";
                }
        return new()
        {
            Available = true, Persistent = _configuration.Persistent, MonitoringAvailable = _monitoring?.Available == true,
            MonitoringPendingDeliveries = monitoring == null ? 0 : monitoring.Deliveries.Count
                + monitoring.Monitors.Sum(m => m.PendingTransitions.Sum(t => t.DestinationIds.Count)),
            MonitoringCheckIntervalSeconds = (int)_options.MonitoringInterval.TotalSeconds,
            Configuration = IntegrationConfigurationService.Redact(config),
            Monitors = config.Monitors.Select(r => monitoring?.Monitors.FirstOrDefault(s => s.Id == r.Id && s.Fingerprint == MonitorEvaluation.Fingerprint(r))?.View
                ?? new IntegrationMonitorStatus { MonitorId = r.Id, Status = r.Enabled ? "waiting" : "paused" }).ToList(),
            Providers = _configuration.Formatters.Keys.Order().ToList(), TemplateTokens = PayloadTemplates.Tokens.ToList(),
            RecentDeliveries = _recent.Concat(monitoring?.History ?? []).OrderByDescending(x => x.AtUtc).Take(50).ToList(),
            Dropped = Interlocked.Read(ref _dropped), Suppressed = Interlocked.Read(ref _suppressed)
        };
    }

    internal async Task<IntegrationDeliveryStatus> SendMonitorAsync(IntegrationDestination destination, IntegrationMessage message, CancellationToken ct)
    {
        try
        {
            var payload = _configuration.Formatters[destination.Provider].Format(destination, message).ToJsonString();
            return await _transport.SendAsync(destination, payload, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch { return new() { DestinationId = destination.Id, AtUtc = DateTimeOffset.UtcNow, Outcome = "Failed", Message = "Could not format or send monitoring alert." }; }
    }

    public Task SaveAsync(IntegrationConfiguration configuration, CancellationToken cancellationToken = default)
        => _configuration.SaveAsync(configuration, cancellationToken);

    public async Task<IntegrationDeliveryStatus> TestAsync(Guid destinationId, CancellationToken cancellationToken = default)
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

    public IntegrationPreviewResponse Preview(IntegrationPreviewRequest request)
    {
        if (request.Provider == null || !_configuration.Formatters.TryGetValue(request.Provider, out var formatter)) throw new ArgumentException("Unknown provider.");
        var sample = Sample(_options, request.MessageKind, request.MonitorKind);
        var payload = formatter.Format(new() { Provider = request.Provider, PayloadTemplate = request.PayloadTemplate }, sample).ToJsonString();
        if (System.Text.Encoding.UTF8.GetByteCount(payload) > 24000) throw new ArgumentException("Rendered payload is too large.");
        return new()
        {
            Payload = payload,
            DefaultTemplate = request.Provider is "slack" or "teams" or "webhook"
                ? BuiltInPayloads.Create(request.Provider, sample, template: true).ToJsonString() : null
        };
    }

    internal static IntegrationMessage Sample(IntegrationOptions options, string category = "job_alert", string monitorKind = "long_running")
    {
        if (!MessageContent.Categories.Contains(category)) throw new ArgumentException("Choose job_alert, monitoring_alert, or recovery.");
        if (monitorKind is not ("deadline" or "long_running" or "consecutive_failures")) throw new ArgumentException("Choose a supported monitoring sample.");
        var now = DateTime.UtcNow;
        var monitoring = category != "job_alert";
        var recovery = category == "recovery";
        var message = new IntegrationMessage(Guid.Parse("99999999-9999-4999-8999-999999999999"), options.ApplicationName, options.EnvironmentName,
            new TickerFailureEvent
            {
                Kind = !monitoring ? "failed" : recovery ? "monitoring_recovered" : "monitoring_" + monitorKind,
                Function = "Invoices.Generate", Reason = !monitoring ? "Sample: invoice generation failed after 3 retries. No job has failed."
                    : recovery ? "Sample: a successful execution has been recorded and the monitored condition is clear."
                    : monitorKind switch
                    {
                        "deadline" => "Sample: no successful execution was recorded by the daily deadline, including the grace period.",
                        "consecutive_failures" => "Sample: at least 3 consecutive final failures were observed in the last 24 hours.",
                        _ => "Sample: the execution is still InProgress, 95 seconds after it was first observed."
                    },
                TickerId = monitoring && monitorKind == "deadline" || recovery ? Guid.Empty : Guid.Parse("11111111-1111-4111-8111-111111111111"),
                TickerType = monitoring ? "FunctionMonitor" : "TimeTicker", Node = monitoring ? "monitoring" : "worker-01",
                RetryCount = monitoring ? 0 : 3, Retries = monitoring ? 0 : 3, OccurredAtUtc = now
            }, options.DashboardUrl ?? "");
        return !monitoring ? message : message with
        {
            MonitorId = Guid.Parse("22222222-2222-4222-8222-222222222222"), MonitorName = "Daily invoices",
            IncidentId = Guid.Parse("33333333-3333-4333-8333-333333333333"), MonitorKind = monitorKind,
            ThresholdSeconds = monitorKind == "long_running" ? 30 : null,
            ObservedDurationSeconds = monitorKind == "long_running" && !recovery ? 95 : null,
            FailureThreshold = monitorKind == "consecutive_failures" ? 3 : null,
            FailureCount = monitorKind == "consecutive_failures" ? recovery ? 0 : 3 : null,
            DeadlineUtc = monitorKind == "deadline" ? now.Date.AddHours(8) : null,
            TimeZoneId = monitorKind == "deadline" ? "UTC" : null, GraceMinutes = monitorKind == "deadline" ? 10 : null
        };
    }

    private void Record(IntegrationDeliveryStatus status)
    {
        _recent.Enqueue(status);
        while (_recent.Count > 50) _recent.TryDequeue(out _);
    }
    private static string Limit(string? s, int length) => s == null ? "" : s.Length <= length ? s : s[..length];
}
