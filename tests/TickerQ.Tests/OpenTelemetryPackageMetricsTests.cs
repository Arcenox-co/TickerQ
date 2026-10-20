using System.Diagnostics.Metrics;
using Microsoft.Extensions.DependencyInjection;
using TickerQ.DependencyInjection;
using TickerQ.Instrumentation.OpenTelemetry;
using TickerQ.Utilities.Instrumentation;

namespace TickerQ.Tests;

public class OpenTelemetryPackageMetricsTests
{
    [Fact]
    public void OptionalInstrumentation_PreservesEnqueueCounter()
    {
        var function = "metric-test-" + Guid.NewGuid();
        long count = 0;
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meter) =>
        {
            if (instrument.Meter.Name == TickerQMetrics.MeterName && instrument.Name == "tickerq.jobs.enqueued")
                meter.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((_, value, tags, _) =>
        {
            foreach (var tag in tags)
                if (tag.Key == "tickerq.function" && Equals(tag.Value, function)) Interlocked.Add(ref count, value);
        });
        listener.Start();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTickerQ(options =>
        {
            options.UseApplicationNamespace("otel-metrics-test").UseReconciliationEpoch(1);
            options.DisableBackgroundServices();
            options.AddOpenTelemetryInstrumentation();
        });
        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<ITickerQInstrumentation>().LogJobEnqueued("TimeTicker", function, Guid.NewGuid());
        Assert.Equal(1, count);
    }
}
