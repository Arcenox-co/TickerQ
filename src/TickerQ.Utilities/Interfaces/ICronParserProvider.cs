using System;

#nullable enable

namespace TickerQ.Utilities.Interfaces
{
    public interface ICronParserProvider
    {
        bool TryGetNextOccurrence(string expression,
                                    DateTimeOffset dateTime,
                                    out DateTime nextOccurrence);
    }
}