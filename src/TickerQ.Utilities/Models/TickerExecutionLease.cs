using System;
using TickerQ.Utilities.Enums;

namespace TickerQ.Utilities.Models
{
    public readonly record struct TickerExecutionLease(
        TickerType Type,
        Guid TickerId,
        Guid? AcquisitionToken);
}
