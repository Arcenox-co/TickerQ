namespace TickerQ.Utilities
{
    public class TickerProviderOptions
    {
        public bool Tracking { get; private set; } = false;
        
        public void SetAsTracking() => Tracking = true;
        public void SetAsNoTracking() => Tracking = false;
    }
}