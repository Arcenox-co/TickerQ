using System;
using System.Threading.Tasks;

namespace TickerQ.Utilities.Interfaces
{
    public interface ITickerHost
    {
        void Run();
        void RestartIfNeeded(DateTime newOccurrence);
        void Restart();
        void Stop();
        bool IsRunning();
    }
}
