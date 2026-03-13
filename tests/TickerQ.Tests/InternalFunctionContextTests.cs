using System;
using System.Linq;
using System.Reflection;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities;
using TickerQ.Utilities.Models;
using Xunit;

namespace TickerQ.Tests;

public class InternalFunctionContextTests
{
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
        Assert.Equal(1, updated.Count);
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
