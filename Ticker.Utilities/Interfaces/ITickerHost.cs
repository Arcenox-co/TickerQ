using System;

namespace TickerQ.Utilities.Interfaces
{
    public interface ITickerHost
    {
        void Run();
        void RestartIfNeeded(DateTime newOccurrence);
        void Restart();
        void Stop();
        DateTimeOffset? NextPlannedOccurrence { get; }
    }
}
