using System;
using System.Linq;
using System.Reflection;
using FluentAssertions;
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

        updated.Should().Contain(new[] { nameof(InternalFunctionContext.Status), nameof(InternalFunctionContext.ElapsedTime), nameof(InternalFunctionContext.ReleaseLock) });
        updated.Count.Should().Be(3);

        context.Status.Should().Be(TickerStatus.InProgress);
        context.ElapsedTime.Should().Be(123L);
        context.ReleaseLock.Should().BeTrue();
    }

    [Fact]
    public void ResetUpdateProps_Clears_Tracked_Properties()
    {
        var context = new InternalFunctionContext();

        context
            .SetProperty(c => c.Status, TickerStatus.Done)
            .SetProperty(c => c.ElapsedTime, 500L);

        context.GetPropsToUpdate().Should().NotBeEmpty();

        context.ResetUpdateProps();

        context.GetPropsToUpdate().Should().BeEmpty();
    }

    [Fact]
    public void ResetUpdateProps_Does_Not_Reset_Property_Values()
    {
        var context = new InternalFunctionContext();

        context
            .SetProperty(c => c.Status, TickerStatus.Done)
            .SetProperty(c => c.ElapsedTime, 250L);

        context.ResetUpdateProps();

        context.Status.Should().Be(TickerStatus.Done);
        context.ElapsedTime.Should().Be(250L);
        context.GetPropsToUpdate().Should().BeEmpty();
    }

    [Fact]
    public void SetProperty_Reinitializes_Tracking_Set_When_Null()
    {
        var context = new InternalFunctionContext();

        // Simulate a null ParametersToUpdate set to verify the null-coalescing assignment.
        var parametersProperty = typeof(InternalFunctionContext)
            .GetProperty("ParametersToUpdate", BindingFlags.Instance | BindingFlags.Public);

        parametersProperty.Should().NotBeNull();
        parametersProperty!.SetValue(context, null);

        context.SetProperty(c => c.Status, TickerStatus.InProgress);

        var updated = context.GetPropsToUpdate();
        updated.Should().NotBeNull();
        updated.Should().Contain(nameof(InternalFunctionContext.Status));
    }

    [Fact]
    public void SetProperty_Allows_Multiple_Updates_To_Same_Property()
    {
        var context = new InternalFunctionContext();

        context
            .SetProperty(c => c.Status, TickerStatus.InProgress)
            .SetProperty(c => c.Status, TickerStatus.Failed);

        context.Status.Should().Be(TickerStatus.Failed);

        var updated = context.GetPropsToUpdate();
        updated.Should().Contain(nameof(InternalFunctionContext.Status));
        updated.Count.Should().Be(1);
    }

    [Fact]
    public void SetProperty_Throws_For_NonProperty_Expression()
    {
        var context = new InternalFunctionContext();

        Action act = () => context.SetProperty(c => c.ElapsedTime + 1, 10L);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*Expression must point to a property*");
    }

    [Fact]
    public void TimeTickerChildren_Defaults_To_Empty_List_And_Can_Add()
    {
        var context = new InternalFunctionContext();

        context.TimeTickerChildren.Should().NotBeNull();
        context.TimeTickerChildren.Should().BeEmpty();

        var child = new InternalFunctionContext
        {
            TickerId = Guid.NewGuid(),
            ParentId = Guid.NewGuid(),
            FunctionName = "ChildFunction"
        };

        context.TimeTickerChildren.Add(child);

        context.TimeTickerChildren.Should().ContainSingle();
        context.TimeTickerChildren.Single().FunctionName.Should().Be("ChildFunction");
    }

    [Fact]
    public void CachedDelegate_And_Priority_Can_Be_Assigned()
    {
        var context = new InternalFunctionContext();
        TickerFunctionDelegate handler = (_, _, _) => default!;

        context.CachedDelegate = handler;
        context.CachedPriority = TickerTaskPriority.High;

        context.CachedDelegate.Should().BeSameAs(handler);
        context.CachedPriority.Should().Be(TickerTaskPriority.High);
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

        context.RetryIntervals.Should().BeSameAs(intervals);
        context.ExceptionDetails.Should().Be(exceptionDetails);

        var updated = context.GetPropsToUpdate();
        updated.Should().Contain(new[]
        {
            nameof(InternalFunctionContext.RetryIntervals),
            nameof(InternalFunctionContext.ExceptionDetails)
        });
    }
}
