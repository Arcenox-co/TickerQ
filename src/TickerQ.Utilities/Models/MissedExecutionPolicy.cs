using System;
using TickerQ.Utilities.Enums;

namespace TickerQ.Utilities.Models;

/// <summary>Controls whether a persisted execution may start after its scheduled instant.</summary>
public sealed class MissedExecutionPolicy
{
    public MissedExecutionAction Action { get; private init; }
    public TimeSpan? MaximumLateness { get; private init; }

    /// <summary>Preserve TickerQ's at-least-once behavior and execute once whenever capacity returns.</summary>
    public static MissedExecutionPolicy RunOnce() => new() { Action = MissedExecutionAction.RunOnce };

    /// <summary>Skip without invoking user code when execution is later than <paramref name="maximumLateness"/>.</summary>
    public static MissedExecutionPolicy SkipAfter(TimeSpan maximumLateness)
    {
        if (maximumLateness < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(maximumLateness), "Maximum lateness cannot be negative.");
        return new MissedExecutionPolicy
        {
            Action = MissedExecutionAction.Skip,
            MaximumLateness = maximumLateness,
        };
    }
}
