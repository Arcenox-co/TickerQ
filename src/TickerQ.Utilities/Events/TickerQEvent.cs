#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace TickerQ.Utilities.Events
{
    /// <summary>
    /// Receives TickerQ events, such as a promise that needs review. Register any number with
    /// <c>services.TryAddEnumerable(ServiceDescriptor.Singleton&lt;ITickerQEventSink, MySink&gt;())</c>.
    /// Each call gets a few seconds before it is abandoned, so forward slow work to a queue.
    /// </summary>
    public interface ITickerQEventSink
    {
        /// <summary>
        /// Takes one event. <paramref name="ct"/> is cancelled when the few seconds run out; an exception is logged
        /// and never reaches the code that raised the event.
        /// </summary>
        ValueTask PublishAsync(TickerQEvent e, CancellationToken ct);
    }

    /// <summary>How much an event needs attention.</summary>
    public enum TickerQEventSeverity
    {
        Info,
        Warning,
        Error,
    }

    /// <summary>
    /// Something TickerQ noticed. <paramref name="Id"/> is stable for the same occurrence, so receivers can drop repeats.
    /// </summary>
    /// <param name="Kind">What happened, for example <c>promise.needs_review</c>.</param>
    /// <param name="Subject">What it happened to, for example the promise's name.</param>
    /// <param name="DashboardPath">Where the dashboard shows it, relative to the dashboard's base path.</param>
    /// <param name="Data">Machine-readable details, for example counts.</param>
    public sealed record TickerQEvent(
        string Id,
        string Kind,
        string Subject,
        string Title,
        string? Detail,
        TickerQEventSeverity Severity,
        string? DashboardPath,
        DateTime OccurredAtUtc,
        IReadOnlyDictionary<string, string> Data);

    /// <summary>Sends an event to every registered sink. Never throws; a sink that fails or is slow is logged and skipped.</summary>
    internal sealed class TickerQEventPublisher(IEnumerable<ITickerQEventSink> sinks, ILogger<TickerQEventPublisher> logger)
    {
        private static readonly TimeSpan SinkTimeout = TimeSpan.FromSeconds(5);
        private readonly ITickerQEventSink[] _sinks = sinks.ToArray();

        public Task PublishAsync(TickerQEvent e) => Task.WhenAll(_sinks.Select(sink => SendAsync(sink, e)));

        private async Task SendAsync(ITickerQEventSink sink, TickerQEvent e)
        {
            using var timeout = new CancellationTokenSource(SinkTimeout);
            try
            {
                await sink.PublishAsync(e, timeout.Token).AsTask().WaitAsync(SinkTimeout).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "TickerQ event sink {Sink} did not take {Kind} event {Id}", sink.GetType().Name, e.Kind, e.Id);
            }
        }
    }
}
