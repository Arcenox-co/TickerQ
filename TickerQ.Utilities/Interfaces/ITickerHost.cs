using System;

namespace TickerQ.Utilities.Interfaces
{
    public interface ITickerHost
    {
        void Start();
        void RestartIfNeeded(DateTime newOccurrence);
        void Restart();
        void Stop();
        bool IsRunning();
        DateTime? NextPlannedOccurrence { get; }
    }
}
