using System;

namespace TickerQ.Utilities.Interfaces;

internal interface ITickerOptionsBuilder
{
    internal TimeSpan TimeOutChecker { get; }
}