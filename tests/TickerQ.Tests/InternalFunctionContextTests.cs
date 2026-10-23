using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities;
using TickerQ.Utilities.Infrastructure;
using TickerQ.Utilities.Models;
using Xunit;

namespace TickerQ.Tests;

public class InternalFunctionContextTests
{
    [Fact]
    public void CacheFunctionReferences_RecognizesParallelGroupWithoutUserRegistration()
    {
        var group = new InternalFunctionContext
        {
            FunctionName = TickerChainInfrastructure.ParallelGroupFunction,
            TimeTickerChildren = []
        };

        TickerExecutionContext.CacheFunctionReferences([group]);

        Assert.Same(TickerChainInfrastructure.ParallelGroupDelegate, group.CachedDelegate);
        Assert.Equal(TickerTaskPriority.Normal, group.CachedPriority);
        Assert.Equal(0, group.CachedMaxConcurrency);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TypedSetters_PreserveExpressionUpdates_WithNullTrackingSet(bool useDefaults)
    {
        var expected = new InternalFunctionContext { ParametersToUpdate = null! };
        var actual = new InternalFunctionContext { ParametersToUpdate = null! };
        var status = useDefaults ? default : TickerStatus.Done;
        var executedAt = useDefaults ? default : new DateTime(2026, 10, 20, 12, 0, 0, DateTimeKind.Utc);
        var executionTime = useDefaults ? default : executedAt.AddMinutes(-1);
        var elapsedTime = useDefaults ? 0L : 125L;
        var retryCount = useDefaults ? 0 : 2;
        var exceptionDetails = useDefaults ? null : "attempt failed";
        var result = useDefaults ? null : new TickerResultEnvelope(
            [0x31], TickerResultEnvelope.CurrentVersion, "application/json");

        expected.SetProperty(c => c.Status, status)
            .SetProperty(c => c.ExecutedAt, executedAt)
            .SetProperty(c => c.ElapsedTime, elapsedTime)
            .SetProperty(c => c.ExceptionDetails, exceptionDetails)
            .SetProperty(c => c.ResultEnvelope, result)
            .SetProperty(c => c.RetryCount, retryCount)
            .SetProperty(c => c.ReleaseLock, !useDefaults)
            .SetProperty(c => c.ExecutionTime, executionTime);
        var returned = actual.SetStatus(status)
            .SetExecutedAt(executedAt)
            .SetElapsedTime(elapsedTime)
            .SetExceptionDetails(exceptionDetails)
            .SetResultEnvelope(result)
            .SetRetryCount(retryCount)
            .SetReleaseLock(!useDefaults)
            .SetExecutionTime(executionTime);

        Assert.Same(actual, returned);
        Assert.Equal(expected.Status, actual.Status);
        Assert.Equal(expected.ExecutedAt, actual.ExecutedAt);
        Assert.Equal(expected.ElapsedTime, actual.ElapsedTime);
        Assert.Equal(expected.ExceptionDetails, actual.ExceptionDetails);
        Assert.Same(expected.ResultEnvelope, actual.ResultEnvelope);
        Assert.Equal(expected.RetryCount, actual.RetryCount);
        Assert.Equal(expected.ReleaseLock, actual.ReleaseLock);
        Assert.Equal(expected.ExecutionTime, actual.ExecutionTime);
        Assert.True(expected.GetPropsToUpdate().SetEquals(actual.GetPropsToUpdate()));
        Assert.Equal(8, actual.GetPropsToUpdate().Count);
    }

    [Fact]
    public void TypedSetters_TrackUnchangedStatusAndExplicitResultAbsenceAfterReset()
    {
        var result = new TickerResultEnvelope([0x31], TickerResultEnvelope.CurrentVersion, "application/json");
        var context = new InternalFunctionContext()
            .SetStatus(TickerStatus.Done)
            .SetElapsedTime(125L)
            .SetResultEnvelope(result);
        var tracking = context.GetPropsToUpdate();

        context.ResetUpdateProps();

        Assert.Same(tracking, context.GetPropsToUpdate());
        Assert.Empty(tracking);
        Assert.Equal(TickerStatus.Done, context.Status);
        Assert.Equal(125L, context.ElapsedTime);
        Assert.Same(result, context.ResultEnvelope);

        context.SetStatus(TickerStatus.Done)
            .SetResultEnvelope(null)
            .SetResultEnvelope(null)
            .SetExceptionDetails(null);

        Assert.Null(context.ResultEnvelope);
        Assert.Null(context.ExceptionDetails);
        Assert.True(tracking.SetEquals([
            nameof(InternalFunctionContext.Status),
            nameof(InternalFunctionContext.ResultEnvelope),
            nameof(InternalFunctionContext.ExceptionDetails)
        ]));
        Assert.DoesNotContain(nameof(InternalFunctionContext.ElapsedTime), tracking);
    }

    [Fact]
    public void TypedSetters_RespectReplacementTrackingSet_AndMixWithPublicUpdates()
    {
        var context = new InternalFunctionContext().SetStatus(TickerStatus.Done);
        var original = context.GetPropsToUpdate();
        var replacement = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "status" };
        context.ParametersToUpdate = replacement;

        context.SetStatus(TickerStatus.Done)
            .SetRetryCount(2)
            .SetProperty(c => c.ReleaseLock, true);

        Assert.Same(replacement, context.GetPropsToUpdate());
        Assert.Equal(3, replacement.Count);
        Assert.Contains(nameof(InternalFunctionContext.RetryCount), replacement);
        Assert.Contains(nameof(InternalFunctionContext.ReleaseLock), replacement);
        Assert.Single(original);
        Assert.Contains(nameof(InternalFunctionContext.Status), original);

        context.ResetUpdateProps();

        Assert.Same(replacement, context.GetPropsToUpdate());
        Assert.Empty(replacement);
        Assert.Single(original);
        Assert.Equal(TickerStatus.Done, context.Status);
        Assert.Equal(2, context.RetryCount);
        Assert.True(context.ReleaseLock);
    }

    [Fact]
    public void SetProperty_Tracks_Updated_Properties()
    {
        var context = new InternalFunctionContext();

        context
            .SetProperty(c => c.Status, TickerStatus.InProgress)
            .SetProperty(c => c.ElapsedTime, 123L)
            .SetProperty(c => c.ReleaseLock, true);

        var updated = context.GetPropsToUpdate();

        Assert.Contains(nameof(InternalFunctionContext.Status), updated);
        Assert.Contains(nameof(InternalFunctionContext.ElapsedTime), updated);
        Assert.Contains(nameof(InternalFunctionContext.ReleaseLock), updated);
        Assert.Equal(3, updated.Count);

        Assert.Equal(TickerStatus.InProgress, context.Status);
        Assert.Equal(123L, context.ElapsedTime);
        Assert.True(context.ReleaseLock);
    }

    [Fact]
    public void ResetUpdateProps_Clears_Tracked_Properties()
    {
        var context = new InternalFunctionContext();

        context
            .SetProperty(c => c.Status, TickerStatus.Done)
            .SetProperty(c => c.ElapsedTime, 500L);

        Assert.NotEmpty(context.GetPropsToUpdate());

        context.ResetUpdateProps();

        Assert.Empty(context.GetPropsToUpdate());
    }

    [Fact]
    public void ResetUpdateProps_Does_Not_Reset_Property_Values()
    {
        var context = new InternalFunctionContext();

        context
            .SetProperty(c => c.Status, TickerStatus.Done)
            .SetProperty(c => c.ElapsedTime, 250L);

        context.ResetUpdateProps();

        Assert.Equal(TickerStatus.Done, context.Status);
        Assert.Equal(250L, context.ElapsedTime);
        Assert.Empty(context.GetPropsToUpdate());
    }

    [Fact]
    public void SetProperty_Reinitializes_Tracking_Set_When_Null()
    {
        var context = new InternalFunctionContext();

        // Simulate a null ParametersToUpdate set to verify the null-coalescing assignment.
        var parametersProperty = typeof(InternalFunctionContext)
            .GetProperty("ParametersToUpdate", BindingFlags.Instance | BindingFlags.Public);

        Assert.NotNull(parametersProperty);
        parametersProperty!.SetValue(context, null);

        context.SetProperty(c => c.Status, TickerStatus.InProgress);

        var updated = context.GetPropsToUpdate();
        Assert.NotNull(updated);
        Assert.Contains(nameof(InternalFunctionContext.Status), updated);
    }

    [Fact]
    public void SetProperty_Allows_Multiple_Updates_To_Same_Property()
    {
        var context = new InternalFunctionContext();

        context
            .SetProperty(c => c.Status, TickerStatus.InProgress)
            .SetProperty(c => c.Status, TickerStatus.Failed);

        Assert.Equal(TickerStatus.Failed, context.Status);

        var updated = context.GetPropsToUpdate();
        Assert.Contains(nameof(InternalFunctionContext.Status), updated);
        Assert.Single(updated);
    }

    [Fact]
    public void SetProperty_Throws_For_NonProperty_Expression()
    {
        var context = new InternalFunctionContext();

        var ex = Assert.Throws<ArgumentException>(() => context.SetProperty(c => c.ElapsedTime + 1, 10L));

        Assert.Contains("Expression must point to a property", ex.Message);
    }

    [Fact]
    public void TimeTickerChildren_Defaults_To_Empty_List_And_Can_Add()
    {
        var context = new InternalFunctionContext();

        Assert.NotNull(context.TimeTickerChildren);
        Assert.Empty(context.TimeTickerChildren);

        var child = new InternalFunctionContext
        {
            TickerId = Guid.NewGuid(),
            ParentId = Guid.NewGuid(),
            FunctionName = "ChildFunction"
        };

        context.TimeTickerChildren.Add(child);

        Assert.Single(context.TimeTickerChildren);
        Assert.Equal("ChildFunction", context.TimeTickerChildren.Single().FunctionName);
    }

    [Fact]
    public void CachedDelegate_And_Priority_Can_Be_Assigned()
    {
        var context = new InternalFunctionContext();
        TickerFunctionDelegate handler = (_, _, _) => default!;

        context.CachedDelegate = handler;
        context.CachedPriority = TickerTaskPriority.High;

        Assert.Same(handler, context.CachedDelegate);
        Assert.Equal(TickerTaskPriority.High, context.CachedPriority);
    }

    [Fact]
    public void SetProperty_Supports_Array_And_String_Properties()
    {
        var context = new InternalFunctionContext();
        var intervals = new[] { 1, 5, 10 };
        const string exceptionDetails = "Something went wrong";

        context
            .SetProperty(c => c.RetryIntervals, intervals)
            .SetProperty(c => c.ExceptionDetails, exceptionDetails);

        Assert.Same(intervals, context.RetryIntervals);
        Assert.Equal(exceptionDetails, context.ExceptionDetails);

        var updated = context.GetPropsToUpdate();
        Assert.Contains(nameof(InternalFunctionContext.RetryIntervals), updated);
        Assert.Contains(nameof(InternalFunctionContext.ExceptionDetails), updated);
    }
}
