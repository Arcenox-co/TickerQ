using System.Diagnostics.Metrics;

namespace TickerQ.Dashboard.Infrastructure.Metrics;

/// <summary>
/// Default <see cref="ITickerQDashboardMetrics"/> backed by a
/// <see cref="Meter"/> named <c>TickerQ.Dashboard</c>. OpenTelemetry users
/// export it with <c>builder.AddMeter("TickerQ.Dashboard")</c> — no extra
/// wiring in TickerQ needed.
/// </summary>
internal sealed class MeterDashboardMetrics : ITickerQDashboardMetrics
{
    public const string MeterName = "TickerQ.Dashboard";

    private static readonly Meter Meter = new(MeterName);

    private static readonly Counter<long> Requests =
        Meter.CreateCounter<long>("tickerq.dashboard.requests", description: "Dashboard API requests served");

    private static readonly Histogram<double> RequestDuration =
        Meter.CreateHistogram<double>("tickerq.dashboard.request.duration", unit: "ms", description: "Dashboard API request latency");

    private static readonly Counter<long> AuthFailures =
        Meter.CreateCounter<long>("tickerq.dashboard.auth.failures", description: "Requests rejected with 401");

    private static readonly Counter<long> LoginsThrottled =
        Meter.CreateCounter<long>("tickerq.dashboard.auth.throttled", description: "Login attempts rejected by the rate limiter");

    private static readonly UpDownCounter<long> SignalRConnections =
        Meter.CreateUpDownCounter<long>("tickerq.dashboard.signalr.connections", description: "Currently connected SignalR clients");

    public void RequestCompleted(string method, string path, int statusCode, double elapsedMilliseconds)
    {
        var tags = new System.Diagnostics.TagList
        {
            { "http.method", method },
            { "http.route", path },
            { "http.status_code", statusCode },
        };
        Requests.Add(1, tags);
        RequestDuration.Record(elapsedMilliseconds, tags);
    }

    public void AuthenticationFailed(string path)
        => AuthFailures.Add(1, new System.Diagnostics.TagList { { "http.route", path } });

    public void LoginThrottled()
        => LoginsThrottled.Add(1);

    public void SignalRConnected()
        => SignalRConnections.Add(1);

    public void SignalRDisconnected()
        => SignalRConnections.Add(-1);
}
