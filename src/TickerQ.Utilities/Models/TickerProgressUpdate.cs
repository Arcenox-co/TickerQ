using System;
using TickerQ.Utilities.Enums;

namespace TickerQ.Utilities.Models;

/// <summary>
/// A generation-fenced progress update for one active execution. Persistence providers must
/// accept it only while the same execution generation is still InProgress.
/// </summary>
public sealed class TickerProgressUpdate
{
    public Guid TickerId { get; init; }
    public TickerType Type { get; init; }
    public Guid? ParentId { get; init; }
    public Guid? ChainRootId { get; init; }
    public Guid? ChainGeneration { get; init; }
    public Guid? AcquisitionToken { get; init; }
    public double Percent { get; init; }
    public string Message { get; init; }
    public DateTime UpdatedAtUtc { get; init; }
}
