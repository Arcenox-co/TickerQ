namespace TickerQ.EntityFrameworkCore
{
    public class EfCoreOptionBuilder
    {
        internal bool UsesModelCustomizer { get; private set; }
        internal bool CancelMissedTickersOnReset { get; private set; }

        public void UseModelCustomizerForMigrations()
            => UsesModelCustomizer = true;
        
        public void CancelMissedTickersOnApplicationRestart()
            => CancelMissedTickersOnReset = true;
    }
}