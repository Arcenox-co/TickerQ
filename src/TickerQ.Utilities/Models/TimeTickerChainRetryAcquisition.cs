using System;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Enums;

namespace TickerQ.Utilities.Models;

/// <summary>The atomically acquired original chain and the root outcome preceding its retry.</summary>
public sealed class TimeTickerChainRetryAcquisition<TTimeTicker>
    where TTimeTicker : TimeTickerEntity<TTimeTicker>
{
    public required TTimeTicker Root { get; init; }
    public required Guid SelectedId { get; init; }
    public required TickerStatus PreviousRootStatus { get; init; }
}
