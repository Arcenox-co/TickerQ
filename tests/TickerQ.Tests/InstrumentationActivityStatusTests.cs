using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Microsoft.Extensions.Logging.Abstractions;
using TickerQ.Utilities;
using TickerQ.Utilities.Instrumentation;
using Xunit;

namespace TickerQ.Tests;

/// <summary>
/// Regression coverage for the OpenTelemetry activity contract emitted by
/// <see cref="ActivitySourceInstrumentation"/>: failed and cancelled jobs carry
/// <see cref="ActivityStatusCode.Error"/>, successful completion is
/// <see cref="ActivityStatusCode.Ok"/>, and no exception message, stack trace,
/// cancellation/skip reason, or status description ever leaks onto a tag — only
/// the stable exception type name is exported. The OpenTelemetry package's
/// instrumentation implements the identical contract against the same
/// <c>"TickerQ"</c> ActivitySource.
/// </summary>
public class InstrumentationActivityStatusTests
{
    private const string SensitiveText = "SENSITIVE password=hunter2 conn=Server=db;Pwd=secret";

    private static ActivitySourceInstrumentation NewInstrumentation()
        => new(NullLogger<ActivitySourceInstrumentation>.Instance, new SchedulerOptionsBuilder());

    private static List<Activity> Capture(Action act)
    {
        var captured = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "TickerQ",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = a => captured.Add(a),
        };
        ActivitySource.AddActivityListener(listener);
        act();
        return captured;
    }

    private static Activity Single(List<Activity> activities, string operationName, Guid jobId)
        => activities.Single(a => a.OperationName == operationName &&
                                  a.GetTagItem("tickerq.job.id") as string == jobId.ToString());

    private static void AssertNoSensitiveTags(Activity activity)
    {
        // No status description (a common place a reason leaks) and no tag key
        // that would carry a message/stack/reason.
        Assert.Null(activity.StatusDescription);
        var keys = activity.TagObjects.Select(t => t.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var forbidden in new[]
                 {
                     "tickerq.job.error_message", "tickerq.job.exception", "tickerq.job.reason",
                     "tickerq.job.skip_reason", "tickerq.job.stacktrace", "tickerq.job.stack_trace",
                     "exception.message", "exception.stacktrace", "exception.type", "otel.status_description",
                 })
            Assert.DoesNotContain(forbidden, keys);

        foreach (var value in activity.TagObjects.Select(t => t.Value?.ToString()))
            if (value != null)
                Assert.DoesNotContain("SENSITIVE", value, StringComparison.Ordinal);
    }

    [Fact]
    public void LogJobFailed_SetsErrorStatus_ExportsOnlyErrorTypeName_NoSensitiveTags()
    {
        var jobId = Guid.NewGuid();
        var instrumentation = NewInstrumentation();

        var activities = Capture(() =>
            instrumentation.LogJobFailed(jobId, "fn", new InvalidOperationException(SensitiveText), retryCount: 3));

        var activity = Single(activities, "tickerq.job.failed", jobId);
        Assert.Equal(ActivityStatusCode.Error, activity.Status);
        Assert.Equal(nameof(InvalidOperationException), activity.GetTagItem("tickerq.job.error_type"));
        Assert.Equal(3, activity.GetTagItem("tickerq.job.retry_count"));
        AssertNoSensitiveTags(activity);
    }

    [Fact]
    public void LogJobCancelled_SetsErrorStatus_NoReasonTagOrSensitiveLeak()
    {
        var jobId = Guid.NewGuid();
        var instrumentation = NewInstrumentation();

        var activities = Capture(() =>
            instrumentation.LogJobCancelled(jobId, "fn", reason: SensitiveText));

        var activity = Single(activities, "tickerq.job.cancelled", jobId);
        Assert.Equal(ActivityStatusCode.Error, activity.Status);
        AssertNoSensitiveTags(activity);
    }

    [Fact]
    public void LogJobSkipped_DoesNotLeakReason()
    {
        var jobId = Guid.NewGuid();
        var instrumentation = NewInstrumentation();

        var activities = Capture(() =>
            instrumentation.LogJobSkipped(jobId, "fn", reason: SensitiveText));

        var activity = Single(activities, "tickerq.job.skipped", jobId);
        AssertNoSensitiveTags(activity);
    }

    [Fact]
    public void LogJobCompleted_MapsSuccessToOk_AndFailureToError()
    {
        var okId = Guid.NewGuid();
        var errId = Guid.NewGuid();
        var instrumentation = NewInstrumentation();

        var activities = Capture(() =>
        {
            instrumentation.LogJobCompleted(okId, "fn", executionTimeMs: 12, success: true);
            instrumentation.LogJobCompleted(errId, "fn", executionTimeMs: 12, success: false);
        });

        Assert.Equal(ActivityStatusCode.Ok, Single(activities, "tickerq.job.completed", okId).Status);
        Assert.Equal(ActivityStatusCode.Error, Single(activities, "tickerq.job.completed", errId).Status);
    }
}
