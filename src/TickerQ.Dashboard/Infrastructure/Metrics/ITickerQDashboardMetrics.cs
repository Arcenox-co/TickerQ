namespace TickerQ.Dashboard.Infrastructure.Metrics;

/// <summary>
/// Observability hooks for the dashboard branch. The default implementation
/// (<see cref="MeterDashboardMetrics"/>) publishes through a
/// <c>System.Diagnostics.Metrics.Meter</c> named <c>TickerQ.Dashboard</c> —
/// add that meter to an OpenTelemetry <c>MeterProviderBuilder</c> to export
/// it. Customers who want the raw callbacks (custom dashboards, StatsD, …)
/// register their own implementation before <c>AddTickerQ</c>:
/// <code>services.AddSingleton&lt;ITickerQDashboardMetrics, MyMetrics&gt;();</code>
/// </summary>
public interface ITickerQDashboardMetrics
{
    /// <summary>A dashboard API request finished.</summary>
    void RequestCompleted(string method, string path, int statusCode, double elapsedMilliseconds);

    /// <summary>A request failed authentication (401). High rates signal brute-force attempts.</summary>
    void AuthenticationFailed(string path);

    /// <summary>A login attempt was rejected by the rate limiter (429).</summary>
    void LoginThrottled();

    /// <summary>A SignalR client connected to the notification hub.</summary>
    void SignalRConnected();

    /// <summary>A SignalR client disconnected from the notification hub.</summary>
    void SignalRDisconnected();
}
